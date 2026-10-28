using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class EventTests(ApiFixture api)
{
    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private record Setup(HttpClient Admin, Guid ClubId, Guid EventId, Guid[] GroupIds);

    private async Task<Guid> CreateClubAsync(HttpClient admin)
    {
        var r = await admin.PostAsJsonAsync("/api/clubs", new { name = "Club " + Guid.NewGuid().ToString("N")[..6] });
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Setup> CreateEventAsync(string visibility = "Public", int?[]? capacities = null, TimeSpan? startsIn = null)
    {
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        capacities ??= [null, null];
        var response = await admin.PostAsJsonAsync($"/api/clubs/{clubId}/events", new
        {
            title = "Martedì Running",
            meetingPointLabel = "Piazza del Comune",
            startsAt = api.Time.GetUtcNow() + (startsIn ?? TimeSpan.FromDays(2)),
            visibility,
            groups = capacities.Select((c, i) => new
            {
                name = $"Gruppo {i + 1}", distanceMeters = 6000 + i * 3000,
                paceMinSecPerKm = 330 - i * 30, paceMaxSecPerKm = 360 - i * 30, capacity = c,
            }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var eventId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var ev = await (await admin.GetAsync($"/api/events/{eventId}")).Content.ReadFromJsonAsync<JsonElement>();
        var groups = ev.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).ToArray();
        return new Setup(admin, clubId, eventId, groups);
    }

    private static Task<HttpResponseMessage> Rsvp(HttpClient c, Guid eventId, Guid? groupId, string status = "Going") =>
        c.PutAsJsonAsync($"/api/events/{eventId}/rsvp", new { status, paceGroupId = groupId });

    private static async Task<JsonElement> GetEvent(HttpClient c, Guid eventId) =>
        await (await c.GetAsync($"/api/events/{eventId}")).Content.ReadFromJsonAsync<JsonElement>();

    private static int Going(JsonElement ev, int groupIndex) =>
        ev.GetProperty("groups")[groupIndex].GetProperty("goingCount").GetInt32();

    // ---- creazione e permessi ----

    [SkippableFact]
    public async Task Admin_creates_an_event_with_several_groups()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var ev = await GetEvent(s.Admin, s.EventId);
        Assert.Equal(2, ev.GetProperty("groups").GetArrayLength());
        Assert.Equal("Admin", ev.GetProperty("myRole").GetString());
    }

    [SkippableFact]
    public async Task Event_creation_is_validated()
    {
        RequireDb();
        var (admin, _, _) = await api.LoginAsync();
        var clubId = await CreateClubAsync(admin);
        var future = api.Time.GetUtcNow().AddDays(1);
        var group = new { name = "Relax" };

        Task<HttpResponseMessage> Post(object body) => admin.PostAsJsonAsync($"/api/clubs/{clubId}/events", body);

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { title = "Uscita", meetingPointLabel = "Piazza", startsAt = future, visibility = "Public", groups = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { title = "Uscita", meetingPointLabel = "Piazza", startsAt = api.Time.GetUtcNow().AddDays(-1), visibility = "Public", groups = new[] { group } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { title = "Uscita", meetingPointLabel = "Piazza", startsAt = future, visibility = "Public",
            groups = new[] { new { name = "Relax", paceMinSecPerKm = 400, paceMaxSecPerKm = 300 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { title = "Uscita", meetingPointLabel = "Piazza", startsAt = future, visibility = "Public",
            groups = new[] { new { name = "Relax", capacity = 0 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(new { title = "Uscita", meetingPointLabel = "Piazza", startsAt = future, visibility = "Public", groups = new[] { group } })).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_of_another_club_cannot_create_modify_or_cancel_events()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var (otherAdmin, _, _) = await api.LoginAsync();
        await CreateClubAsync(otherAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await otherAdmin.PostAsJsonAsync($"/api/clubs/{s.ClubId}/events", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherAdmin.PatchAsJsonAsync($"/api/events/{s.EventId}", new { title = "Hackerato" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherAdmin.PostAsync($"/api/events/{s.EventId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherAdmin.PutAsJsonAsync($"/api/events/{s.EventId}/groups/{s.GroupIds[0]}", new { name = "Hackerato" })).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_can_edit_the_event_and_a_group()
    {
        RequireDb();
        var s = await CreateEventAsync();

        var patched = await s.Admin.PatchAsJsonAsync($"/api/events/{s.EventId}", new { title = "Giovedì Running", meetingPointLabel = "Parco" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var group = await s.Admin.PutAsJsonAsync($"/api/events/{s.EventId}/groups/{s.GroupIds[0]}",
            new { name = "Super relax", distanceMeters = 5000, capacity = 10 });
        Assert.Equal(HttpStatusCode.OK, group.StatusCode);

        var ev = await GetEvent(s.Admin, s.EventId);
        Assert.Equal("Giovedì Running", ev.GetProperty("title").GetString());
        Assert.Equal("Super relax", ev.GetProperty("groups")[0].GetProperty("name").GetString());
    }

    // ---- visibilità ----

    [SkippableFact]
    public async Task Public_event_is_visible_to_anyone_but_participant_names_are_hidden()
    {
        RequireDb();
        var s = await CreateEventAsync("Public");
        var (runner, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await Rsvp(runner, s.EventId, s.GroupIds[0])).StatusCode);

        var visitor = await GetEvent(api.NewClient(), s.EventId);
        Assert.Equal(1, Going(visitor, 0));
        Assert.Equal(JsonValueKind.Null, visitor.GetProperty("groups")[0].GetProperty("participants").ValueKind);

        var asAdmin = await GetEvent(s.Admin, s.EventId);
        Assert.Equal(1, asAdmin.GetProperty("groups")[0].GetProperty("participants").GetArrayLength());
    }

    [SkippableFact]
    public async Task Club_only_event_is_hidden_from_non_members_and_visitors()
    {
        RequireDb();
        var s = await CreateEventAsync("ClubOnly");
        var (stranger, _, _) = await api.LoginAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await api.NewClient().GetAsync($"/api/events/{s.EventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/events/{s.EventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Rsvp(stranger, s.EventId, s.GroupIds[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.GetAsync($"/api/events/{s.EventId}")).StatusCode);
    }

    [SkippableFact]
    public async Task Club_only_events_are_not_listed_for_non_members()
    {
        RequireDb();
        var s = await CreateEventAsync("ClubOnly");

        var visitorList = await (await api.NewClient().GetAsync($"/api/clubs/{s.ClubId}/events")).Content.ReadFromJsonAsync<JsonElement>();
        var memberList = await (await s.Admin.GetAsync($"/api/clubs/{s.ClubId}/events")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, visitorList.GetArrayLength());
        Assert.Equal(1, memberList.GetArrayLength());
    }

    [SkippableFact]
    public async Task Pending_member_cannot_see_club_only_events()
    {
        RequireDb();
        var s = await CreateEventAsync("ClubOnly");
        var (pending, _, _) = await api.LoginAsync();
        await pending.PostAsJsonAsync($"/api/clubs/{s.ClubId}/memberships", new { });

        Assert.Equal(HttpStatusCode.NotFound, (await pending.GetAsync($"/api/events/{s.EventId}")).StatusCode);
    }

    // ---- RSVP ----

    [SkippableFact]
    public async Task Rsvp_requires_login()
    {
        RequireDb();
        var s = await CreateEventAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Rsvp(api.NewClient(), s.EventId, s.GroupIds[0])).StatusCode);
    }

    [SkippableFact]
    public async Task Repeating_the_same_rsvp_does_not_duplicate_it()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var (runner, _, _) = await api.LoginAsync();

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await Rsvp(runner, s.EventId, s.GroupIds[0])).StatusCode);

        var ev = await GetEvent(runner, s.EventId);
        Assert.Equal(1, Going(ev, 0));
        Assert.Equal(0, Going(ev, 1));
    }

    [SkippableFact]
    public async Task Changing_group_moves_the_rsvp_instead_of_adding_a_second_one()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var (runner, _, _) = await api.LoginAsync();
        await Rsvp(runner, s.EventId, s.GroupIds[0]);
        await Rsvp(runner, s.EventId, s.GroupIds[1]);

        var ev = await GetEvent(runner, s.EventId);
        Assert.Equal(0, Going(ev, 0));
        Assert.Equal(1, Going(ev, 1));
        Assert.Equal(s.GroupIds[1], ev.GetProperty("myRsvp").GetProperty("paceGroupId").GetGuid());
    }

    [SkippableFact]
    public async Task Rsvp_to_a_group_of_another_event_is_rejected()
    {
        RequireDb();
        var a = await CreateEventAsync();
        var b = await CreateEventAsync();
        var (runner, _, _) = await api.LoginAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Rsvp(runner, a.EventId, b.GroupIds[0])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Rsvp(runner, a.EventId, null)).StatusCode);
    }

    [SkippableFact]
    public async Task Not_going_does_not_take_a_seat_and_withdrawing_removes_the_rsvp()
    {
        RequireDb();
        var s = await CreateEventAsync(capacities: [1, null]);
        var (first, _, _) = await api.LoginAsync();
        var (second, _, _) = await api.LoginAsync();

        await Rsvp(first, s.EventId, null, "NotGoing");
        Assert.Equal(0, Going(await GetEvent(first, s.EventId), 0));
        Assert.Equal(HttpStatusCode.OK, (await Rsvp(second, s.EventId, s.GroupIds[0])).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await second.DeleteAsync($"/api/events/{s.EventId}/rsvp")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await second.DeleteAsync($"/api/events/{s.EventId}/rsvp")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await GetEvent(second, s.EventId)).GetProperty("myRsvp").ValueKind);
    }

    [SkippableFact]
    public async Task Withdrawing_frees_the_seat_immediately()
    {
        RequireDb();
        var s = await CreateEventAsync(capacities: [1, null]);
        var (first, _, _) = await api.LoginAsync();
        var (second, _, _) = await api.LoginAsync();

        await Rsvp(first, s.EventId, s.GroupIds[0]);
        Assert.Equal(HttpStatusCode.Conflict, (await Rsvp(second, s.EventId, s.GroupIds[0])).StatusCode);

        await first.DeleteAsync($"/api/events/{s.EventId}/rsvp");
        Assert.Equal(HttpStatusCode.OK, (await Rsvp(second, s.EventId, s.GroupIds[0])).StatusCode);
    }

    [SkippableFact]
    public async Task Switching_to_a_full_group_keeps_the_previous_rsvp()
    {
        RequireDb();
        var s = await CreateEventAsync(capacities: [null, 1]);
        var (holder, _, _) = await api.LoginAsync();
        var (runner, _, _) = await api.LoginAsync();
        await Rsvp(holder, s.EventId, s.GroupIds[1]);
        await Rsvp(runner, s.EventId, s.GroupIds[0]);

        var switched = await Rsvp(runner, s.EventId, s.GroupIds[1]);

        Assert.Equal(HttpStatusCode.Conflict, switched.StatusCode);
        var ev = await GetEvent(runner, s.EventId);
        Assert.Equal(s.GroupIds[0], ev.GetProperty("myRsvp").GetProperty("paceGroupId").GetGuid());
        Assert.Equal(1, Going(ev, 0));
        Assert.Equal(1, Going(ev, 1));
    }

    [SkippableFact]
    public async Task Someone_already_in_a_full_group_can_confirm_again()
    {
        RequireDb();
        var s = await CreateEventAsync(capacities: [1, null]);
        var (runner, _, _) = await api.LoginAsync();
        await Rsvp(runner, s.EventId, s.GroupIds[0]);

        Assert.Equal(HttpStatusCode.OK, (await Rsvp(runner, s.EventId, s.GroupIds[0])).StatusCode);
    }

    [SkippableFact]
    public async Task Concurrent_requests_for_the_last_seat_never_overbook()
    {
        RequireDb();
        for (var round = 0; round < 5; round++)
        {
            var s = await CreateEventAsync(capacities: [1, null]);
            var runners = new List<HttpClient>();
            for (var i = 0; i < 8; i++) runners.Add((await api.LoginAsync()).Client);

            var results = await Task.WhenAll(runners.Select(r => Rsvp(r, s.EventId, s.GroupIds[0])));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, Going(await GetEvent(s.Admin, s.EventId), 0));
        }
    }

    [SkippableFact]
    public async Task Concurrent_retries_by_the_same_user_create_a_single_rsvp()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var (runner, _, _) = await api.LoginAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Rsvp(runner, s.EventId, s.GroupIds[0])));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, Going(await GetEvent(runner, s.EventId), 0));
    }

    [SkippableFact]
    public async Task Concurrent_group_switches_by_the_same_user_end_with_one_rsvp()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var (runner, _, _) = await api.LoginAsync();

        var results = await Task.WhenAll(
            Rsvp(runner, s.EventId, s.GroupIds[0]), Rsvp(runner, s.EventId, s.GroupIds[1]),
            Rsvp(runner, s.EventId, s.GroupIds[0]), Rsvp(runner, s.EventId, s.GroupIds[1]));

        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        var ev = await GetEvent(runner, s.EventId);
        Assert.Equal(1, Going(ev, 0) + Going(ev, 1));
    }

    [SkippableFact]
    public async Task Capacity_cannot_drop_below_confirmed_participants()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var (a, _, _) = await api.LoginAsync();
        var (b, _, _) = await api.LoginAsync();
        await Rsvp(a, s.EventId, s.GroupIds[0]);
        await Rsvp(b, s.EventId, s.GroupIds[0]);

        var tooLow = await s.Admin.PutAsJsonAsync($"/api/events/{s.EventId}/groups/{s.GroupIds[0]}", new { name = "Relax", capacity = 1 });
        var ok = await s.Admin.PutAsJsonAsync($"/api/events/{s.EventId}/groups/{s.GroupIds[0]}", new { name = "Relax", capacity = 2 });

        Assert.Equal(HttpStatusCode.Conflict, tooLow.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ---- annullamento e tempo ----

    [SkippableFact]
    public async Task Cancelled_event_rejects_new_rsvps_but_keeps_existing_ones()
    {
        RequireDb();
        var s = await CreateEventAsync();
        var (early, _, _) = await api.LoginAsync();
        var (late, _, _) = await api.LoginAsync();
        await Rsvp(early, s.EventId, s.GroupIds[0]);

        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/events/{s.EventId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/events/{s.EventId}/cancel", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Rsvp(late, s.EventId, s.GroupIds[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Rsvp(early, s.EventId, s.GroupIds[1])).StatusCode);
        var ev = await GetEvent(early, s.EventId);
        Assert.Equal("Cancelled", ev.GetProperty("status").GetString());
        Assert.Equal(1, Going(ev, 0));
        Assert.Equal(HttpStatusCode.Conflict, (await s.Admin.PatchAsJsonAsync($"/api/events/{s.EventId}", new { title = "Nuovo titolo" })).StatusCode);
    }

    [SkippableFact]
    public async Task Rsvp_is_rejected_once_the_event_has_started()
    {
        RequireDb();
        var s = await CreateEventAsync(startsIn: TimeSpan.FromHours(1));
        var (runner, _, _) = await api.LoginAsync();
        api.Time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.Conflict, (await Rsvp(runner, s.EventId, s.GroupIds[0])).StatusCode);
    }

    [SkippableFact]
    public async Task My_events_lists_only_upcoming_confirmed_participations()
    {
        RequireDb();
        var going = await CreateEventAsync();
        var notGoing = await CreateEventAsync();
        var (runner, _, _) = await api.LoginAsync();
        await Rsvp(runner, going.EventId, going.GroupIds[1]);
        await Rsvp(runner, notGoing.EventId, null, "NotGoing");

        var list = await (await runner.GetAsync("/api/me/events")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(going.EventId, list[0].GetProperty("eventId").GetGuid());
        Assert.Equal("Gruppo 2", list[0].GetProperty("groupName").GetString());
    }

    [SkippableFact]
    public async Task Unknown_event_returns_404()
    {
        RequireDb();
        var (runner, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await runner.GetAsync($"/api/events/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Rsvp(runner, Guid.NewGuid(), Guid.NewGuid())).StatusCode);
    }
}
