using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runtivo.Api.Common;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class PrivacyTests(ApiFixture api)
{
    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private static Task<HttpResponseMessage> DeleteAccount(HttpClient c, string? confirmEmail) =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me") { Content = JsonContent.Create(new { confirmEmail }) });

    private static async Task<Guid> CreateClubAsync(HttpClient admin, string name)
    {
        var r = await admin.PostAsJsonAsync("/api/clubs", new { name });
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<(Guid EventId, Guid GroupId)> CreateEventAsync(HttpClient admin, Guid clubId)
    {
        var r = await admin.PostAsJsonAsync($"/api/clubs/{clubId}/events", new
        {
            title = "Uscita", meetingPointLabel = "Piazza", startsAt = api.Time.GetUtcNow().AddDays(2), visibility = "Public",
            groups = new[] { new { name = "Relax" } },
        });
        var eventId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var ev = await (await admin.GetAsync($"/api/events/{eventId}")).Content.ReadFromJsonAsync<JsonElement>();
        return (eventId, ev.GetProperty("groups")[0].GetProperty("id").GetGuid());
    }

    // ---- preferenze di comunicazione ----

    [SkippableFact]
    public async Task Notification_preferences_default_to_operational_emails_only_and_can_be_changed()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();

        var defaults = await (await client.GetAsync("/api/me/notifications")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(defaults.GetProperty("eventChangesEmail").GetBoolean());
        Assert.False(defaults.GetProperty("remindersEmail").GetBoolean());
        Assert.False(defaults.GetProperty("discoveryEmails").GetBoolean());

        await client.PutAsJsonAsync("/api/me/notifications", new { eventChangesEmail = false, remindersEmail = true, discoveryEmails = false });
        var saved = await (await client.GetAsync("/api/me/notifications")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(saved.GetProperty("eventChangesEmail").GetBoolean());
        Assert.True(saved.GetProperty("remindersEmail").GetBoolean());
    }

    [SkippableFact]
    public async Task Notification_preferences_require_login()
    {
        RequireDb();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().GetAsync("/api/me/notifications")).StatusCode);
    }

    // ---- profilo ----

    [SkippableFact]
    public async Task Preferred_pace_can_be_cleared()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        await client.PatchAsJsonAsync("/api/me", new { preferredPaceSecPerKm = 330 });

        var cleared = await client.PatchAsJsonAsync("/api/me", new { clearPreferredPace = true });

        var body = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("preferredPaceSecPerKm").ValueKind);
    }

    // ---- export ----

    [SkippableFact]
    public async Task Export_contains_my_data_and_nobody_elses()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin, "Export " + Guid.NewGuid().ToString("N")[..6]);
        var (eventId, groupId) = await CreateEventAsync(admin, clubId);
        var (me, email, _) = await api.LoginAsync();
        var (other, otherEmail, _) = await api.LoginAsync();
        await me.PostAsJsonAsync($"/api/clubs/{clubId}/memberships", new { });
        await me.PutAsJsonAsync($"/api/events/{eventId}/rsvp", new { status = "Going", paceGroupId = groupId });
        await other.PutAsJsonAsync($"/api/events/{eventId}/rsvp", new { status = "Going", paceGroupId = groupId });

        var response = await me.GetAsync("/api/me/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var text = await response.Content.ReadAsStringAsync();
        var export = JsonDocument.Parse(text).RootElement;
        Assert.Equal(email, export.GetProperty("profile").GetProperty("email").GetString());
        Assert.Equal(1, export.GetProperty("memberships").GetArrayLength());
        Assert.Equal(1, export.GetProperty("rsvps").GetArrayLength());
        Assert.Equal("Relax", export.GetProperty("rsvps")[0].GetProperty("group").GetString());
        Assert.DoesNotContain(otherEmail, text);
    }

    [SkippableFact]
    public async Task Export_requires_login()
    {
        RequireDb();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().GetAsync("/api/me/export")).StatusCode);
    }

    // ---- cancellazione ----

    [SkippableFact]
    public async Task Deleting_the_account_removes_the_person_and_frees_their_seat()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin, "Del " + Guid.NewGuid().ToString("N")[..6]);
        var (eventId, groupId) = await CreateEventAsync(admin, clubId);
        var (runner, email, runnerId) = await api.LoginAsync();
        await runner.PostAsJsonAsync($"/api/clubs/{clubId}/memberships", new { });
        await runner.PutAsJsonAsync($"/api/events/{eventId}/rsvp", new { status = "Going", paceGroupId = groupId });

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(runner, email)).StatusCode);

        // La sessione non vale più e l'iscrizione all'uscita è sparita.
        Assert.Equal(HttpStatusCode.Unauthorized, (await runner.GetAsync("/api/me")).StatusCode);
        var ev = await (await admin.GetAsync($"/api/events/{eventId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, ev.GetProperty("groups")[0].GetProperty("goingCount").GetInt32());
        var members = await (await admin.GetAsync($"/api/clubs/{clubId}/members")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(members.EnumerateArray(), m => m.GetProperty("userId").GetGuid() == runnerId);
        // Chi riaccede con la stessa email ricomincia da un account nuovo.
        var (again, _, newId) = await api.LoginAsync(email);
        Assert.NotEqual(runnerId, newId);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Deleting_requires_the_email_as_confirmation()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAccount(client, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAccount(client, "altra@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
    }

    [SkippableFact]
    public async Task The_only_admin_of_a_club_with_members_cannot_delete_the_account()
    {
        RequireDb();
        var (admin, email, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin, "Blocco " + Guid.NewGuid().ToString("N")[..6]);
        var (member, _, memberId) = await api.LoginAsync();
        var invite = await admin.PostAsJsonAsync($"/api/clubs/{clubId}/invitations", new { });
        var token = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        await member.PostAsJsonAsync($"/api/clubs/{clubId}/memberships", new { inviteToken = token });

        var blocked = await DeleteAccount(admin, email);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/me")).StatusCode);

        // Passato il ruolo, la cancellazione riesce e il club resta al nuovo admin.
        await admin.PutAsJsonAsync($"/api/clubs/{clubId}/memberships/{memberId}/role", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(admin, email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/clubs/{clubId}/members")).StatusCode);
    }

    [SkippableFact]
    public async Task A_club_with_no_other_members_is_deleted_with_its_only_admin()
    {
        RequireDb();
        var (admin, email, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin, "Solo " + Guid.NewGuid().ToString("N")[..6]);
        var (eventId, _) = await CreateEventAsync(admin, clubId);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(admin, email)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await api.NewClient().GetAsync($"/api/clubs/{clubId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.NewClient().GetAsync($"/api/events/{eventId}")).StatusCode);
    }

    // ---- sicurezza ----

    [SkippableFact]
    public async Task Responses_carry_basic_security_headers()
    {
        RequireDb();
        var response = await api.NewClient().GetAsync("/health");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [SkippableFact]
    public async Task Rate_limits_are_per_client_ip_when_behind_the_frontend_proxy()
    {
        RequireDb();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(ApiFixture.TestDbVariable)!);
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Series:BackgroundGeneration", "false");
            b.UseSetting("App:TrustForwardedHeaders", "true");
            b.UseSetting("App:AuthRequestsPerMinute", "3");
            b.ConfigureServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(new CapturingEmailSender()); });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "runtivo");

        async Task<HttpStatusCode> Request(string ip)
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/auth/request-link") { Content = JsonContent.Create(new { email = ApiFixture.NewEmail() }) };
            msg.Headers.Add("X-Forwarded-For", ip);
            return (await client.SendAsync(msg)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Accepted, await Request("203.0.113.10"));
        Assert.Equal(HttpStatusCode.Accepted, await Request("203.0.113.10"));
        Assert.Equal(HttpStatusCode.Accepted, await Request("203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Request("203.0.113.10"));
        // Un altro utente dietro lo stesso proxy non è penalizzato.
        Assert.Equal(HttpStatusCode.Accepted, await Request("203.0.113.99"));
    }
}
