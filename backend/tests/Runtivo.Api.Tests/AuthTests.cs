using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Runtivo.Api.Common;

namespace Runtivo.Api.Tests;

[Collection("api")]
public class AuthTests(ApiFixture api)
{
    private void RequireDb() => Skip.IfNot(api.Available, $"{ApiFixture.TestDbVariable} non impostata");

    [SkippableFact]
    public async Task Magic_link_logs_the_user_in_and_creates_the_account()
    {
        RequireDb();
        var (client, email, _) = await api.LoginAsync();

        var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());
    }

    [SkippableFact]
    public async Task Me_requires_a_session()
    {
        RequireDb();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().GetAsync("/api/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Login_token_cannot_be_reused()
    {
        RequireDb();
        var email = ApiFixture.NewEmail();
        var client = api.NewClient();
        await client.PostAsJsonAsync("/api/auth/request-link", new { email });
        var token = ApiFixture.TokenFromLink(api.Mail.To(email)[0].Link);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/verify", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.NewClient().PostAsJsonAsync("/api/auth/verify", new { token })).StatusCode);
    }

    [SkippableFact]
    public async Task Login_token_expires_after_15_minutes()
    {
        RequireDb();
        var email = ApiFixture.NewEmail();
        var client = api.NewClient();
        await client.PostAsJsonAsync("/api/auth/request-link", new { email });
        var token = ApiFixture.TokenFromLink(api.Mail.To(email)[0].Link);

        api.Time.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify", new { token })).StatusCode);
    }

    [SkippableFact]
    public async Task Garbage_token_is_rejected()
    {
        RequireDb();
        var response = await api.NewClient().PostAsJsonAsync("/api/auth/verify", new { token = "non-esiste" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task Link_requests_are_limited_per_email_but_always_answer_202()
    {
        RequireDb();
        var email = ApiFixture.NewEmail();
        var client = api.NewClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/request-link", new { email })).StatusCode);

        Assert.Equal(3, api.Mail.To(email).Count);
    }

    [SkippableFact]
    public async Task Invalid_email_is_rejected()
    {
        RequireDb();
        var response = await api.NewClient().PostAsJsonAsync("/api/auth/request-link", new { email = "non-una-email" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task State_changing_requests_without_csrf_header_are_refused()
    {
        RequireDb();
        var response = await api.NewClient(csrfHeader: false)
            .PostAsJsonAsync("/api/auth/request-link", new { email = ApiFixture.NewEmail() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Profile_can_be_updated_and_validated()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();

        var ok = await client.PatchAsJsonAsync("/api/me", new { displayName = "Marco", city = "Prato", preferredPaceSecPerKm = 330 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Marco", body.GetProperty("displayName").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/me", new { preferredPaceSecPerKm = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/me", new { displayName = " " })).StatusCode);
    }

    [SkippableFact]
    public async Task Logout_ends_the_session()
    {
        RequireDb();
        var (client, _, _) = await api.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }
}

public class HelperTests
{
    [Theory]
    [InlineData("Martedì Running!", "martedi-running")]
    [InlineData("  A&B  club ", "a-b-club")]
    [InlineData("!!!", "club")]
    public void Slugs_are_url_safe(string name, string expected) => Assert.Equal(expected, Slugs.From(name));

    [Theory]
    [InlineData("Marco@Example.COM ", "marco@example.com")]
    [InlineData("senza-chiocciola", null)]
    [InlineData("a@b", null)]
    [InlineData("", null)]
    public void Emails_are_normalized(string input, string? expected) => Assert.Equal(expected, Emails.Normalize(input));

    [Fact]
    public void Tokens_are_unique_and_only_their_hash_is_derivable()
    {
        var (t1, h1) = Tokens.Generate();
        var (t2, _) = Tokens.Generate();
        Assert.NotEqual(t1, t2);
        Assert.Equal(h1, Tokens.Hash(t1));
        Assert.DoesNotContain(t1, h1);
    }
}
