using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Runtivo.Api.Data;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class ReportTests(ApiFixture api)
{
    private readonly MatchHarness harness = new(api);

    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    internal static Task<HttpResponseMessage> Report(HttpClient c, Guid reportedUserId, string reason = "Harassment", string? details = null, Guid? runId = null) =>
        c.PostAsJsonAsync("/api/reports", new { reportedUserId, reason, details, runId });

    [SkippableFact]
    public async Task Report_requires_login()
    {
        RequireDb();
        var target = await harness.RunnerAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Report(api.NewClient(), target.UserId)).StatusCode);
    }

    [SkippableFact]
    public async Task Report_is_created_and_target_never_learns_who_reported()
    {
        RequireDb();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();

        var response = await Report(reporter.Client, target.UserId, "Unsafe", "Messaggi insistenti fuori dall'app");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Profilo ed export della persona segnalata non contengono né segnalazioni né chi le ha fatte.
        var seenByTarget = string.Concat(
            await (await target.Client.GetAsync("/api/me")).Content.ReadAsStringAsync(),
            await (await target.Client.GetAsync("/api/me/export")).Content.ReadAsStringAsync());
        Assert.DoesNotContain(reporter.Email, seenByTarget);
        Assert.DoesNotContain(reporter.UserId.ToString(), seenByTarget);
        Assert.DoesNotContain("insistenti", seenByTarget);
    }

    [SkippableFact]
    public async Task Cannot_report_yourself_or_unknown_people_or_send_bad_input()
    {
        RequireDb();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Report(reporter.Client, reporter.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(reporter.Client, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(reporter.Client, target.UserId, "Inesistente")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(reporter.Client, target.UserId, details: new string('x', 501))).StatusCode);
    }

    [SkippableFact]
    public async Task Duplicate_open_report_returns_the_same_id()
    {
        RequireDb();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();

        var first = await Report(reporter.Client, target.UserId);
        var second = await Report(reporter.Client, target.UserId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),
            (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    [SkippableFact]
    public async Task Daily_limit_of_ten_reports()
    {
        RequireDb();
        var reporter = await harness.RunnerAsync();
        for (var i = 0; i < 10; i++)
        {
            var target = await harness.RunnerAsync();
            Assert.Equal(HttpStatusCode.Created, (await Report(reporter.Client, target.UserId)).StatusCode);
        }

        var eleventh = await harness.RunnerAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Report(reporter.Client, eleventh.UserId)).StatusCode);
    }

    [SkippableFact]
    public async Task Run_reports_require_both_to_be_participants()
    {
        RequireDb();
        var organizer = await harness.RunnerAsync();
        var invitee = await harness.RunnerAsync();
        var outsider = await harness.RunnerAsync();
        var create = await organizer.Client.PostAsJsonAsync("/api/runs", new
        {
            placeId = await harness.PlaceAsync(harness.DefaultPlace),
            startsAt = api.Time.GetUtcNow().AddDays(3),
            inviteeIds = new[] { invitee.UserId },
        });
        var runId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, (await Report(invitee.Client, organizer.UserId, runId: runId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(outsider.Client, organizer.UserId, runId: runId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(invitee.Client, outsider.UserId, runId: runId)).StatusCode);
    }

    [SkippableFact]
    public async Task Reporter_deletion_anonymises_the_report_but_keeps_it()
    {
        RequireDb();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();
        var id = (await (await Report(reporter.Client, target.UserId)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var delete = await reporter.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me") { Content = JsonContent.Create(new { confirmEmail = reporter.Email }) });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var scope = api.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().Reports.AsNoTracking().FirstAsync(r => r.Id == id);
        Assert.Null(stored.ReporterUserId);
        Assert.Equal(target.UserId, stored.ReportedUserId);
    }

    [SkippableFact]
    public async Task Export_lists_reports_I_filed_but_not_those_filed_against_me()
    {
        RequireDb();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();
        await Report(reporter.Client, target.UserId, "Spam");

        var mine = JsonDocument.Parse(await (await reporter.Client.GetAsync("/api/me/export")).Content.ReadAsStringAsync()).RootElement;
        var against = JsonDocument.Parse(await (await target.Client.GetAsync("/api/me/export")).Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(1, mine.GetProperty("reportsFiled").GetArrayLength());
        Assert.Equal(0, against.GetProperty("reportsFiled").GetArrayLength());
    }
}
