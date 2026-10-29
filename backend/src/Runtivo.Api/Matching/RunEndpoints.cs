using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;

namespace Runtivo.Api.Matching;

public record RunParticipantDto(Guid UserId, string DisplayName, ParticipantStatus Status);
public record RunDto(Guid Id, RunStatus Status, string? CancelReason, DateTimeOffset StartsAtUtc, string TimeZoneId, PlaceDto Place,
    int? DistanceMeters, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, List<RunParticipantDto> Participants, ParticipantStatus MyStatus, bool IsOrganizer);
public record RunSummaryDto(Guid Id, RunStatus Status, DateTimeOffset StartsAtUtc, string PlaceName, string City, ParticipantStatus MyStatus, int ActiveParticipants);

public static class RunEndpoints
{
    public static void MapRunEndpoints(this WebApplication app)
    {
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
}
