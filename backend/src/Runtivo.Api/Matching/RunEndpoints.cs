using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Microsoft.Extensions.Options;
using Runtivo.Api.Data;
using Runtivo.Api.Safety;

namespace Runtivo.Api.Matching;

public record RunParticipantDto(Guid UserId, string DisplayName, ParticipantStatus Status);
public record RunDto(Guid Id, RunStatus Status, string? CancelReason, DateTimeOffset StartsAtUtc, string TimeZoneId, PlaceDto Place,
    int? DistanceMeters, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, List<RunParticipantDto> Participants, ParticipantStatus MyStatus, bool IsOrganizer);
public record CreateRunDto(Guid? PlaceId, DateTimeOffset? StartsAt, int? DistanceMeters, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, List<Guid>? InviteeIds);
public record RunSummaryDto(Guid Id, RunStatus Status, DateTimeOffset StartsAtUtc, string PlaceName, string City, ParticipantStatus MyStatus, int ActiveParticipants);

public static class RunEndpoints
{
    private const int MaxInvitees = 7;
    private const int MaxOpenProposals = 3;
    private static readonly TimeSpan MinNotice = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxAhead = TimeSpan.FromDays(60);

    public static void MapRunEndpoints(this WebApplication app)
    {
        app.MapPost("/api/runs", async (CreateRunDto dto, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, IOptions<AppOptions> opts, CancellationToken ct) =>
        {
            var me = p.UserId();
            var now = time.GetUtcNow();

            var mine = await MatchService.LoadMineAsync(db, me, now, ct);
            if (mine is null) return Http.Problem(409, "Pubblica o riattiva il tuo annuncio per proporre un'uscita.");

            if (dto.PlaceId is not { } placeId || !await db.PublicPlaces.AnyAsync(x => x.Id == placeId && x.IsActive && x.IsCurated, ct))
                return Http.Problem(400, "Scegli un luogo dall'elenco.");
            if (dto.StartsAt is not { } startsAt || startsAt < now + MinNotice) return Http.Problem(400, "L'uscita deve iniziare tra almeno un'ora.");
            if (startsAt > now + MaxAhead) return Http.Problem(400, "L'uscita può essere al massimo tra 60 giorni.");
            if (dto.InviteeIds is not { Count: >= 1 and <= MaxInvitees } invitees) return Http.Problem(400, $"Invita da 1 a {MaxInvitees} persone.");
            if (invitees.Distinct().Count() != invitees.Count || invitees.Contains(me)) return Http.Problem(400, "L'elenco delle persone invitate non è valido.");
            if ((dto.PaceMinSecPerKm is null) != (dto.PaceMaxSecPerKm is null)) return Http.Problem(400, "Indica il passo da e a, oppure nessuno dei due.");
            if (dto.PaceMinSecPerKm is { } lo && dto.PaceMaxSecPerKm is { } hi && (lo is < 120 or > 1200 || hi is < 120 or > 1200 || lo > hi))
                return Http.Problem(400, "Il passo non è valido.");
            if (dto.DistanceMeters is { } dist && (dist < 1000 || dist > 60000)) return Http.Problem(400, "La distanza deve essere tra 1 e 60 km.");

            // Conteggio delle proposte in attesa e inserimento sono atomici (lock per persona): richieste parallele non superano il tetto.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await NewAccountLimits.LockAsync(db, "run", me, ct);
            var isNew = await NewAccountLimits.IsNewAsync(db, me, now, opts.Value.NewAccountDays, ct);
            var maxOpen = isNew ? NewAccountLimits.OpenProposals : MaxOpenProposals;
            var open = await db.SpontaneousRuns.CountAsync(r => r.CreatedByUserId == me && r.Status == RunStatus.Proposed && r.StartsAtUtc > now, ct);
            if (open >= maxOpen)
                return Http.Problem(409, isNew
                    ? "Hai creato l'account da poco: puoi avere una sola proposta in attesa alla volta."
                    : $"Hai già {MaxOpenProposals} proposte in attesa: aspetta le risposte prima di farne altre.");

            // Si invitano solo persone compatibili adesso: per ogni altro id la risposta è 404.
            var people = new List<Compatible>();
            foreach (var id in invitees)
            {
                var other = await MatchService.FindOneAsync(db, mine, id, now, ct);
                if (other is null) return Http.Problem(404, "Persona non trovata.");
                people.Add(other);
            }
            if (people.Count == 1 && !(mine.Intent.AllowPairs && people[0].Intent.AllowPairs))
                return Http.Problem(400, "Una di voi due preferisce uscite di gruppo: invita almeno altre due persone.");

            var run = new SpontaneousRun
            {
                Id = Guid.NewGuid(), CreatedByUserId = me, PlaceId = placeId, StartsAtUtc = startsAt.ToUniversalTime(),
                DistanceMeters = dto.DistanceMeters, PaceMinSecPerKm = dto.PaceMinSecPerKm, PaceMaxSecPerKm = dto.PaceMaxSecPerKm,
                CreatedAt = now, UpdatedAt = now,
            };
            db.SpontaneousRuns.Add(run);
            db.RunParticipants.Add(new RunParticipant { RunId = run.Id, UserId = me, Status = ParticipantStatus.Confirmed, UpdatedAt = now });
            foreach (var other in people)
                db.RunParticipants.Add(new RunParticipant { RunId = run.Id, UserId = other.UserId, Status = ParticipantStatus.Pending, UpdatedAt = now });
            db.Audit(time, me, "run.create", "SpontaneousRun", run.Id);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/runs/{run.Id}", new { run.Id });
        }).RequireAuthorization();

        app.MapPost("/api/runs/{runId:guid}/confirm", (Guid runId, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
            ChangeAsync(db, time, runId, p.UserId(), RunAction.Confirm, ct)).RequireAuthorization();
        app.MapPost("/api/runs/{runId:guid}/decline", (Guid runId, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
            ChangeAsync(db, time, runId, p.UserId(), RunAction.Decline, ct)).RequireAuthorization();
        app.MapPost("/api/runs/{runId:guid}/cancel", (Guid runId, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
            ChangeAsync(db, time, runId, p.UserId(), RunAction.Cancel, ct)).RequireAuthorization();
        // Solo chi partecipa (in qualunque stato) vede l'uscita e i nomi: per gli altri è 404.
        app.MapGet("/api/runs/{runId:guid}", async (Guid runId, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var dto = await LoadRunDtoAsync(db, runId, p.UserId(), ct);
            return dto is null ? Http.Problem(404, "Uscita non trovata.") : Results.Ok(dto);
        }).RequireAuthorization();

        app.MapGet("/api/me/runs", async (ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var me = p.UserId();
            var now = time.GetUtcNow();
            var rows = await (from part in db.RunParticipants
                              join run in db.SpontaneousRuns on part.RunId equals run.Id
                              join pl in db.PublicPlaces on run.PlaceId equals pl.Id
                              where part.UserId == me && (part.Status == ParticipantStatus.Pending || part.Status == ParticipantStatus.Confirmed)
                                    && run.Status != RunStatus.Cancelled && run.StartsAtUtc > now
                              orderby run.StartsAtUtc
                              select new RunSummaryDto(run.Id, run.Status, run.StartsAtUtc, pl.Name, pl.City, part.Status,
                                  db.RunParticipants.Count(x => x.RunId == run.Id && (x.Status == ParticipantStatus.Pending || x.Status == ParticipantStatus.Confirmed))))
                .Take(100).ToListAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization();
    }

    internal static async Task<RunDto?> LoadRunDtoAsync(RuntivoDbContext db, Guid runId, Guid viewerId, CancellationToken ct)
    {
        var participants = await (from part in db.RunParticipants.AsNoTracking()
                                  join u in db.Users.AsNoTracking() on part.UserId equals u.Id
                                  where part.RunId == runId
                                  orderby part.UpdatedAt
                                  select new RunParticipantDto(part.UserId, u.DisplayName, part.Status)).ToListAsync(ct);
        var mine = participants.FirstOrDefault(x => x.UserId == viewerId);
        if (mine is null) return null;

        var row = await (from run in db.SpontaneousRuns.AsNoTracking()
                         join pl in db.PublicPlaces.AsNoTracking() on run.PlaceId equals pl.Id
                         where run.Id == runId
                         select new { Run = run, Place = pl }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var r = row.Run;
        return new RunDto(r.Id, r.Status, r.CancelReason, r.StartsAtUtc, r.TimeZoneId, new PlaceDto(row.Place.Id, row.Place.Name, row.Place.City),
            r.DistanceMeters, r.PaceMinSecPerKm, r.PaceMaxSecPerKm, participants, mine.Status, r.CreatedByUserId == viewerId);
    }

    private enum RunAction { Confirm, Decline, Cancel }

    /// <summary>
    /// Conferma, rifiuto/uscita o annullamento. La riga dell'uscita viene bloccata per tutta la transazione: richieste
    /// simultanee dei partecipanti si accodano e lo stato finale deriva sempre da <see cref="RunRules.Resolve"/>.
    /// </summary>
    private static async Task<IResult> ChangeAsync(RuntivoDbContext db, TimeProvider time, Guid runId, Guid userId, RunAction action, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SpontaneousRuns\" WHERE \"Id\" = {runId} FOR UPDATE", ct);

        var run = await db.SpontaneousRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        var participants = await db.RunParticipants.Where(x => x.RunId == runId).ToListAsync(ct);
        var me = participants.FirstOrDefault(x => x.UserId == userId);
        if (run is null || me is null) return Http.Problem(404, "Uscita non trovata.");

        if (action == RunAction.Cancel)
        {
            if (run.CreatedByUserId != userId) return Http.Problem(403, "Solo chi ha proposto l'uscita può annullarla.");
            if (run.Status != RunStatus.Cancelled)
            {
                if (run.StartsAtUtc <= now) return Http.Problem(409, "L'uscita è già iniziata.");
                run.Status = RunStatus.Cancelled;
                run.CancelReason = "Annullata da chi l'aveva proposta.";
            }
        }
        else
        {
            if (run.Status == RunStatus.Cancelled) return Http.Problem(409, "L'uscita è stata annullata.");
            if (run.StartsAtUtc <= now) return Http.Problem(409, "L'uscita è già iniziata.");

            if (action == RunAction.Confirm)
            {
                if (me.Status is ParticipantStatus.Declined or ParticipantStatus.Left) return Http.Problem(409, "Hai già lasciato questa uscita.");
                me.Status = ParticipantStatus.Confirmed;
            }
            else if (me.Status == ParticipantStatus.Pending) me.Status = ParticipantStatus.Declined;
            else if (me.Status == ParticipantStatus.Confirmed) me.Status = ParticipantStatus.Left;
            me.UpdatedAt = now;

            var outcome = await ResolveAsync(db, participants, ct);
            run.Status = outcome.Status;
            run.CancelReason = outcome.CancelReason;
        }

        run.UpdatedAt = now;
        db.Audit(time, userId, $"run.{action.ToString().ToLowerInvariant()}", "SpontaneousRun", runId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(await LoadRunDtoAsync(db, runId, userId, ct));
    }

    /// <summary>Stato dell'uscita dato l'elenco attuale; chi non ha più un annuncio conta come disposto alla coppia.</summary>
    internal static async Task<RunOutcome> ResolveAsync(RuntivoDbContext db, List<RunParticipant> participants, CancellationToken ct)
    {
        var ids = participants.Select(x => x.UserId).ToList();
        var noPairs = (await db.RunIntents.AsNoTracking().Where(i => ids.Contains(i.UserId) && !i.AllowPairs).Select(i => i.UserId).ToListAsync(ct)).ToHashSet();
        return RunRules.Resolve(participants.Select(x => new ParticipantState(x.UserId, x.Status, !noPairs.Contains(x.UserId))));
    }
}
