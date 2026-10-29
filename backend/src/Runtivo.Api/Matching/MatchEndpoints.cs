using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;

namespace Runtivo.Api.Matching;

/// <summary>Scheda di una persona compatibile: mai coordinate, email o luogo esatto, solo la città e una distanza approssimata.</summary>
public record MatchDto(Guid UserId, string DisplayName, string City, int Score, List<string> Reasons, int PaceMinSecPerKm, int PaceMaxSecPerKm,
    int DistanceMinMeters, int DistanceMaxMeters, RunType RunType, List<SlotDto> SharedSlots, int ApproxDistanceKm, bool CanMeetAsPair,
    bool InterestSent, Guid? RunId);

public static class MatchEndpoints
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 20;

    public static void MapMatchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/me/matches", async (int? skip, int? take, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var now = time.GetUtcNow();

            // Vedere gli altri richiede di essere visibili a propria volta: niente ricerca a senso unico.
            var mine = await MatchService.LoadMineAsync(db, userId, now, ct);
            if (mine is null) return Http.Problem(409, "Pubblica o riattiva il tuo annuncio per vedere le persone compatibili.");

            var page = (await MatchService.FindAsync(db, mine, now, ct))
                .OrderByDescending(c => c.Score.Total).ThenBy(c => Math.Max(1, (int)Math.Round(c.DistanceKm))).ThenBy(c => c.DisplayName)
                .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize)).ToList();

            var ids = page.Select(c => c.UserId).ToList();
            var sent = (await db.Interests.Where(i => i.FromUserId == userId && ids.Contains(i.ToUserId)).Select(i => i.ToUserId).ToListAsync(ct)).ToHashSet();
            var runs = await MatchService.ActiveRunsWithAsync(db, userId, ids, now, ct);

            return Results.Ok(page.Select(c => new MatchDto(c.UserId, c.DisplayName, c.City, c.Score.Total, c.Score.Reasons.ToList(),
                c.Intent.PaceMinSecPerKm, c.Intent.PaceMaxSecPerKm, c.Intent.DistanceMinMeters, c.Intent.DistanceMaxMeters, c.Intent.RunType,
                Availability.Slots(c.Score.SharedMask).Select(s => new SlotDto(s.Day, s.Band)).ToList(),
                Math.Max(1, (int)Math.Round(c.DistanceKm)), mine.Intent.AllowPairs && c.Intent.AllowPairs,
                sent.Contains(c.UserId), runs.TryGetValue(c.UserId, out var runId) ? runId : null)).ToList());
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
}
