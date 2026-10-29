using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Runtivo.Api.Matching;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class RunTests(ApiFixture api)
{
    private readonly MatchHarness harness = new(api);

    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private async Task<Dictionary<string, object?>> RunBody(IEnumerable<Guid> invitees, object? overrides = null, TimeSpan? startsIn = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["placeId"] = await harness.PlaceAsync(harness.DefaultPlace),
            ["startsAt"] = api.Time.GetUtcNow() + (startsIn ?? TimeSpan.FromDays(3)),
            ["distanceMeters"] = 8000,
            ["paceMinSecPerKm"] = 330,
            ["paceMaxSecPerKm"] = 360,
            ["inviteeIds"] = invitees.ToArray(),
        };
        return IntentTests.Apply(body, overrides);
    }

    private static Task<HttpResponseMessage> Create(HttpClient c, Dictionary<string, object?> body) => c.PostAsJsonAsync("/api/runs", body);

    private async Task<Guid> CreateRunAsync(Runner organizer, params Runner[] invitees)
    {
        var response = await Create(organizer.Client, await RunBody(invitees.Select(i => i.UserId)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Confirm(Runner r, Guid runId) => r.Client.PostAsync($"/api/runs/{runId}/confirm", null);
    private static Task<HttpResponseMessage> Decline(Runner r, Guid runId) => r.Client.PostAsync($"/api/runs/{runId}/decline", null);

    private static async Task<JsonElement> GetRun(Runner r, Guid runId)
    {
        var response = await r.Client.GetAsync($"/api/runs/{runId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string StatusOf(JsonElement run, Guid userId) =>
        run.GetProperty("participants").EnumerateArray().First(p => p.GetProperty("userId").GetGuid() == userId).GetProperty("status").GetString()!;

    [SkippableFact]
    public async Task Organizer_invites_a_compatible_person_and_the_run_is_proposed()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();

        var runId = await CreateRunAsync(organizer, invitee);

        var run = await GetRun(invitee, runId);
        Assert.Equal("Proposed", run.GetProperty("status").GetString());
        Assert.Equal("Confirmed", StatusOf(run, organizer.UserId));
        Assert.Equal("Pending", StatusOf(run, invitee.UserId));
        Assert.True((await GetRun(organizer, runId)).GetProperty("isOrganizer").GetBoolean());
        var mine = await (await invitee.Client.GetAsync("/api/me/runs")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(mine.EnumerateArray(), r => r.GetProperty("id").GetGuid() == runId);
    }

    [SkippableFact]
    public async Task Creation_requires_login_and_my_own_active_intent()
    {
        RequireDb();
        var invitee = await harness.RunnerAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(api.NewClient(), await RunBody([invitee.UserId]))).StatusCode);

        var (nobody, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Create(nobody, await RunBody([invitee.UserId]))).StatusCode);
    }

    [SkippableFact]
    public async Task Creation_is_validated()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var ok = new[] { invitee.UserId };

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody(ok, new { PlaceId = Guid.NewGuid() }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody(ok, startsIn: TimeSpan.FromMinutes(30)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody(ok, startsIn: TimeSpan.FromDays(61)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody(Enumerable.Range(0, 8).Select(_ => Guid.NewGuid())))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody([invitee.UserId, invitee.UserId]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody([organizer.UserId, invitee.UserId]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody(ok, new { PaceMinSecPerKm = 400, PaceMaxSecPerKm = 300 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(organizer.Client, await RunBody(ok, new { DistanceMeters = 200 }))).StatusCode);
    }

    [SkippableFact]
    public async Task Cannot_invite_incompatible_blocked_or_unknown_people()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var slow = await harness.RunnerAsync(overrides: new { PaceMinSecPerKm = 450, PaceMaxSecPerKm = 480 });
        var blocked = await harness.RunnerAsync();
        await organizer.Client.PostAsync($"/api/users/{blocked.UserId}/block", null);

        Assert.Equal(HttpStatusCode.NotFound, (await Create(organizer.Client, await RunBody([slow.UserId]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(organizer.Client, await RunBody([blocked.UserId]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(organizer.Client, await RunBody([Guid.NewGuid()]))).StatusCode);
    }

    [SkippableFact]
    public async Task A_pair_requires_both_to_allow_pairs()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync(overrides: new { AllowPairs = true });
        var groupsOnly = await harness.RunnerAsync(overrides: new { AllowPairs = false });
        var third = await harness.RunnerAsync();

        var pair = await Create(organizer.Client, await RunBody([groupsOnly.UserId]));
        Assert.Equal(HttpStatusCode.BadRequest, pair.StatusCode);
        Assert.Contains("gruppo", await pair.Content.ReadAsStringAsync());

        // In tre la preferenza non blocca nessuno.
        Assert.Equal(HttpStatusCode.Created, (await Create(organizer.Client, await RunBody([groupsOnly.UserId, third.UserId]))).StatusCode);
    }

    [SkippableFact]
    public async Task Run_is_confirmed_only_when_everyone_confirms()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, a, b);

        Assert.Equal(HttpStatusCode.OK, (await Confirm(a, runId)).StatusCode);
        Assert.Equal("Proposed", (await GetRun(organizer, runId)).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Confirm(b, runId)).StatusCode);
        Assert.Equal("Confirmed", (await GetRun(organizer, runId)).GetProperty("status").GetString());
        // Ripetere la conferma non cambia nulla.
        Assert.Equal(HttpStatusCode.OK, (await Confirm(b, runId)).StatusCode);
    }

    [SkippableFact]
    public async Task Declining_removes_the_person_and_the_rest_can_still_confirm()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var a = await harness.RunnerAsync();
        var b = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, a, b);

        Assert.Equal(HttpStatusCode.OK, (await Decline(a, runId)).StatusCode);
        var afterDecline = await GetRun(organizer, runId);
        Assert.Equal("Declined", StatusOf(afterDecline, a.UserId));
        Assert.Equal("Proposed", afterDecline.GetProperty("status").GetString());

        await Confirm(b, runId);
        Assert.Equal("Confirmed", (await GetRun(organizer, runId)).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Everyone_declining_cancels_the_run_with_a_reason()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var a = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, a);

        await Decline(a, runId);

        var run = await GetRun(organizer, runId);
        Assert.Equal("Cancelled", run.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(run.GetProperty("cancelReason").GetString()));
    }

    [SkippableFact]
    public async Task Leaving_a_group_that_becomes_a_pair_with_someone_who_disallows_pairs_cancels_it()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync(overrides: new { AllowPairs = true });
        var groupsOnly = await harness.RunnerAsync(overrides: new { AllowPairs = false });
        var third = await harness.RunnerAsync(overrides: new { AllowPairs = true });
        var runId = await CreateRunAsync(organizer, groupsOnly, third);
        await Confirm(groupsOnly, runId);
        await Confirm(third, runId);
        Assert.Equal("Confirmed", (await GetRun(organizer, runId)).GetProperty("status").GetString());

        await Decline(third, runId);

        var run = await GetRun(organizer, runId);
        Assert.Equal("Left", StatusOf(run, third.UserId));
        Assert.Equal("Cancelled", run.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Organizer_can_cancel_and_cancelled_runs_refuse_confirmations()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, invitee);

        Assert.Equal(HttpStatusCode.Forbidden, (await invitee.Client.PostAsync($"/api/runs/{runId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await organizer.Client.PostAsync($"/api/runs/{runId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await organizer.Client.PostAsync($"/api/runs/{runId}/cancel", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(invitee, runId)).StatusCode);
        var mine = await (await invitee.Client.GetAsync("/api/me/runs")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(mine.EnumerateArray(), r => r.GetProperty("id").GetGuid() == runId);
    }

    [SkippableFact]
    public async Task Runs_that_have_started_refuse_confirmations()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var response = await Create(organizer.Client, await RunBody([invitee.UserId], startsIn: TimeSpan.FromHours(2)));
        var runId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        api.Time.Advance(TimeSpan.FromHours(3));

        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(invitee, runId)).StatusCode);
    }

    [SkippableFact]
    public async Task Only_participants_see_the_run_and_the_names()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var outsider = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, invitee);

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/runs/{runId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.PostAsync($"/api/runs/{runId}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().GetAsync($"/api/runs/{runId}")).StatusCode);
        var raw = await (await invitee.Client.GetAsync($"/api/runs/{runId}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(organizer.Email, raw);
        Assert.DoesNotContain("latitude", raw, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Concurrent_confirmations_and_declines_end_consistent()
    {
        RequireDb();
        for (var round = 0; round < 3; round++)
        {
            var organizer = await harness.RunnerAsync();
            var people = new List<Runner>();
            for (var i = 0; i < 4; i++) people.Add(await harness.RunnerAsync());
            var runId = await CreateRunAsync(organizer, people.ToArray());

            var results = await Task.WhenAll(Confirm(people[0], runId), Confirm(people[1], runId), Decline(people[2], runId), Confirm(people[3], runId));

            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var run = await GetRun(organizer, runId);
            Assert.Equal("Declined", StatusOf(run, people[2].UserId));
            Assert.Equal("Confirmed", run.GetProperty("status").GetString());
            Assert.DoesNotContain(run.GetProperty("participants").EnumerateArray(), p => p.GetProperty("status").GetString() == "Pending");
        }
    }

    [SkippableFact]
    public async Task Organizer_has_at_most_three_open_proposals()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();

        for (var i = 0; i < 3; i++) await CreateRunAsync(organizer, invitee);

        Assert.Equal(HttpStatusCode.Conflict, (await Create(organizer.Client, await RunBody([invitee.UserId]))).StatusCode);
    }
}
