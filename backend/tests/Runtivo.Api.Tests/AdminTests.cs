using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Runtivo.Api.Data;

namespace Runtivo.Api.Tests;

/// <summary>Ogni test usa un'app propria in cui è amministratore un indirizzo creato apposta.</summary>
[Collection("api")]
public class AdminTests : IDisposable
{
    private readonly string adminEmail = $"admin{Guid.NewGuid():N}@example.com";
    private readonly ApiFixture app;
    private readonly MatchHarness harness;

    public AdminTests()
    {
        // L'elenco ha una voce con le maiuscole: l'indirizzo si confronta senza distinguerle.
        app = ApiFixture.WithSettings(new Dictionary<string, string> { ["Platform:AdminEmails:0"] = adminEmail.ToUpperInvariant() });
        harness = new MatchHarness(app);
    }

    public void Dispose() => app.Dispose();

    private void RequireDb() => Skip.IfNot(app.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private async Task<(HttpClient Client, Guid UserId)> AdminAsync()
    {
        var (client, _, id) = await app.LoginAsync(adminEmail);
        return (client, id);
    }

    private static async Task<Guid> ReportAsync(Runner reporter, Guid targetId, string reason = "Harassment")
    {
        var response = await ReportTests.Report(reporter.Client, targetId, reason, "dettagli riservati");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Resolve(HttpClient c, Guid reportId, string action, string? note = null) =>
        c.PostAsJsonAsync($"/api/admin/reports/{reportId}/resolve", new { action, note });

    [SkippableFact]
    public async Task Non_admins_get_403_on_every_admin_endpoint()
    {
        RequireDb();
        var user = await harness.RunnerAsync();
        var other = await harness.RunnerAsync();
        var reportId = await ReportAsync(user, other.UserId);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.GetAsync("/api/admin/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Resolve(user.Client, reportId, "Dismiss")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Resolve(user.Client, Guid.NewGuid(), "Suspend")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.PostAsJsonAsync($"/api/admin/users/{other.UserId}/suspend", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.PostAsync($"/api/admin/users/{other.UserId}/unsuspend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync("/api/admin/reports")).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_sees_open_reports_with_names_but_no_emails()
    {
        RequireDb();
        var (admin, _) = await AdminAsync();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();
        var reportId = await ReportAsync(reporter, target.UserId, "Unsafe");

        var raw = await (await admin.GetAsync("/api/admin/reports")).Content.ReadAsStringAsync();
        var list = JsonDocument.Parse(raw).RootElement;

        var item = list.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == reportId);
        Assert.Equal(target.DisplayName, item.GetProperty("reportedName").GetString());
        Assert.Equal(reporter.DisplayName, item.GetProperty("reporterName").GetString());
        Assert.Equal("Unsafe", item.GetProperty("reason").GetString());
        Assert.Equal("dettagli riservati", item.GetProperty("details").GetString());
        Assert.DoesNotContain(reporter.Email, raw);
        Assert.DoesNotContain(target.Email, raw);
    }

    [SkippableFact]
    public async Task Dismiss_closes_the_report_and_records_the_reviewer()
    {
        RequireDb();
        var (admin, adminId) = await AdminAsync();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();
        var reportId = await ReportAsync(reporter, target.UserId);

        Assert.Equal(HttpStatusCode.OK, (await Resolve(admin, reportId, "Dismiss", "Nessuna violazione")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Resolve(admin, reportId, "Dismiss")).StatusCode);

        var open = JsonDocument.Parse(await (await admin.GetAsync("/api/admin/reports")).Content.ReadAsStringAsync()).RootElement;
        Assert.DoesNotContain(open.EnumerateArray(), r => r.GetProperty("id").GetGuid() == reportId);
        using var scope = app.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Equal(Runtivo.Api.Safety.ReportStatus.Dismissed, stored.Status);
        Assert.Equal(adminId, stored.ReviewedByUserId);
        Assert.Equal("Nessuna violazione", stored.ResolutionNote);
        // Archiviare non tocca la persona segnalata.
        Assert.Equal(HttpStatusCode.OK, (await target.Client.GetAsync("/api/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Suspending_blocks_login_hides_the_person_ends_the_session_and_leaves_future_runs()
    {
        RequireDb();
        var (admin, _) = await AdminAsync();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();
        var third = await harness.RunnerAsync();
        var reportId = await ReportAsync(reporter, target.UserId);
        var created = await third.Client.PostAsJsonAsync("/api/runs", new
        {
            placeId = await harness.PlaceAsync(harness.DefaultPlace), startsAt = app.Time.GetUtcNow().AddDays(3),
            inviteeIds = new[] { target.UserId, reporter.UserId },
        });
        var runId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.True(MatchHarness.Contains(await MatchHarness.MatchesAsync(reporter.Client), target.UserId));

        Assert.Equal(HttpStatusCode.OK, (await Resolve(admin, reportId, "Suspend", "Molestie")).StatusCode);

        // Sessione già aperta: cade. Nuovo accesso: rifiutato.
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.Client.GetAsync("/api/me")).StatusCode);
        var again = app.NewClient();
        await again.PostAsJsonAsync("/api/auth/request-link", new { email = target.Email });
        var token = ApiFixture.TokenFromLink(app.Mail.To(target.Email)[^1].Link);
        Assert.Equal(HttpStatusCode.Forbidden, (await again.PostAsJsonAsync("/api/auth/verify", new { token })).StatusCode);
        // Non compare più né nei match né nelle uscite.
        Assert.False(MatchHarness.Contains(await MatchHarness.MatchesAsync(reporter.Client), target.UserId));
        var run = await (await third.Client.GetAsync($"/api/runs/{runId}")).Content.ReadFromJsonAsync<JsonElement>();
        var status = run.GetProperty("participants").EnumerateArray().First(p => p.GetProperty("userId").GetGuid() == target.UserId).GetProperty("status").GetString();
        Assert.Equal("Declined", status);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntivoDbContext>();
        Assert.Equal(Runtivo.Api.Matching.IntentStatus.Paused, (await db.RunIntents.AsNoTracking().FirstAsync(i => i.UserId == target.UserId)).Status);
    }

    [SkippableFact]
    public async Task Suspending_resolves_all_open_reports_against_the_person()
    {
        RequireDb();
        var (admin, _) = await AdminAsync();
        var first = await harness.RunnerAsync();
        var second = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();
        var firstReport = await ReportAsync(first, target.UserId);
        var secondReport = await ReportAsync(second, target.UserId, "Spam");

        await Resolve(admin, firstReport, "Suspend");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntivoDbContext>();
        Assert.All(await db.Reports.AsNoTracking().Where(r => r.Id == firstReport || r.Id == secondReport).ToListAsync(),
            r => Assert.Equal(Runtivo.Api.Safety.ReportStatus.ActionTaken, r.Status));
    }

    [SkippableFact]
    public async Task Unsuspend_restores_login()
    {
        RequireDb();
        var (admin, _) = await AdminAsync();
        var target = await harness.RunnerAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{target.UserId}/suspend", new { reason = "Verifica" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.Client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{target.UserId}/unsuspend", null)).StatusCode);

        var (back, _, id) = await app.LoginAsync(target.Email);
        Assert.Equal(target.UserId, id);
        Assert.Equal(HttpStatusCode.OK, (await back.GetAsync("/api/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Admins_cannot_suspend_themselves_or_unknown_users()
    {
        RequireDb();
        var (admin, adminId) = await AdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/users/{adminId}/suspend", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/suspend", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(admin, Guid.NewGuid(), "Dismiss")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Resolve(admin, Guid.NewGuid(), "Boh")).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_actions_leave_audit_entries_with_the_actor()
    {
        RequireDb();
        var (admin, adminId) = await AdminAsync();
        var reporter = await harness.RunnerAsync();
        var target = await harness.RunnerAsync();
        var reportId = await ReportAsync(reporter, target.UserId);

        await Resolve(admin, reportId, "Suspend");
        await admin.PostAsync($"/api/admin/users/{target.UserId}/unsuspend", null);

        using var scope = app.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().AuditEntries.AsNoTracking()
            .Where(a => a.ActorUserId == adminId).Select(a => a.Action).ToListAsync();
        Assert.Contains("admin.user.suspend", actions);
        Assert.Contains("admin.user.unsuspend", actions);
    }

    [SkippableFact]
    public async Task Me_exposes_isPlatformAdmin_only_for_admins_and_matching_ignores_case()
    {
        RequireDb();
        var (admin, _) = await AdminAsync();
        var user = await harness.RunnerAsync();

        var adminMe = await (await admin.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        var userMe = await (await user.Client.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(adminMe.GetProperty("isPlatformAdmin").GetBoolean());
        Assert.False(userMe.GetProperty("isPlatformAdmin").GetBoolean());
    }
}
