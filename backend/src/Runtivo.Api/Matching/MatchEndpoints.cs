using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Matching;

/// <summary>Scheda di una persona compatibile: mai coordinate, email o luogo esatto, solo la città e una distanza approssimata.</summary>
public record MatchDto(Guid UserId, string DisplayName, string City, int Score, List<string> Reasons, int PaceMinSecPerKm, int PaceMaxSecPerKm,
    int DistanceMinMeters, int DistanceMaxMeters, RunType RunType, List<SlotDto> SharedSlots, int ApproxDistanceKm, bool CanMeetAsPair);

public static class MatchEndpoints
{
    private const int MaxCandidates = 500;
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 20;

    public static void MapMatchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/me/matches", async (int? skip, int? take, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var now = time.GetUtcNow();

            var mine = await (from i in db.RunIntents.AsNoTracking()
                              join pl in db.PublicPlaces on i.HomePlaceId equals pl.Id
                              where i.UserId == userId
                              select new { Intent = i, pl.Latitude, pl.Longitude }).FirstOrDefaultAsync(ct);
            // Vedere gli altri richiede di essere visibili a propria volta: niente ricerca a senso unico.
            if (mine is null || mine.Intent.Status != IntentStatus.Active || mine.Intent.ExpiresAt <= now)
                return Http.Problem(409, "Pubblica o riattiva il tuo annuncio per vedere le persone compatibili.");

            var me = mine.Intent;
            var (minLat, maxLat, minLon, maxLon) = Geo.BoundingBox(mine.Latitude, mine.Longitude, me.RadiusKm);

            var candidates = await (from i in db.RunIntents.AsNoTracking()
                                    join pl in db.PublicPlaces on i.HomePlaceId equals pl.Id
                                    join u in db.Users on i.UserId equals u.Id
                                    where i.UserId != userId && i.Status == IntentStatus.Active && i.ExpiresAt > now && u.Status == UserStatus.Active
                                          && pl.Latitude >= minLat && pl.Latitude <= maxLat && pl.Longitude >= minLon && pl.Longitude <= maxLon
                                          && !db.UserBlocks.Any(b => (b.BlockerId == userId && b.BlockedId == i.UserId) || (b.BlockerId == i.UserId && b.BlockedId == userId))
                                    orderby i.UpdatedAt descending
                                    select new { i.UserId, u.DisplayName, pl.City, pl.Latitude, pl.Longitude, Intent = i })
                .Take(MaxCandidates).ToListAsync(ct);

            var myProfile = ToProfile(me);
            var matches = new List<MatchDto>();
            foreach (var c in candidates)
            {
                var distance = Geo.DistanceKm(mine.Latitude, mine.Longitude, c.Latitude, c.Longitude);
                var score = MatchScoring.Score(myProfile, ToProfile(c.Intent), distance);
                if (score is null) continue;
                matches.Add(new MatchDto(c.UserId, c.DisplayName, c.City, score.Total, score.Reasons.ToList(),
                    c.Intent.PaceMinSecPerKm, c.Intent.PaceMaxSecPerKm, c.Intent.DistanceMinMeters, c.Intent.DistanceMaxMeters, c.Intent.RunType,
                    Availability.Slots(score.SharedMask).Select(s => new SlotDto(s.Day, s.Band)).ToList(),
                    Math.Max(1, (int)Math.Round(distance)), me.AllowPairs && c.Intent.AllowPairs));
            }

            var page = matches.OrderByDescending(m => m.Score).ThenBy(m => m.ApproxDistanceKm).ThenBy(m => m.DisplayName)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize)).ToList();
            return Results.Ok(page);
        }).RequireAuthorization();

        // Il blocco è reciproco e invisibile: l'altra persona non riceve alcun avviso, semplicemente non compare più.
        app.MapPost("/api/users/{userId:guid}/block", async (Guid userId, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var me = p.UserId();
            if (userId == me) return Http.Problem(400, "Non puoi bloccare te stesso.");
            if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return Http.Problem(404, "Utente non trovato.");
            if (!await db.UserBlocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == userId, ct))
            {
                db.UserBlocks.Add(new UserBlock { BlockerId = me, BlockedId = userId, CreatedAt = time.GetUtcNow() });
                db.Audit(time, me, "user.block", "User", userId);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { /* richiesta ripetuta in parallelo: il blocco esiste già */ }
            }
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapDelete("/api/users/{userId:guid}/block", async (Guid userId, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var me = p.UserId();
            await db.UserBlocks.Where(b => b.BlockerId == me && b.BlockedId == userId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static IntentProfile ToProfile(RunIntent i) =>
        new(i.PaceMinSecPerKm, i.PaceMaxSecPerKm, i.DistanceMinMeters, i.DistanceMaxMeters, i.RunType, i.AvailabilityMask, i.RadiusKm);
}
