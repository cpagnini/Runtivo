using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Runtivo.Api.Tests;

/// <summary>Per questi test gli account sono "nuovi" (7 giorni): serve un'app con quella opzione attiva.</summary>
[Collection("api")]
public class NewAccountLimitTests(ApiFixture api) : IDisposable
{
    private readonly ApiFixture strict = ApiFixture.WithSettings(new Dictionary<string, string> { ["App:NewAccountDays"] = "7" });
    private MatchHarness? strictHarness;
    private MatchHarness Strict => strictHarness ??= new MatchHarness(strict);
    private readonly MatchHarness normal = new(api);

    public void Dispose() => strict.Dispose();

    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private static Task<HttpResponseMessage> CreateRun(ApiFixture app, MatchHarness h, Runner organizer, Guid inviteeId, Guid placeId, TimeSpan startsIn) =>
        organizer.Client.PostAsJsonAsync("/api/runs", new { placeId, startsAt = app.Time.GetUtcNow() + startsIn, inviteeIds = new[] { inviteeId } });

    private static async Task<List<Runner>> PeopleAsync(MatchHarness h, int count)
    {
        var people = new List<Runner>();
        for (var i = 0; i < count; i++) people.Add(await h.RunnerAsync());
        return people;
    }

    [SkippableFact]
    public async Task New_account_can_show_interest_five_times_per_day_not_six()
    {
        RequireDb();
        var me = await Strict.RunnerAsync();
        var others = await PeopleAsync(Strict, 6);

        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await InterestTests.Interest(me.Client, others[i].UserId)).StatusCode);

        var sixth = await InterestTests.Interest(me.Client, others[5].UserId);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.Contains("account da poco", await sixth.Content.ReadAsStringAsync());
        // Ripetere un interesse già mostrato non conta come un nuovo interesse.
        Assert.Equal(HttpStatusCode.OK, (await InterestTests.Interest(me.Client, others[0].UserId)).StatusCode);
    }

    [SkippableFact]
    public async Task The_daily_window_slides_for_new_accounts()
    {
        RequireDb();
        var me = await Strict.RunnerAsync();
        var others = await PeopleAsync(Strict, 6);
        for (var i = 0; i < 5; i++) await InterestTests.Interest(me.Client, others[i].UserId);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await InterestTests.Interest(me.Client, others[5].UserId)).StatusCode);

        strict.Time.Advance(TimeSpan.FromHours(25));

        Assert.Equal(HttpStatusCode.OK, (await InterestTests.Interest(me.Client, others[5].UserId)).StatusCode);
    }

    [SkippableFact]
    public async Task New_account_has_a_single_open_proposal()
    {
        RequireDb();
        var me = await Strict.RunnerAsync();
        var invitee = await Strict.RunnerAsync();
        var placeId = await Strict.PlaceAsync(Strict.DefaultPlace);

        Assert.Equal(HttpStatusCode.Created, (await CreateRun(strict, Strict, me, invitee.UserId, placeId, TimeSpan.FromDays(3))).StatusCode);

        var second = await CreateRun(strict, Strict, me, invitee.UserId, placeId, TimeSpan.FromDays(4));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("una sola proposta", await second.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Limits_lift_after_the_new_account_period()
    {
        RequireDb();
        var me = await Strict.RunnerAsync();
        var invitee = await Strict.RunnerAsync();
        var placeId = await Strict.PlaceAsync(Strict.DefaultPlace);
        Assert.Equal(HttpStatusCode.Created, (await CreateRun(strict, Strict, me, invitee.UserId, placeId, TimeSpan.FromDays(20))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreateRun(strict, Strict, me, invitee.UserId, placeId, TimeSpan.FromDays(21))).StatusCode);

        strict.Time.Advance(TimeSpan.FromDays(8)); // oltre i 7 giorni; la prima uscita è ancora in programma

        Assert.Equal(HttpStatusCode.Created, (await CreateRun(strict, Strict, me, invitee.UserId, placeId, TimeSpan.FromDays(21))).StatusCode);
    }

    [SkippableFact]
    public async Task Parallel_requests_cannot_exceed_the_interest_limit()
    {
        RequireDb();
        var me = await Strict.RunnerAsync();
        var others = await PeopleAsync(Strict, 10);

        var results = await Task.WhenAll(others.Select(o => InterestTests.Interest(me.Client, o.UserId)));

        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
    }

    [SkippableFact]
    public async Task Parallel_proposals_cannot_exceed_the_open_proposal_limit()
    {
        RequireDb();
        var me = await Strict.RunnerAsync();
        var invitee = await Strict.RunnerAsync();
        var placeId = await Strict.PlaceAsync(Strict.DefaultPlace);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => CreateRun(strict, Strict, me, invitee.UserId, placeId, TimeSpan.FromDays(3 + i))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [SkippableFact]
    public async Task Established_accounts_keep_the_normal_limits()
    {
        RequireDb();
        // L'app normale ha il periodo "nuovo account" disattivato: nessuno dei limiti stretti si applica.
        var me = await normal.RunnerAsync();
        var others = await PeopleAsync(normal, 7);

        foreach (var other in others) Assert.Equal(HttpStatusCode.OK, (await InterestTests.Interest(me.Client, other.UserId)).StatusCode);
    }
}
