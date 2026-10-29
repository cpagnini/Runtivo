using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;

namespace Runtivo.Api.Matching;

public record InterestInput(Guid? ToUserId);
public record InterestResultDto(bool Mutual, Guid? RunId);

public static class InterestEndpoints
{
    public static void MapInterestEndpoints(this WebApplication app)
    {
        // «Mi interessa»: privato. Solo verso persone compatibili adesso; per ogni altro id la risposta è 404,
        // così l'endpoint non si può usare per sondare chi esiste o chi ha un annuncio.
        app.MapPost("/api/interests", async ([FromBody] InterestInput dto, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var me = p.UserId();
            var now = time.GetUtcNow();
            if (dto.ToUserId is not { } toUserId) return Http.Problem(400, "Indica la persona.");
            if (toUserId == me) return Http.Problem(400, "Non puoi mostrare interesse per te stesso.");

            var mine = await MatchService.LoadMineAsync(db, me, now, ct);
            if (mine is null) return Http.Problem(409, "Pubblica o riattiva il tuo annuncio per mostrare interesse.");
            var other = await MatchService.FindOneAsync(db, mine, toUserId, now, ct);
            if (other is null) return Http.Problem(404, "Persona non trovata.");
            // Un'uscita a due richiede che entrambi accettino di correre in coppia; altrimenti si propone un gruppo.
            if (!(mine.Intent.AllowPairs && other.Intent.AllowPairs))
                return Http.Problem(400, "Una di voi due preferisce uscite di gruppo: proponi un'uscita con almeno altre due persone.");

            if (!await db.Interests.AnyAsync(i => i.FromUserId == me && i.ToUserId == toUserId, ct))
            {
                db.Interests.Add(new Interest { FromUserId = me, ToUserId = toUserId, CreatedAt = now });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); /* richiesta ripetuta in parallelo: esiste già */ }
            }

            var reciprocal = await db.Interests.AnyAsync(i => i.FromUserId == toUserId && i.ToUserId == me, ct);
            if (!reciprocal) return Results.Ok(new InterestResultDto(false, null));

            return Results.Ok(new InterestResultDto(true, await EnsureProposalAsync(db, mine, other, time, ct)));
        }).RequireAuthorization();

        app.MapDelete("/api/interests/{userId:guid}", async (Guid userId, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var me = p.UserId();
            await db.Interests.Where(i => i.FromUserId == me && i.ToUserId == userId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    /// <summary>
    /// Crea la proposta di uscita tra due persone con interesse reciproco, oppure restituisce quella già attiva.
    /// Un lock sulla coppia serializza le richieste simultanee dei due: ne nasce una sola.
    /// </summary>
    private static async Task<Guid?> EnsureProposalAsync(RuntivoDbContext db, MyIntentContext mine, Compatible other, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var me = mine.Intent.UserId;
        var pairKey = string.Join(':', new[] { me, other.UserId }.OrderBy(x => x));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({pairKey}, 0))", ct);

        var existing = await MatchService.ActiveRunsWithAsync(db, me, [other.UserId], now, ct);
        if (existing.TryGetValue(other.UserId, out var runId)) return runId;

        var startsAt = RunSuggestions.NextSharedSlot(other.Score.SharedMask, now);
        if (startsAt is null) return null;

        var places = await db.PublicPlaces.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var place = RunSuggestions.BestMeetingPlace(places, mine.Place, other.Place);
        var pace = RunSuggestions.Overlap(mine.Intent.PaceMinSecPerKm, mine.Intent.PaceMaxSecPerKm, other.Intent.PaceMinSecPerKm, other.Intent.PaceMaxSecPerKm);
        var distance = RunSuggestions.Overlap(mine.Intent.DistanceMinMeters, mine.Intent.DistanceMaxMeters, other.Intent.DistanceMinMeters, other.Intent.DistanceMaxMeters);

        var run = new SpontaneousRun
        {
            Id = Guid.NewGuid(), CreatedByUserId = me, PlaceId = place.Id, StartsAtUtc = startsAt.Value,
            PaceMinSecPerKm = pace?.Min, PaceMaxSecPerKm = pace?.Max,
            // Distanza proposta: il punto medio dell'intervallo comune, arrotondato a 500 m.
            DistanceMeters = distance is { } d ? (int)(Math.Round((d.Min + d.Max) / 2.0 / 500) * 500) : null,
            CreatedAt = now, UpdatedAt = now,
        };
        db.SpontaneousRuns.Add(run);
        db.RunParticipants.Add(new RunParticipant { RunId = run.Id, UserId = me, Status = ParticipantStatus.Pending, UpdatedAt = now });
        db.RunParticipants.Add(new RunParticipant { RunId = run.Id, UserId = other.UserId, Status = ParticipantStatus.Pending, UpdatedAt = now });
        db.Audit(time, me, "run.propose", "SpontaneousRun", run.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return run.Id;
    }
}
