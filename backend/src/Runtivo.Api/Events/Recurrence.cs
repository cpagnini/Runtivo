using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Events;

public static class Recurrence
{
    public const int HorizonWeeks = 8;

    /// <summary>
    /// Date settimanali tra from e to (estremi inclusi) con il relativo istante UTC.
    /// L'ora locale resta quella voluta anche a cavallo del cambio ora legale/solare.
    /// </summary>
    public static IEnumerable<(DateOnly LocalDate, DateTimeOffset StartsAtUtc)> Occurrences(
        DayOfWeek weekday, TimeOnly localTime, string timeZoneId, DateOnly from, DateOnly to)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var date = from.AddDays(((int)weekday - (int)from.DayOfWeek + 7) % 7);
        for (; date <= to; date = date.AddDays(7))
        {
            var local = date.ToDateTime(localTime, DateTimeKind.Unspecified);
            // Ora locale inesistente (salto in avanti): si sposta alla prima ora valida.
            if (tz.IsInvalidTime(local)) local = local.AddHours(1);
            yield return (date, new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero));
        }
    }

    public static DateOnly LocalToday(DateTimeOffset now, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);

    public static DateOnly DefaultUntil(DateTimeOffset now, string timeZoneId) =>
        LocalToday(now, timeZoneId).AddDays(HorizonWeeks * 7);

    public static List<GroupTemplate> ReadGroups(EventSeries s) =>
        JsonSerializer.Deserialize<List<GroupTemplate>>(s.GroupsJson) ?? [];

    /// <summary>
    /// Crea le occorrenze mancanti fino a <paramref name="until"/>. Idempotente: le date già presenti
    /// (anche modificate o annullate) non vengono toccate né ricreate. Restituisce quante ne ha create.
    /// </summary>
    public static async Task<int> GenerateAsync(RuntivoDbContext db, EventSeries series, DateOnly until, DateTimeOffset now, CancellationToken ct)
    {
        if (!series.Enabled) return 0;
        var from = series.StartDate;
        var today = LocalToday(now, series.TimeZoneId);
        if (from < today) from = today;
        var to = series.EndDate is { } end && end < until ? end : until;
        if (from > to) return 0;
        var template = ReadGroups(series);

        // Una sola generazione alla volta per serie (lock di transazione su Postgres): richieste concorrenti
        // che inserissero le stesse date in ordine diverso andrebbero in deadlock. L'indice univoco resta
        // come garanzia finale contro i duplicati.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({series.Id.ToString()}, 0))", ct);

        var existing = (await db.Events.Where(e => e.SeriesId == series.Id && e.OccurrenceLocalDate != null)
            .Select(e => e.OccurrenceLocalDate!.Value).ToListAsync(ct)).ToHashSet();
        var created = 0;
        foreach (var (date, startsAt) in Occurrences(series.Weekday, series.LocalStartTime, series.TimeZoneId, from, to))
        {
            if (existing.Contains(date) || startsAt <= now) continue;
            var ev = new RunEvent
            {
                Id = Guid.NewGuid(), ClubId = series.ClubId, Title = series.Title, Description = series.Description,
                StartsAtUtc = startsAt, TimeZoneId = series.TimeZoneId, MeetingPointLabel = series.MeetingPointLabel,
                Visibility = series.Visibility, CreatedByUserId = series.CreatedByUserId, CreatedAt = now,
                SeriesId = series.Id, OccurrenceLocalDate = date,
            };
            for (var i = 0; i < template.Count; i++)
                ev.Groups.Add(new PaceGroup
                {
                    Id = Guid.NewGuid(), EventId = ev.Id, Name = template[i].Name, DistanceMeters = template[i].DistanceMeters,
                    PaceMinSecPerKm = template[i].PaceMinSecPerKm, PaceMaxSecPerKm = template[i].PaceMaxSecPerKm,
                    Capacity = template[i].Capacity, SortOrder = i,
                });
            db.Events.Add(ev);
            created++;
        }
        if (created == 0) return 0;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return created;
    }
}

/// <summary>Estende ogni giorno l'orizzonte delle serie attive, così i club non devono ricrearle a mano.</summary>
public class SeriesGenerationService(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, ILogger<SeriesGenerationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Series:BackgroundGeneration", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RuntivoDbContext>();
                var now = time.GetUtcNow();
                foreach (var series in await db.Series.Where(s => s.Enabled).ToListAsync(stoppingToken))
                    await Recurrence.GenerateAsync(db, series, Recurrence.DefaultUntil(now, series.TimeZoneId), now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Generazione delle serie non riuscita");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
