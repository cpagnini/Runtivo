using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Events;

public record CreateSeriesDto(string? Title, string? Description, string? MeetingPointLabel, DayOfWeek? Weekday, string? LocalStartTime,
    DateOnly? StartDate, DateOnly? EndDate, EventVisibility? Visibility, List<GroupInput>? Groups);
public record StopSeriesDto(bool? CancelFutureEvents);
public record SeriesDto(Guid Id, string Title, string MeetingPointLabel, DayOfWeek Weekday, string LocalStartTime, DateOnly StartDate,
    DateOnly? EndDate, bool Enabled, EventVisibility Visibility, int GroupCount, int UpcomingEvents);
public record DashboardGroupDto(string Name, int Going, int? Capacity);
public record DashboardEventDto(Guid Id, string Title, DateTimeOffset StartsAtUtc, EventStatus Status, Guid? SeriesId,
    int Going, int NotGoing, List<DashboardGroupDto> Groups);
public record DashboardDto(List<DashboardEventDto> Upcoming, List<SeriesDto> Series);

public static class SeriesEndpoints
{
    private const int DashboardDays = 60;

    public static void MapSeriesEndpoints(this WebApplication app)
    {
        app.MapPost("/api/clubs/{clubId:guid}/series", async (Guid clubId, CreateSeriesDto dto, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            if (await EventEndpoints.EnsureClubAdminAsync(db, clubId, userId, ct) is { } denied) return denied;

            if (EventEndpoints.Validate(dto.Title, dto.Description, dto.MeetingPointLabel) is { } err) return Http.Problem(400, err);
            if (dto.Weekday is not { } weekday || !Enum.IsDefined(weekday)) return Http.Problem(400, "Giorno della settimana non valido.");
            if (!TimeOnly.TryParseExact(dto.LocalStartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
                return Http.Problem(400, "L'orario deve essere nel formato HH:mm.");
            if (dto.StartDate is not { } startDate) return Http.Problem(400, "Indica la data di inizio.");
            if (dto.EndDate is { } endDate && endDate < startDate) return Http.Problem(400, "La data di fine precede quella di inizio.");
            if (dto.Visibility is not { } visibility || !Enum.IsDefined(visibility)) return Http.Problem(400, "Visibilità non valida.");
            if (dto.Groups is not { Count: >= 1 and <= 8 } groups) return Http.Problem(400, "Servono da 1 a 8 gruppi di passo.");

            for (var i = 0; i < groups.Count; i++)
            {
                // Il capogruppo non fa parte del modello della serie: si assegna sulle singole uscite.
                if (await EventEndpoints.ValidateGroupAsync(db, clubId, groups[i] with { LeaderUserId = null }, ct) is { } groupError)
                    return Http.Problem(400, $"Gruppo {i + 1}: {groupError}");
            }

            var now = time.GetUtcNow();
            var series = new EventSeries
            {
                Id = Guid.NewGuid(), ClubId = clubId, Title = dto.Title!.Trim(), Description = EventEndpoints.Clean(dto.Description),
                MeetingPointLabel = dto.MeetingPointLabel!.Trim(), Visibility = visibility, Weekday = weekday,
                LocalStartTime = localTime, StartDate = startDate, EndDate = dto.EndDate, CreatedByUserId = userId, CreatedAt = now,
                GroupsJson = JsonSerializer.Serialize(groups.Select(g => new GroupTemplate(g.Name!.Trim(), g.DistanceMeters,
                    g.PaceMinSecPerKm, g.PaceMaxSecPerKm, g.Capacity))),
            };
            db.Series.Add(series);
            db.Audit(time, userId, "series.create", "Series", series.Id);
            await db.SaveChangesAsync(ct);

            var created = await Recurrence.GenerateAsync(db, series, Recurrence.DefaultUntil(now, series.TimeZoneId), now, ct);
            return Results.Created($"/api/series/{series.Id}", new { series.Id, CreatedEvents = created });
        }).RequireAuthorization();

        app.MapGet("/api/clubs/{clubId:guid}/series", async (Guid clubId, ClaimsPrincipal p, RuntivoDbContext db,
            TimeProvider time, CancellationToken ct) =>
        {
            if (await EventEndpoints.EnsureClubAdminAsync(db, clubId, p.UserId(), ct) is { } denied) return denied;
            return Results.Ok(await LoadSeriesAsync(db, clubId, time.GetUtcNow(), ct));
        }).RequireAuthorization();

        // Estende l'orizzonte della serie: le date esistenti (anche annullate o modificate) restano come sono.
        app.MapPost("/api/series/{seriesId:guid}/generate", async (Guid seriesId, ClaimsPrincipal p, RuntivoDbContext db,
            TimeProvider time, CancellationToken ct) =>
        {
            var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
            if (series is null) return Http.Problem(404, "Serie non trovata.");
            if (await EventEndpoints.EnsureClubAdminAsync(db, series.ClubId, p.UserId(), ct) is { } denied) return denied;
            var now = time.GetUtcNow();
            var created = await Recurrence.GenerateAsync(db, series, Recurrence.DefaultUntil(now, series.TimeZoneId), now, ct);
            return Results.Ok(new { CreatedEvents = created });
        }).RequireAuthorization();

        // Ferma la serie; con cancelFutureEvents annulla anche le uscite future non ancora annullate.
        app.MapPost("/api/series/{seriesId:guid}/stop", async (Guid seriesId, StopSeriesDto dto, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
            if (series is null) return Http.Problem(404, "Serie non trovata.");
            var userId = p.UserId();
            if (await EventEndpoints.EnsureClubAdminAsync(db, series.ClubId, userId, ct) is { } denied) return denied;

            var now = time.GetUtcNow();
            series.Enabled = false;
            var cancelled = 0;
            if (dto.CancelFutureEvents == true)
                cancelled = await db.Events
                    .Where(e => e.SeriesId == seriesId && e.StartsAtUtc > now && e.Status == EventStatus.Scheduled)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EventStatus.Cancelled), ct);
            db.Audit(time, userId, "series.stop", "Series", seriesId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { CancelledEvents = cancelled });
        }).RequireAuthorization();

        app.MapGet("/api/clubs/{clubId:guid}/dashboard", async (Guid clubId, ClaimsPrincipal p, RuntivoDbContext db,
            TimeProvider time, CancellationToken ct) =>
        {
            if (await EventEndpoints.EnsureClubAdminAsync(db, clubId, p.UserId(), ct) is { } denied) return denied;
            var now = time.GetUtcNow();
            var until = now.AddDays(DashboardDays);

            var events = await db.Events.AsNoTracking().Include(e => e.Groups)
                .Where(e => e.ClubId == clubId && e.StartsAtUtc > now && e.StartsAtUtc <= until)
                .OrderBy(e => e.StartsAtUtc).Take(100).ToListAsync(ct);
            var ids = events.Select(e => e.Id).ToList();
            var counts = await db.Rsvps.Where(r => ids.Contains(r.EventId))
                .GroupBy(r => new { r.EventId, r.PaceGroupId, r.Status })
                .Select(g => new { g.Key.EventId, g.Key.PaceGroupId, g.Key.Status, N = g.Count() }).ToListAsync(ct);

            var upcoming = events.Select(e =>
            {
                var groups = e.Groups.OrderBy(g => g.SortOrder)
                    .Select(g => new DashboardGroupDto(g.Name,
                        counts.Where(c => c.PaceGroupId == g.Id && c.Status == RsvpStatus.Going).Sum(c => c.N), g.Capacity)).ToList();
                return new DashboardEventDto(e.Id, e.Title, e.StartsAtUtc, e.Status, e.SeriesId, groups.Sum(g => g.Going),
                    counts.Where(c => c.EventId == e.Id && c.Status == RsvpStatus.NotGoing).Sum(c => c.N), groups);
            }).ToList();

            return Results.Ok(new DashboardDto(upcoming, await LoadSeriesAsync(db, clubId, now, ct)));
        }).RequireAuthorization();
    }

    private static async Task<List<SeriesDto>> LoadSeriesAsync(RuntivoDbContext db, Guid clubId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Series.AsNoTracking().Where(s => s.ClubId == clubId).OrderByDescending(s => s.Enabled).ThenBy(s => s.Title).ToListAsync(ct);
        var upcoming = await db.Events.Where(e => e.ClubId == clubId && e.SeriesId != null && e.StartsAtUtc > now && e.Status == EventStatus.Scheduled)
            .GroupBy(e => e.SeriesId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        return rows.Select(s => new SeriesDto(s.Id, s.Title, s.MeetingPointLabel, s.Weekday, s.LocalStartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            s.StartDate, s.EndDate, s.Enabled, s.Visibility, Recurrence.ReadGroups(s).Count, upcoming.GetValueOrDefault(s.Id))).ToList();
    }
}
