using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Runtivo.Api.Events;

namespace Runtivo.Api.Tests;

public class RecurrenceTests
{
    private static readonly TimeOnly Evening = new(19, 0);

    private static string[] Utc(DayOfWeek day, TimeOnly time, string from, string to) =>
        Recurrence.Occurrences(day, time, "Europe/Rome", DateOnly.Parse(from), DateOnly.Parse(to))
            .Select(o => $"{o.LocalDate:yyyy-MM-dd} {o.StartsAtUtc:HH:mm}Z").ToArray();

    [Fact]
    public void Local_time_is_kept_when_daylight_saving_starts()
    {
        // Il cambio all'ora legale è il 28 marzo 2027: 19:00 locali = 18:00Z prima, 17:00Z dopo.
        Assert.Equal(
            ["2027-03-16 18:00Z", "2027-03-23 18:00Z", "2027-03-30 17:00Z", "2027-04-06 17:00Z"],
            Utc(DayOfWeek.Tuesday, Evening, "2027-03-15", "2027-04-06"));
    }

    [Fact]
    public void Local_time_is_kept_when_daylight_saving_ends()
    {
        // Il cambio all'ora solare è il 25 ottobre 2026.
        Assert.Equal(
            ["2026-10-20 17:00Z", "2026-10-27 18:00Z", "2026-11-03 18:00Z"],
            Utc(DayOfWeek.Tuesday, Evening, "2026-10-20", "2026-11-03"));
    }

    [Fact]
    public void A_time_skipped_by_the_clock_change_moves_to_the_first_valid_hour()
    {
        // Domenica 28 marzo 2027 alle 02:30 non esiste a Roma: diventa 03:30 (01:30Z).
        Assert.Equal(["2027-03-28 01:30Z"], Utc(DayOfWeek.Sunday, new TimeOnly(2, 30), "2027-03-28", "2027-03-28"));
    }

    [Fact]
    public void First_occurrence_is_the_first_matching_weekday_on_or_after_the_start()
    {
        var dates = Recurrence.Occurrences(DayOfWeek.Tuesday, Evening, "Europe/Rome", new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 20))
            .Select(o => o.LocalDate.ToString("yyyy-MM-dd")).ToArray();
        Assert.Equal(["2026-11-10", "2026-11-17"], dates);
    }
}

