using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class InterestTests(ApiFixture api)
{
    private readonly MatchHarness harness = new(api);

    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    internal static Task<HttpResponseMessage> Interest(HttpClient c, Guid toUserId) =>
        c.PostAsJsonAsync("/api/interests", new { toUserId });

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [SkippableFact]
    public async Task Interest_in_a_compatible_person_is_private_until_mutual()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();

        var result = await Json(await Interest(a.Client, b.UserId));

        Assert.False(result.GetProperty("mutual").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("runId").ValueKind);
        Assert.True(MatchHarness.Card(await MatchHarness.MatchesAsync(a.Client), b.UserId).GetProperty("interestSent").GetBoolean());
        // B non riceve alcun segno dell'interesse di A.
        var cardOfAForB = MatchHarness.Card(await MatchHarness.MatchesAsync(b.Client), a.UserId);
        Assert.False(cardOfAForB.GetProperty("interestSent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, cardOfAForB.GetProperty("runId").ValueKind);
    }

    [SkippableFact]
    public async Task Mutual_interest_creates_one_proposal_for_both()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();

        await Interest(a.Client, b.UserId);
        var mutual = await Json(await Interest(b.Client, a.UserId));

        Assert.True(mutual.GetProperty("mutual").GetBoolean());
        var runId = mutual.GetProperty("runId").GetGuid();
        Assert.Equal(runId, MatchHarness.Card(await MatchHarness.MatchesAsync(a.Client), b.UserId).GetProperty("runId").GetGuid());
        Assert.Equal(runId, MatchHarness.Card(await MatchHarness.MatchesAsync(b.Client), a.UserId).GetProperty("runId").GetGuid());
    }

    [SkippableFact]
    public async Task Simultaneous_mutual_interest_creates_a_single_proposal()
    {
        RequireDb();
        for (var round = 0; round < 3; round++)
        {
            var a = await harness.RunnerAsync();
            var b = await harness.RunnerAsync();

            var results = await Task.WhenAll(Interest(a.Client, b.UserId), Interest(b.Client, a.UserId));

            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var runs = await Task.WhenAll(results.Select(async r => (await Json(r)).GetProperty("runId")));
            var ids = runs.Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetGuid()).Distinct().ToList();
            Assert.Single(ids);
            var mine = await (await a.Client.GetAsync("/api/me/runs")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(mine.EnumerateArray().Where(r => r.GetProperty("id").GetGuid() == ids[0]));
        }
    }

    [SkippableFact]
    public async Task Interest_requires_login_and_my_own_active_intent()
    {
        RequireDb();
        var b = await harness.RunnerAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Interest(api.NewClient(), b.UserId)).StatusCode);

        var (nobody, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Interest(nobody, b.UserId)).StatusCode);
    }

    [SkippableFact]
    public async Task Interest_in_a_stranger_or_incompatible_person_is_404()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        var slow = await harness.RunnerAsync(overrides: new { PaceMinSecPerKm = 450, PaceMaxSecPerKm = 480 });
        var paused = await harness.RunnerAsync();
        await paused.Client.PostAsync("/api/me/intent/pause", null);
        var blocked = await harness.RunnerAsync();
        await a.Client.PostAsync($"/api/users/{blocked.UserId}/block", null);
        var (noIntent, _, noIntentId) = await api.LoginAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Interest(a.Client, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Interest(a.Client, slow.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Interest(a.Client, paused.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Interest(a.Client, blocked.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Interest(a.Client, noIntentId)).StatusCode);
        Assert.NotNull(noIntent);
    }

    [SkippableFact]
    public async Task Cannot_show_interest_in_myself()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Interest(a.Client, a.UserId)).StatusCode);
    }

    [SkippableFact]
    public async Task Repeated_interest_is_idempotent()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await Interest(a.Client, b.UserId)).StatusCode);

        Assert.True(MatchHarness.Card(await MatchHarness.MatchesAsync(a.Client), b.UserId).GetProperty("interestSent").GetBoolean());
    }

    [SkippableFact]
    public async Task Repeating_the_mutual_interest_returns_the_same_proposal()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();
        await Interest(a.Client, b.UserId);
        var first = (await Json(await Interest(b.Client, a.UserId))).GetProperty("runId").GetGuid();

        var again = (await Json(await Interest(b.Client, a.UserId))).GetProperty("runId").GetGuid();
        var fromA = (await Json(await Interest(a.Client, b.UserId))).GetProperty("runId").GetGuid();

        Assert.Equal(first, again);
        Assert.Equal(first, fromA);
    }

    [SkippableFact]
    public async Task Withdrawing_interest_removes_it()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();
        await Interest(a.Client, b.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/interests/{b.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync($"/api/interests/{b.UserId}")).StatusCode);

        Assert.False(MatchHarness.Card(await MatchHarness.MatchesAsync(a.Client), b.UserId).GetProperty("interestSent").GetBoolean());
        // Senza l'interesse di A, B che lo mostra non crea una proposta.
        Assert.False((await Json(await Interest(b.Client, a.UserId))).GetProperty("mutual").GetBoolean());
    }

    [SkippableFact]
    public async Task The_proposal_has_a_public_place_and_a_future_time_in_the_shared_slot()
    {
        RequireDb();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();
        await Interest(a.Client, b.UserId);
        var runId = (await Json(await Interest(b.Client, a.UserId))).GetProperty("runId").GetGuid();

        var run = await Json(await a.Client.GetAsync($"/api/runs/{runId}"));

        Assert.Equal("Proposed", run.GetProperty("status").GetString());
        Assert.True(run.GetProperty("startsAtUtc").GetDateTimeOffset() > api.Time.GetUtcNow().AddHours(23));
        var local = TimeZoneInfo.ConvertTime(run.GetProperty("startsAtUtc").GetDateTimeOffset(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"));
        Assert.Equal(harness.Niche.Day, local.DayOfWeek.ToString());
        Assert.False(string.IsNullOrEmpty(run.GetProperty("place").GetProperty("name").GetString()));
        Assert.DoesNotContain("latitude", run.ToString(), StringComparison.OrdinalIgnoreCase);
        var statuses = run.GetProperty("participants").EnumerateArray().Select(p => p.GetProperty("status").GetString()).ToList();
        Assert.Equal(["Pending", "Pending"], statuses);
    }
}
