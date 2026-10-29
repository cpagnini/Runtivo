using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class RunSafetyTests(ApiFixture api)
{
    private readonly MatchHarness harness = new(api);

    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private async Task<Guid> CreateRunAsync(Runner organizer, params Runner[] invitees)
    {
        var response = await organizer.Client.PostAsJsonAsync("/api/runs", new
        {
            placeId = await harness.PlaceAsync(harness.DefaultPlace),
            startsAt = api.Time.GetUtcNow().AddDays(3),
            inviteeIds = invitees.Select(i => i.UserId).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetRun(Runner r, Guid runId) =>
        await (await r.Client.GetAsync($"/api/runs/{runId}")).Content.ReadFromJsonAsync<JsonElement>();

    private static string StatusOf(JsonElement run, Guid userId) =>
        run.GetProperty("participants").EnumerateArray().First(p => p.GetProperty("userId").GetGuid() == userId).GetProperty("status").GetString()!;

    private static Task<HttpResponseMessage> DeleteAccount(Runner r) =>
        r.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me") { Content = JsonContent.Create(new { confirmEmail = r.Email }) });

    [SkippableFact]
    public async Task Blocking_someone_leaves_the_runs_you_share_with_them()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var blocker = await harness.RunnerAsync();
        var blocked = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, blocker, blocked);
        await blocker.Client.PostAsync($"/api/runs/{runId}/confirm", null);

        Assert.Equal(HttpStatusCode.NoContent, (await blocker.Client.PostAsync($"/api/users/{blocked.UserId}/block", null)).StatusCode);

        var run = await GetRun(blocker, runId);
        Assert.Equal("Left", StatusOf(run, blocker.UserId));
        // Gli altri due restano: l'uscita prosegue come proposta tra loro.
        Assert.Equal("Proposed", run.GetProperty("status").GetString());
        var mine = await (await blocker.Client.GetAsync("/api/me/runs")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(mine.EnumerateArray(), r => r.GetProperty("id").GetGuid() == runId);
    }

    [SkippableFact]
    public async Task Blocking_leaves_unrelated_runs_untouched()
    {
        RequireDb();
        var blocker = await harness.RunnerAsync();
        var blocked = await harness.RunnerAsync();
        var friend = await harness.RunnerAsync();
        var withBlocked = await CreateRunAsync(blocker, blocked);
        var withFriend = await CreateRunAsync(blocker, friend);

        await blocker.Client.PostAsync($"/api/users/{blocked.UserId}/block", null);

        // Organizzatore di una sola uscita a due che si svuota: quella annullata; l'altra resta com'era.
        Assert.Equal("Cancelled", (await GetRun(blocker, withBlocked)).GetProperty("status").GetString());
        var other = await GetRun(blocker, withFriend);
        Assert.Equal("Proposed", other.GetProperty("status").GetString());
        Assert.Equal("Confirmed", StatusOf(other, blocker.UserId));
    }

    [SkippableFact]
    public async Task Deleting_the_account_cancels_runs_that_fall_below_two()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var leaving = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, leaving);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(leaving)).StatusCode);

        var run = await GetRun(organizer, runId);
        Assert.Equal("Cancelled", run.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(run.GetProperty("cancelReason").GetString()));
    }

    [SkippableFact]
    public async Task Deleting_the_account_keeps_a_group_that_still_has_two_who_accept_pairs()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var leaving = await harness.RunnerAsync();
        var stays = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, leaving, stays);

        await DeleteAccount(leaving);

        var run = await GetRun(organizer, runId);
        Assert.Equal("Proposed", run.GetProperty("status").GetString());
        Assert.Equal(2, run.GetProperty("participants").GetArrayLength());
    }

    [SkippableFact]
    public async Task Deleting_the_account_cancels_a_group_left_with_someone_who_disallows_pairs()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync(overrides: new { AllowPairs = true });
        var groupsOnly = await harness.RunnerAsync(overrides: new { AllowPairs = false });
        var leaving = await harness.RunnerAsync(overrides: new { AllowPairs = true });
        var runId = await CreateRunAsync(organizer, groupsOnly, leaving);

        await DeleteAccount(leaving);

        Assert.Equal("Cancelled", (await GetRun(organizer, runId)).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Export_lists_my_runs_but_not_other_participants_names()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        await CreateRunAsync(organizer, invitee);
        await organizer.Client.PostAsJsonAsync("/api/interests", new { toUserId = invitee.UserId });

        var raw = await (await invitee.Client.GetAsync("/api/me/export")).Content.ReadAsStringAsync();
        var export = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(1, export.GetProperty("runs").GetArrayLength());
        Assert.Equal("Pending", export.GetProperty("runs")[0].GetProperty("myStatus").GetString());
        Assert.DoesNotContain(organizer.DisplayName, raw);
        Assert.DoesNotContain(organizer.Email, raw);
        Assert.Equal(0, export.GetProperty("interestsSent").GetInt32());
    }
}
