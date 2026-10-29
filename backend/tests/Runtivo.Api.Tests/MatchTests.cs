using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class MatchTests(ApiFixture api)
{
    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private record Runner(HttpClient Client, string Email, Guid UserId);

    // Ogni test usa una fascia oraria tutta sua (lunedì, mercoledì, venerdì, sabato, domenica): gli orari in comune
    // sono un filtro obbligatorio, quindi i candidati di un test non compaiono mai negli elenchi degli altri.
    private static readonly (string Day, string Band)[] Niches =
        new[] { "Monday", "Wednesday", "Friday", "Saturday", "Sunday" }.SelectMany(d => new[] { "Morning", "Midday", "Evening" }.Select(b => (d, b))).ToArray();
    private static int nicheCounter;
    private readonly (string Day, string Band) niche = Niches[Interlocked.Increment(ref nicheCounter) % Niches.Length];

    private async Task<Guid> PlaceAsync(string name) =>
        (await IntentTests.PlacesAsync(api.NewClient())).First(p => p.Name == name).Id;

    private async Task<Runner> RunnerAsync(string place = "Piazza del Comune", object? overrides = null)
    {
        var (client, email, id) = await api.LoginAsync();
        var body = IntentTests.ValidIntent(await PlaceAsync(place));
        body["availability"] = new[] { new { day = niche.Day, band = niche.Band } };
        var response = await client.PutAsJsonAsync("/api/me/intent", IntentTests.Apply(body, overrides));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new Runner(client, email, id);
    }

    private static async Task<List<JsonElement>> MatchesAsync(HttpClient c, string query = "")
    {
        var response = await c.GetAsync("/api/me/matches" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    private static bool Contains(IEnumerable<JsonElement> matches, Guid userId) => matches.Any(m => m.GetProperty("userId").GetGuid() == userId);

    [SkippableFact]
    public async Task Compatible_people_see_each_other_with_reasons()
    {
        RequireDb();
        var a = await RunnerAsync();
        var b = await RunnerAsync("Piazza Mercatale");

        var forA = await MatchesAsync(a.Client);
        var forB = await MatchesAsync(b.Client);

        Assert.True(Contains(forA, b.UserId));
        Assert.True(Contains(forB, a.UserId));
        var card = forA.First(m => m.GetProperty("userId").GetGuid() == b.UserId);
        Assert.True(card.GetProperty("score").GetInt32() >= 50);
        Assert.NotEmpty(card.GetProperty("reasons").EnumerateArray());
        Assert.Equal("Prato", card.GetProperty("city").GetString());
        Assert.False(Contains(forA, a.UserId));
    }

    [SkippableFact]
    public async Task Matches_require_my_own_active_intent()
    {
        RequireDb();
        var (nobody, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await nobody.GetAsync("/api/me/matches")).StatusCode);

        var paused = await RunnerAsync();
        await paused.Client.PostAsync("/api/me/intent/pause", null);
        Assert.Equal(HttpStatusCode.Conflict, (await paused.Client.GetAsync("/api/me/matches")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().GetAsync("/api/me/matches")).StatusCode);
    }

    [SkippableFact]
    public async Task Response_has_no_coordinates_email_or_exact_place()
    {
        RequireDb();
        var a = await RunnerAsync("Piazza del Comune");
        var b = await RunnerAsync("Piazza Mercatale");

        var raw = await (await a.Client.GetAsync("/api/me/matches")).Content.ReadAsStringAsync();

        Assert.Contains(b.UserId.ToString(), raw);
        Assert.DoesNotContain("latitude", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(b.Email, raw);
        Assert.DoesNotContain("Mercatale", raw);
    }

    [SkippableFact]
    public async Task Incompatible_pace_or_time_does_not_match()
    {
        RequireDb();
        var a = await RunnerAsync();
        var slow = await RunnerAsync(overrides: new { PaceMinSecPerKm = 450, PaceMaxSecPerKm = 480 });
        var otherDays = await RunnerAsync(overrides: new { Availability = new[] { new { day = "Thursday", band = "Morning" } } });

        var matches = await MatchesAsync(a.Client);

        Assert.False(Contains(matches, slow.UserId));
        Assert.False(Contains(matches, otherDays.UserId));
    }

    [SkippableFact]
    public async Task Too_far_apart_does_not_match()
    {
        RequireDb();
        var a = await RunnerAsync("Piazza del Comune", new { RadiusKm = 10 });
        var florence = await RunnerAsync("Piazza Santa Croce", new { RadiusKm = 30 });

        // Prato–Firenze sono circa 16 km: il raggio minore (10) li esclude.
        Assert.False(Contains(await MatchesAsync(a.Client), florence.UserId));
        Assert.False(Contains(await MatchesAsync(florence.Client), a.UserId));
    }

    [SkippableFact]
    public async Task Expired_or_paused_intents_are_hidden()
    {
        RequireDb();
        var a = await RunnerAsync();
        var paused = await RunnerAsync();
        var shortLived = await RunnerAsync(overrides: new { ExpiresInDays = 1 });
        Assert.True(Contains(await MatchesAsync(a.Client), paused.UserId));
        Assert.True(Contains(await MatchesAsync(a.Client), shortLived.UserId));

        await paused.Client.PostAsync("/api/me/intent/pause", null);
        api.Time.Advance(TimeSpan.FromDays(2));

        var matches = await MatchesAsync(a.Client);
        Assert.False(Contains(matches, paused.UserId));
        Assert.False(Contains(matches, shortLived.UserId));

        await paused.Client.PostAsync("/api/me/intent/resume", null);
        Assert.True(Contains(await MatchesAsync(a.Client), paused.UserId));
    }

    [SkippableFact]
    public async Task Blocking_hides_both_ways_even_if_already_listed_and_unblocking_restores()
    {
        RequireDb();
        var a = await RunnerAsync();
        var b = await RunnerAsync();
        Assert.True(Contains(await MatchesAsync(a.Client), b.UserId));
        Assert.True(Contains(await MatchesAsync(b.Client), a.UserId));

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PostAsync($"/api/users/{b.UserId}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.PostAsync($"/api/users/{b.UserId}/block", null)).StatusCode);

        Assert.False(Contains(await MatchesAsync(a.Client), b.UserId));
        Assert.False(Contains(await MatchesAsync(b.Client), a.UserId));

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/users/{b.UserId}/block")).StatusCode);
        Assert.True(Contains(await MatchesAsync(a.Client), b.UserId));
        Assert.True(Contains(await MatchesAsync(b.Client), a.UserId));
    }

    [SkippableFact]
    public async Task Block_input_is_validated()
    {
        RequireDb();
        var a = await RunnerAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await a.Client.PostAsync($"/api/users/{a.UserId}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.PostAsync($"/api/users/{Guid.NewGuid()}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().PostAsync($"/api/users/{a.UserId}/block", null)).StatusCode);
    }

    [SkippableFact]
    public async Task Suspended_users_are_hidden()
    {
        RequireDb();
        var a = await RunnerAsync();
        var b = await RunnerAsync();
        Assert.True(Contains(await MatchesAsync(a.Client), b.UserId));

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().Users
                .Where(u => u.Id == b.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended));

        Assert.False(Contains(await MatchesAsync(a.Client), b.UserId));
    }

    [SkippableFact]
    public async Task Pagination_returns_the_requested_window_ordered_by_score()
    {
        RequireDb();
        // Passo 3:20–3:30: nessun altro test lo usa, quindi l'elenco contiene solo questi cinque.
        var pace = new { PaceMinSecPerKm = 200, PaceMaxSecPerKm = 210 };
        var viewer = await RunnerAsync(overrides: pace);
        for (var i = 0; i < 5; i++) await RunnerAsync(overrides: pace);

        var all = await MatchesAsync(viewer.Client, "?take=50");
        var window = await MatchesAsync(viewer.Client, "?skip=2&take=2");

        Assert.Equal(5, all.Count);
        Assert.Equal(2, window.Count);
        Assert.Equal(all[2].GetProperty("userId").GetGuid(), window[0].GetProperty("userId").GetGuid());
        Assert.Equal(all.Select(m => m.GetProperty("score").GetInt32()).OrderByDescending(x => x), all.Select(m => m.GetProperty("score").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await viewer.Client.GetAsync("/api/me/matches?take=500")).StatusCode);
    }

    [SkippableFact]
    public async Task Pair_flag_requires_both_to_allow_pairs()
    {
        RequireDb();
        var a = await RunnerAsync(overrides: new { AllowPairs = true });
        var open = await RunnerAsync(overrides: new { AllowPairs = true });
        var groupsOnly = await RunnerAsync(overrides: new { AllowPairs = false });

        var matches = await MatchesAsync(a.Client);

        Assert.True(matches.First(m => m.GetProperty("userId").GetGuid() == open.UserId).GetProperty("canMeetAsPair").GetBoolean());
        Assert.False(matches.First(m => m.GetProperty("userId").GetGuid() == groupsOnly.UserId).GetProperty("canMeetAsPair").GetBoolean());
    }

    [SkippableFact]
    public async Task Account_deletion_removes_the_intent_and_the_blocks()
    {
        RequireDb();
        var a = await RunnerAsync();
        var b = await RunnerAsync();
        Assert.True(Contains(await MatchesAsync(a.Client), b.UserId));
        await b.Client.PostAsync($"/api/users/{a.UserId}/block", null);

        var delete = await b.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me") { Content = JsonContent.Create(new { confirmEmail = b.Email }) });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.False(Contains(await MatchesAsync(a.Client), b.UserId));
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntivoDbContext>();
        Assert.False(await db.RunIntents.AnyAsync(i => i.UserId == b.UserId));
        Assert.False(await db.UserBlocks.AnyAsync(x => x.BlockerId == b.UserId || x.BlockedId == b.UserId));
    }

    [SkippableFact]
    public async Task Export_includes_my_intent_but_not_others()
    {
        RequireDb();
        var a = await RunnerAsync();
        var b = await RunnerAsync();

        var raw = await (await a.Client.GetAsync("/api/me/export")).Content.ReadAsStringAsync();
        var export = JsonDocument.Parse(raw).RootElement;

        Assert.Equal("Piazza del Comune", export.GetProperty("intent").GetProperty("homePlaceName").GetString());
        Assert.DoesNotContain(b.Email, raw);
        Assert.DoesNotContain(b.UserId.ToString(), raw);
    }
}