[Collection("api")]
public class SeriesTests(ApiFixture api)
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private record Setup(HttpClient Admin, Guid ClubId, Guid SeriesId);

    private async Task<Setup> CreateSeriesAsync(object? overrides = null)
    {
        var (admin, _, _) = await api.LoginAsync();
        var club = await admin.PostAsJsonAsync("/api/clubs", new { name = "Club " + Guid.NewGuid().ToString("N")[..6] });
        var clubId = (await club.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var response = await PostSeries(admin, clubId, overrides);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return new Setup(admin, clubId, id);
    }

    private Task<HttpResponseMessage> PostSeries(HttpClient admin, Guid clubId, object? overrides = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Martedì Running",
            ["meetingPointLabel"] = "Piazza del Comune",
            ["weekday"] = "Tuesday",
            ["localStartTime"] = "19:00",
            ["startDate"] = DateOnly.FromDateTime(api.Time.GetUtcNow().UtcDateTime),
            ["visibility"] = "Public",
            ["groups"] = new[]
            {
                new { name = "Relax", distanceMeters = 6000, paceMinSecPerKm = 375, paceMaxSecPerKm = 405, capacity = (int?)null },
                new { name = "Veloce", distanceMeters = 12000, paceMinSecPerKm = 270, paceMaxSecPerKm = 290, capacity = (int?)20 },
            },
        };
        if (overrides is not null)
            foreach (var p in overrides.GetType().GetProperties()) body[p.Name] = p.GetValue(overrides);
        return admin.PostAsJsonAsync($"/api/clubs/{clubId}/series", body);
    }

    private static async Task<List<JsonElement>> EventsAsync(HttpClient c, Guid clubId) =>
        (await (await c.GetAsync($"/api/clubs/{clubId}/events")).Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();

    private static async Task<int> GenerateAsync(HttpClient admin, Guid seriesId)
    {
        var response = await admin.PostAsync($"/api/series/{seriesId}/generate", null);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("createdEvents").GetInt32();
    }

    [SkippableFact]
    public async Task Creating_a_series_generates_the_next_weeks_with_the_group_template()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        var events = await EventsAsync(s.Admin, s.ClubId);

        Assert.InRange(events.Count, Recurrence.HorizonWeeks, Recurrence.HorizonWeeks + 1);
        Assert.All(events, e =>
        {
            var local = TimeZoneInfo.ConvertTime(e.GetProperty("startsAtUtc").GetDateTimeOffset(), Rome);
            Assert.Equal(DayOfWeek.Tuesday, local.DayOfWeek);
            Assert.Equal(new TimeSpan(19, 0, 0), local.TimeOfDay);
            Assert.Equal(2, e.GetProperty("groupCount").GetInt32());
        });

        var first = await (await s.Admin.GetAsync($"/api/events/{events[0].GetProperty("id").GetGuid()}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(s.SeriesId, first.GetProperty("seriesId").GetGuid());
        Assert.Equal("Veloce", first.GetProperty("groups")[1].GetProperty("name").GetString());
        Assert.Equal(20, first.GetProperty("groups")[1].GetProperty("capacity").GetInt32());
    }

    [SkippableFact]
    public async Task Regenerating_does_not_create_duplicates()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        var before = (await EventsAsync(s.Admin, s.ClubId)).Count;

        Assert.Equal(0, await GenerateAsync(s.Admin, s.SeriesId));
        Assert.Equal(0, await GenerateAsync(s.Admin, s.SeriesId));

        Assert.Equal(before, (await EventsAsync(s.Admin, s.ClubId)).Count);
    }

    [SkippableFact]
    public async Task Concurrent_generation_creates_each_date_once()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        // Dopo qualche settimana l'orizzonte si sposta: le nuove date vengono generate da più richieste insieme.
        api.Time.Advance(TimeSpan.FromDays(21));

        var created = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => GenerateAsync(s.Admin, s.SeriesId)));

        var events = await EventsAsync(s.Admin, s.ClubId);
        Assert.Equal(events.Count, events.Select(e => e.GetProperty("startsAtUtc").GetDateTimeOffset()).Distinct().Count());
        Assert.Equal(3, created.Sum());
    }

    [SkippableFact]
    public async Task Cancelling_or_editing_one_occurrence_leaves_the_others_untouched()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        var events = await EventsAsync(s.Admin, s.ClubId);
        var cancelledId = events[1].GetProperty("id").GetGuid();
        var editedId = events[2].GetProperty("id").GetGuid();

        await s.Admin.PostAsync($"/api/events/{cancelledId}/cancel", null);
        await s.Admin.PatchAsJsonAsync($"/api/events/{editedId}", new { title = "Serata speciale", meetingPointLabel = "Parco delle Cascine" });
        Assert.Equal(0, await GenerateAsync(s.Admin, s.SeriesId));

        var after = await EventsAsync(s.Admin, s.ClubId);
        Assert.Equal(events.Count, after.Count);
        Assert.Equal("Cancelled", after[1].GetProperty("status").GetString());
        Assert.Equal("Serata speciale", after[2].GetProperty("title").GetString());
        Assert.All(after.Where((_, i) => i is not (1 or 2)), e =>
        {
            Assert.Equal("Scheduled", e.GetProperty("status").GetString());
            Assert.Equal("Martedì Running", e.GetProperty("title").GetString());
        });
    }

    [SkippableFact]
    public async Task Stopping_a_series_can_cancel_the_future_occurrences()
    {
        RequireDb();
        var s = await CreateSeriesAsync();

        var stop = await s.Admin.PostAsJsonAsync($"/api/series/{s.SeriesId}/stop", new { cancelFutureEvents = true });
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);

        Assert.All(await EventsAsync(s.Admin, s.ClubId), e => Assert.Equal("Cancelled", e.GetProperty("status").GetString()));
        api.Time.Advance(TimeSpan.FromDays(20));
        Assert.Equal(0, await GenerateAsync(s.Admin, s.SeriesId));
    }

    [SkippableFact]
    public async Task Stopping_without_cancelling_keeps_existing_occurrences()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        await s.Admin.PostAsJsonAsync($"/api/series/{s.SeriesId}/stop", new { cancelFutureEvents = false });

        Assert.All(await EventsAsync(s.Admin, s.ClubId), e => Assert.Equal("Scheduled", e.GetProperty("status").GetString()));
    }

    [SkippableFact]
    public async Task End_date_limits_the_generated_occurrences()
    {
        RequireDb();
        var today = DateOnly.FromDateTime(api.Time.GetUtcNow().UtcDateTime);
        var s = await CreateSeriesAsync(new { endDate = today.AddDays(14) });

        Assert.InRange((await EventsAsync(s.Admin, s.ClubId)).Count, 2, 3);
    }

    [SkippableFact]
    public async Task Series_input_is_validated()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        var today = DateOnly.FromDateTime(api.Time.GetUtcNow().UtcDateTime);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostSeries(s.Admin, s.ClubId, new { localStartTime = "7pm" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostSeries(s.Admin, s.ClubId, new { endDate = today.AddDays(-1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostSeries(s.Admin, s.ClubId, new { groups = Array.Empty<object>() })).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_of_another_club_cannot_manage_the_series()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        var (other, _, _) = await api.LoginAsync();
        await other.PostAsJsonAsync("/api/clubs", new { name = "Altro club " + Guid.NewGuid().ToString("N")[..6] });

        Assert.Equal(HttpStatusCode.Forbidden, (await PostSeries(other, s.ClubId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/clubs/{s.ClubId}/series")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/clubs/{s.ClubId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync($"/api/series/{s.SeriesId}/generate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/series/{s.SeriesId}/stop", new { cancelFutureEvents = true })).StatusCode);
    }

    [SkippableFact]
    public async Task Dashboard_shows_upcoming_events_with_participant_counts_and_the_series()
    {
        RequireDb();
        var s = await CreateSeriesAsync();
        var firstId = (await EventsAsync(s.Admin, s.ClubId))[0].GetProperty("id").GetGuid();
        var firstEvent = await (await s.Admin.GetAsync($"/api/events/{firstId}")).Content.ReadFromJsonAsync<JsonElement>();
        var groupId = firstEvent.GetProperty("groups")[1].GetProperty("id").GetGuid();
        var (runner, _, _) = await api.LoginAsync();
        await runner.PutAsJsonAsync($"/api/events/{firstId}/rsvp", new { status = "Going", paceGroupId = groupId });
        var (decliner, _, _) = await api.LoginAsync();
        await decliner.PutAsJsonAsync($"/api/events/{firstId}/rsvp", new { status = "NotGoing" });

        var dashboard = await (await s.Admin.GetAsync($"/api/clubs/{s.ClubId}/dashboard")).Content.ReadFromJsonAsync<JsonElement>();

        var first = dashboard.GetProperty("upcoming")[0];
        Assert.Equal(firstId, first.GetProperty("id").GetGuid());
        Assert.Equal(1, first.GetProperty("going").GetInt32());
        Assert.Equal(1, first.GetProperty("notGoing").GetInt32());
        Assert.Equal(1, first.GetProperty("groups")[1].GetProperty("going").GetInt32());
        Assert.Equal(20, first.GetProperty("groups")[1].GetProperty("capacity").GetInt32());
        var series = dashboard.GetProperty("series");
        Assert.Equal(1, series.GetArrayLength());
        Assert.True(series[0].GetProperty("enabled").GetBoolean());
        Assert.Equal("19:00", series[0].GetProperty("localStartTime").GetString());
    }
}
