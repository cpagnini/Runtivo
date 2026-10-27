using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class ClubTests(ApiFixture api)
{
    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private static async Task<Guid> CreateClubAsync(HttpClient admin, string name = "Martedì Running")
    {
        var response = await admin.PostAsJsonAsync("/api/clubs", new { name, city = "Prato" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<string> InviteAsync(HttpClient admin, Guid clubId, string? email = null)
    {
        var response = await admin.PostAsJsonAsync($"/api/clubs/{clubId}/invitations", new { email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> JoinAsync(HttpClient client, Guid clubId, string? inviteToken = null) =>
        client.PostAsJsonAsync($"/api/clubs/{clubId}/memberships", new { inviteToken });

    [SkippableFact]
    public async Task Creating_a_club_requires_login_and_makes_the_creator_admin()
    {
        RequireDb();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.NewClient().PostAsJsonAsync("/api/clubs", new { name = "Club anonimo" })).StatusCode);

        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);

        var club = await (await admin.GetAsync($"/api/clubs/{clubId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Admin", club.GetProperty("myRole").GetString());
        Assert.StartsWith("martedi-running", club.GetProperty("slug").GetString());
    }

    [SkippableFact]
    public async Task Club_details_are_public_but_role_is_hidden_from_visitors()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);

        var club = await (await api.NewClient().GetAsync($"/api/clubs/{clubId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, club.GetProperty("myRole").ValueKind);
    }

    [SkippableFact]
    public async Task Invitation_makes_the_member_active_immediately_and_is_single_use()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var token = await InviteAsync(admin, clubId);

        var (runner, _, _) = await api.LoginAsync();
        var joined = await (await JoinAsync(runner, clubId, token)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", joined.GetProperty("status").GetString());

        var (other, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await JoinAsync(other, clubId, token)).StatusCode);
    }

    [SkippableFact]
    public async Task Invitation_for_a_specific_email_cannot_be_used_by_someone_else()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var invited = ApiFixture.NewEmail();
        await InviteAsync(admin, clubId, invited);
        var token = ApiFixture.TokenFromLink(api.Mail.To(invited)[0].Link);

        var (stranger, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await JoinAsync(stranger, clubId, token)).StatusCode);

        var (rightPerson, _, _) = await api.LoginAsync(invited);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(rightPerson, clubId, token)).StatusCode);
    }

    [SkippableFact]
    public async Task Invitation_expires_after_seven_days()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var token = await InviteAsync(admin, clubId);
        var (runner, _, _) = await api.LoginAsync();

        api.Time.Advance(TimeSpan.FromDays(8));

        Assert.Equal(HttpStatusCode.BadRequest, (await JoinAsync(runner, clubId, token)).StatusCode);
    }

    [SkippableFact]
    public async Task Join_request_stays_pending_until_an_admin_approves_it()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var (runner, _, runnerId) = await api.LoginAsync();

        var pending = await (await JoinAsync(runner, clubId)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        // Ripetere la richiesta non crea duplicati.
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(runner, clubId)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await runner.PostAsync($"/api/clubs/{clubId}/memberships/{runnerId}/approve", null)).StatusCode);

        var approved = await admin.PostAsync($"/api/clubs/{clubId}/memberships/{runnerId}/approve", null);
        Assert.Equal("Active", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Admin_of_another_club_cannot_manage_this_club()
    {
        RequireDb();
        var (adminA, _, _) = await api.LoginAsync();
        var (adminB, _, _) = await api.LoginAsync();
        var clubA = await CreateClubAsync(adminA, "Club A");
        await CreateClubAsync(adminB, "Club B");
        var (runner, _, runnerId) = await api.LoginAsync();
        await JoinAsync(runner, clubA);

        Assert.Equal(HttpStatusCode.Forbidden, (await adminB.GetAsync($"/api/clubs/{clubA}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await adminB.PostAsync($"/api/clubs/{clubA}/memberships/{runnerId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await adminB.PostAsJsonAsync($"/api/clubs/{clubA}/invitations", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await adminB.PutAsJsonAsync($"/api/clubs/{clubA}/memberships/{runnerId}/role", new { role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminB.DeleteAsync($"/api/clubs/{clubA}/memberships/{runnerId}")).StatusCode);
    }

    [SkippableFact]
    public async Task Plain_member_cannot_see_the_member_list()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var (runner, _, _) = await api.LoginAsync();
        await JoinAsync(runner, clubId, await InviteAsync(admin, clubId));

        Assert.Equal(HttpStatusCode.Forbidden, (await runner.GetAsync($"/api/clubs/{clubId}/members")).StatusCode);
    }

    [SkippableFact]
    public async Task Member_list_is_visible_to_admins()
    {
        RequireDb();
        var (admin, adminEmail, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);

        var members = await (await admin.GetAsync($"/api/clubs/{clubId}/members")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, members.GetArrayLength());
        Assert.Equal(adminEmail, members[0].GetProperty("email").GetString());
    }

    [SkippableFact]
    public async Task The_last_admin_cannot_be_demoted_or_removed()
    {
        RequireDb();
        var (admin, _, adminId) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync($"/api/clubs/{clubId}/memberships/{adminId}/role", new { role = "Member" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/clubs/{clubId}/memberships/{adminId}")).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_can_hand_over_and_then_leave()
    {
        RequireDb();
        var (admin, _, adminId) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var (second, _, secondId) = await api.LoginAsync();
        await JoinAsync(second, clubId, await InviteAsync(admin, clubId));

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/clubs/{clubId}/memberships/{secondId}/role", new { role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/clubs/{clubId}/memberships/{adminId}")).StatusCode);

        // Il nuovo admin ha ora il controllo.
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync($"/api/clubs/{clubId}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/clubs/{clubId}/members")).StatusCode);
    }

    [SkippableFact]
    public async Task A_member_can_leave_but_cannot_remove_others()
    {
        RequireDb();
        var (admin, _, adminId) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var (runner, _, runnerId) = await api.LoginAsync();
        await JoinAsync(runner, clubId, await InviteAsync(admin, clubId));

        Assert.Equal(HttpStatusCode.Forbidden, (await runner.DeleteAsync($"/api/clubs/{clubId}/memberships/{adminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await runner.DeleteAsync($"/api/clubs/{clubId}/memberships/{runnerId}")).StatusCode);

        var mine = await (await runner.GetAsync("/api/clubs/mine")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, mine.GetArrayLength());
    }

    [SkippableFact]
    public async Task Unknown_club_returns_404()
    {
        RequireDb();
        var (user, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/clubs/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/clubs/{Guid.NewGuid()}/members")).StatusCode);
    }

    [SkippableFact]
    public async Task Club_name_is_validated()
    {
        RequireDb();
        var (user, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/clubs", new { name = "x" })).StatusCode);
    }
}
