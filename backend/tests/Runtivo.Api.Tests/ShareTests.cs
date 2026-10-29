using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Runtivo.Api.Data;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class ShareTests(ApiFixture api)
{
    private readonly MatchHarness harness = new(api);

    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private async Task<Guid> CreateRunAsync(Runner organizer, TimeSpan? startsIn = null, params Runner[] invitees)
    {
        var response = await organizer.Client.PostAsJsonAsync("/api/runs", new
        {
            placeId = await harness.PlaceAsync(harness.DefaultPlace),
            startsAt = api.Time.GetUtcNow() + (startsIn ?? TimeSpan.FromDays(2)),
            inviteeIds = invitees.Select(i => i.UserId).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<string> ShareAsync(Runner r, Guid runId)
    {
        var response = await r.Client.PostAsync($"/api/runs/{runId}/share", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var link = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        return link[(link.LastIndexOf('/') + 1)..];
    }

    private Task<HttpResponseMessage> View(string token) => api.NewClient().GetAsync($"/api/safety/{token}");

    [SkippableFact]
    public async Task Participant_creates_a_link_and_the_trusted_person_reads_it_without_login()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, null, invitee);

        var token = await ShareAsync(invitee, runId);

        var view = await (await View(token)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(invitee.DisplayName, view.GetProperty("sharedBy").GetString());
        Assert.Equal("Proposed", view.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(view.GetProperty("placeName").GetString()));
        var names = view.GetProperty("participants").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(organizer.DisplayName, names);
        Assert.Contains(invitee.DisplayName, names);
    }

    [SkippableFact]
    public async Task Public_view_has_no_emails_ids_or_coordinates_and_is_not_cacheable()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, null, invitee);
        var token = await ShareAsync(invitee, runId);

        var response = await View(token);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(organizer.Email, raw);
        Assert.DoesNotContain(invitee.Email, raw);
        Assert.DoesNotContain(organizer.UserId.ToString(), raw);
        Assert.DoesNotContain(invitee.UserId.ToString(), raw);
        Assert.DoesNotContain("latitude", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
    }

    [SkippableFact]
    public async Task Only_active_participants_can_share_and_cancelled_runs_cannot()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var outsider = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, null, invitee);

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.PostAsync($"/api/runs/{runId}/share", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().PostAsync($"/api/runs/{runId}/share", null)).StatusCode);

        await organizer.Client.PostAsync($"/api/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, (await organizer.Client.PostAsync($"/api/runs/{runId}/share", null)).StatusCode);
    }

    [SkippableFact]
    public async Task A_person_who_left_the_run_cannot_share_it()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var third = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, null, invitee, third);
        await invitee.Client.PostAsync($"/api/runs/{runId}/decline", null);

        Assert.Equal(HttpStatusCode.NotFound, (await invitee.Client.PostAsync($"/api/runs/{runId}/share", null)).StatusCode);
    }

    [SkippableFact]
    public async Task Link_stops_working_after_revocation_and_after_the_run()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, TimeSpan.FromHours(5), invitee);

        var revoked = await ShareAsync(invitee, runId);
        Assert.Equal(HttpStatusCode.OK, (await View(revoked)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await invitee.Client.DeleteAsync($"/api/runs/{runId}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await View(revoked)).StatusCode);

        var expiring = await ShareAsync(invitee, runId);
        Assert.Equal(HttpStatusCode.OK, (await View(expiring)).StatusCode);
        api.Time.Advance(TimeSpan.FromHours(5 + 7)); // inizio + 6 ore = scadenza
        Assert.Equal(HttpStatusCode.NotFound, (await View(expiring)).StatusCode);
    }

    [SkippableFact]
    public async Task Limit_of_five_active_shares_per_person_and_run()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, null, invitee);

        for (var i = 0; i < 5; i++) await ShareAsync(invitee, runId);

        Assert.Equal(HttpStatusCode.Conflict, (await invitee.Client.PostAsync($"/api/runs/{runId}/share", null)).StatusCode);
        // Il limite è per persona: l'altra può comunque condividere.
        await ShareAsync(organizer, runId);
    }

    [SkippableFact]
    public async Task Unknown_token_is_404_and_tokens_are_stored_hashed()
    {
        RequireDb();
        Assert.Equal(HttpStatusCode.NotFound, (await View("non-esiste")).StatusCode);

        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, null, invitee);
        var token = await ShareAsync(invitee, runId);

        using var scope = api.Services.CreateScope();
        var hashes = await scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().SafetyShares.AsNoTracking()
            .Where(s => s.RunId == runId).Select(s => s.TokenHash).ToListAsync();
        Assert.Single(hashes);
        Assert.NotEqual(token, hashes[0]);
        Assert.DoesNotContain(token, hashes[0]);
    }

    [SkippableFact]
    public async Task Public_view_reflects_a_run_cancelled_after_sharing_without_other_details()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var runId = await CreateRunAsync(organizer, null, invitee);
        var token = await ShareAsync(invitee, runId);

        await organizer.Client.PostAsync($"/api/runs/{runId}/cancel", null);

        var view = await (await View(token)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", view.GetProperty("status").GetString());
        Assert.Equal(0, view.GetProperty("participants").GetArrayLength());
    }
}
