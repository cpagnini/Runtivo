using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Matching;

namespace Runtivo.Api.Safety;

public record SafetyViewDto(string SharedBy, DateTimeOffset StartsAtUtc, string TimeZoneId, string PlaceName, string City, RunStatus Status,
    List<string> Participants, DateTimeOffset ExpiresAt);

public static class ShareEndpoints
{
    private const int MaxActiveShares = 5;
    private static readonly TimeSpan GraceAfterStart = TimeSpan.FromHours(6);

    public static void MapShareEndpoints(this WebApplication app)
    {
        // Chi partecipa può dare a una persona di fiducia un link per vedere dove, quando e con chi corre.
        // Il token in chiaro si mostra una sola volta; nel DB resta solo l'hash.
        app.MapPost("/api/runs/{runId:guid}/share", async (Guid runId, ClaimsPrincipal p, RuntivoDbContext db, IOptions<AppOptions> opts,
            TimeProvider time, CancellationToken ct) =>
        {
            var me = p.UserId();
            var now = time.GetUtcNow();
            var active = await db.RunParticipants.AnyAsync(x => x.RunId == runId && x.UserId == me &&
                (x.Status == ParticipantStatus.Pending || x.Status == ParticipantStatus.Confirmed), ct);
            var run = active ? await db.SpontaneousRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct) : null;
            if (run is null) return Http.Problem(404, "Uscita non trovata.");
            if (run.Status == RunStatus.Cancelled) return Http.Problem(409, "L'uscita è stata annullata.");
            var expiresAt = run.StartsAtUtc + GraceAfterStart;
            if (expiresAt <= now) return Http.Problem(409, "L'uscita è già conclusa.");

            if (await db.SafetyShares.CountAsync(s => s.RunId == runId && s.UserId == me && s.RevokedAt == null && s.ExpiresAt > now, ct) >= MaxActiveShares)
                return Http.Problem(409, $"Hai già {MaxActiveShares} link attivi per questa uscita: revocane qualcuno prima.");

            var (token, hash) = Tokens.Generate();
            var share = new SafetyShare { Id = Guid.NewGuid(), RunId = runId, UserId = me, TokenHash = hash, CreatedAt = now, ExpiresAt = expiresAt };
            db.SafetyShares.Add(share);
            db.Audit(time, me, "run.share", "SpontaneousRun", runId);
            await db.SaveChangesAsync(ct);

            var link = $"{opts.Value.PublicBaseUrl.TrimEnd('/')}/sicurezza/{token}";
            return Results.Created($"/api/runs/{runId}/share", new { link, expiresAt });
        }).RequireAuthorization();

        app.MapDelete("/api/runs/{runId:guid}/share", async (Guid runId, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var me = p.UserId();
            var now = time.GetUtcNow();
            await db.SafetyShares.Where(s => s.RunId == runId && s.UserId == me && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // Lettura pubblica (la persona di fiducia non ha un account): solo nomi visibili, luogo pubblico, giorno e ora.
        app.MapGet("/api/safety/{token}", async (string token, HttpContext http, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            http.Response.Headers.CacheControl = "no-store";
            var hash = Tokens.Hash(token);

            var row = await (from s in db.SafetyShares.AsNoTracking()
                             join run in db.SpontaneousRuns.AsNoTracking() on s.RunId equals run.Id
                             join pl in db.PublicPlaces.AsNoTracking() on run.PlaceId equals pl.Id
                             join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                             where s.TokenHash == hash && s.RevokedAt == null && s.ExpiresAt > now
                             select new { s.RunId, s.ExpiresAt, Run = run, Place = pl, SharedBy = u.DisplayName }).FirstOrDefaultAsync(ct);
            if (row is null) return Http.Problem(404, "Link non valido o scaduto.");

            // Se l'uscita è stata annullata non si mostra più chi altro ci fosse.
            var names = row.Run.Status == RunStatus.Cancelled
                ? []
                : await (from part in db.RunParticipants.AsNoTracking()
                         join u in db.Users.AsNoTracking() on part.UserId equals u.Id
                         where part.RunId == row.RunId && (part.Status == ParticipantStatus.Pending || part.Status == ParticipantStatus.Confirmed)
                         orderby part.UpdatedAt
                         select u.DisplayName).ToListAsync(ct);
            return Results.Ok(new SafetyViewDto(row.SharedBy, row.Run.StartsAtUtc, row.Run.TimeZoneId, row.Place.Name, row.Place.City, row.Run.Status, names, row.ExpiresAt));
        });
    }
}
