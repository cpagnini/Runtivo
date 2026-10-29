using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runtivo.Api.Auth;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Tests;

public class FakeGoogleClient : IGoogleClient
{
    public Func<string, GoogleIdentity?> Handler { get; set; } = _ => null;
    public string? LastVerifier { get; private set; }
    public string? LastRedirectUri { get; private set; }

    public Task<GoogleIdentity?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        LastVerifier = codeVerifier;
        LastRedirectUri = redirectUri;
        return Task.FromResult(Handler(code));
    }
}

/// <summary>Accesso con Google con un provider simulato: lo scambio del codice con Google vero non si può provare in automatico.</summary>
[Collection("api")]
public class GoogleAuthTests : IDisposable
{
    private const string ClientId = "test-client.apps.googleusercontent.com";

    private readonly FakeGoogleClient fake = new();
    private readonly ApiFixture app;

    public GoogleAuthTests()
    {
        app = ApiFixture.WithSettings(
            new Dictionary<string, string> { ["Auth:Google:ClientId"] = ClientId, ["Auth:Google:ClientSecret"] = "segreto-di-prova" },
            s =>
            {
                s.RemoveAll<IGoogleClient>();
                s.AddSingleton<IGoogleClient>(fake);
            });
    }

    public void Dispose() => app.Dispose();

    private void RequireDb() => Skip.IfNot(app.Available, $"{ApiFixture.TestDbVariable} non impostata");

    private GoogleIdentity Identity(string email, string? sub = null, string? nonce = null, bool verified = true, string iss = "https://accounts.google.com",
        string aud = ClientId, TimeSpan? validFor = null) =>
        new(sub ?? "sub-" + Guid.NewGuid().ToString("N"), email, verified, nonce, iss, aud, app.Time.GetUtcNow() + (validFor ?? TimeSpan.FromHours(1)));

    private static Uri LocationOf(HttpResponseMessage r) => r.Headers.Location!.IsAbsoluteUri ? r.Headers.Location : new Uri(new Uri("http://localhost"), r.Headers.Location);

    /// <summary>Avvia il flusso e restituisce (risposta, state, nonce, code_challenge).</summary>
    private static async Task<(HttpResponseMessage Response, string State, string Nonce, string Challenge)> StartAsync(HttpClient c, string? next = null)
    {
        var response = await c.GetAsync("/api/auth/google/start" + (next is null ? "" : "?next=" + Uri.EscapeDataString(next)));
        var query = HttpUtility.ParseQueryString(LocationOf(response).Query);
        return (response, query["state"] ?? "", query["nonce"] ?? "", query["code_challenge"] ?? "");
    }

    /// <summary>Esegue avvio e ritorno da Google; <paramref name="make"/> costruisce l'identità che "Google" restituisce.</summary>
    private async Task<HttpResponseMessage> LoginAsync(HttpClient c, Func<string, GoogleIdentity?> make, string? next = null, string? stateOverride = null)
    {
        var (_, state, nonce, _) = await StartAsync(c, next);
        fake.Handler = _ => make(nonce);
        return await c.GetAsync($"/api/auth/google/callback?code=codice&state={Uri.EscapeDataString(stateOverride ?? state)}");
    }

    private static string Target(HttpResponseMessage r) => r.Headers.Location!.OriginalString;

    [SkippableFact]
    public async Task Providers_endpoint_reports_google_only_when_configured()
    {
        RequireDb();
        var configured = await (await app.NewClient().GetAsync("/api/auth/providers")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(configured.GetProperty("google").GetBoolean());

        using var plain = ApiFixture.WithSettings(new Dictionary<string, string>());
        var off = await (await plain.NewClient().GetAsync("/api/auth/providers")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(off.GetProperty("google").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await plain.NewClient(followRedirects: false).GetAsync("/api/auth/google/start")).StatusCode);
    }

    [SkippableFact]
    public async Task Start_redirects_to_google_with_pkce_state_nonce_and_minimal_scope()
    {
        RequireDb();
        var client = app.NewClient(followRedirects: false);

        var (response, state, nonce, challenge) = await StartAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = LocationOf(response);
        Assert.Equal("accounts.google.com", location.Host);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("http://localhost:3000/api/auth/google/callback", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.True(state.Length >= 40 && nonce.Length >= 40 && challenge.Length >= 40);
        Assert.DoesNotContain("segreto-di-prova", location.ToString());
        var cookie = string.Join(';', response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("runtivo.oauth", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task The_code_verifier_sent_to_google_matches_the_challenge()
    {
        RequireDb();
        var client = app.NewClient(followRedirects: false);
        var (_, state, nonce, challenge) = await StartAsync(client);
        fake.Handler = _ => Identity(ApiFixture.NewEmail(), nonce: nonce);

        await client.GetAsync($"/api/auth/google/callback?code=c&state={Uri.EscapeDataString(state)}");

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(fake.LastVerifier!))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, challenge);
        Assert.Equal("http://localhost:3000/api/auth/google/callback", fake.LastRedirectUri);
    }

    [SkippableFact]
    public async Task Callback_creates_the_account_signs_in_and_keeps_the_default_name()
    {
        RequireDb();
        var client = app.NewClient(followRedirects: false);
        var email = ApiFixture.NewEmail();

        var response = await LoginAsync(client, nonce => Identity(email, nonce: nonce));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", Target(response));
        var me = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, me.GetProperty("email").GetString());
        // Il nome non si importa da Google: resta quello predefinito, da scegliere nel profilo.
        Assert.Equal(email[..email.IndexOf('@')], me.GetProperty("displayName").GetString());
    }

    [SkippableFact]
    public async Task An_existing_account_is_linked_by_verified_email()
    {
        RequireDb();
        var email = ApiFixture.NewEmail();
        var (_, _, existingId) = await app.LoginAsync(email);
        var client = app.NewClient(followRedirects: false);

        await LoginAsync(client, nonce => Identity(email, nonce: nonce));

        var me = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(existingId, me.GetProperty("id").GetGuid());
    }

    [SkippableFact]
    public async Task A_known_subject_logs_in_to_the_same_account_even_if_the_google_email_changed()
    {
        RequireDb();
        var sub = "sub-" + Guid.NewGuid().ToString("N");
        var first = ApiFixture.NewEmail();
        var firstClient = app.NewClient(followRedirects: false);
        await LoginAsync(firstClient, nonce => Identity(first, sub, nonce));
        var id = (await (await firstClient.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var secondClient = app.NewClient(followRedirects: false);
        await LoginAsync(secondClient, nonce => Identity(ApiFixture.NewEmail(), sub, nonce));

        var me = await (await secondClient.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, me.GetProperty("id").GetGuid());
        Assert.Equal(first, me.GetProperty("email").GetString());
    }

    [SkippableFact]
    public async Task Unverified_email_is_refused_and_nobody_is_signed_in()
    {
        RequireDb();
        var client = app.NewClient(followRedirects: false);

        var response = await LoginAsync(client, nonce => Identity(ApiFixture.NewEmail(), nonce: nonce, verified: false));

        Assert.Equal("/accedi?errore=email", Target(response));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Tampered_or_replayed_flows_are_refused()
    {
        RequireDb();
        // state sbagliato
        var client = app.NewClient(followRedirects: false);
        Assert.Equal("/accedi?errore=sessione", Target(await LoginAsync(client, nonce => Identity(ApiFixture.NewEmail(), nonce: nonce), stateOverride: "altro")));

        // nessun cookie del flusso (callback aperto da zero)
        var bare = app.NewClient(followRedirects: false);
        fake.Handler = _ => Identity(ApiFixture.NewEmail(), nonce: "x");
        Assert.Equal("/accedi?errore=sessione", Target(await bare.GetAsync("/api/auth/google/callback?code=c&state=s")));

        // nonce diverso, destinatario diverso, emittente diverso, token scaduto, scambio rifiutato
        Assert.Equal("/accedi?errore=google", Target(await LoginAsync(app.NewClient(followRedirects: false), _ => Identity(ApiFixture.NewEmail(), nonce: "non-e-questo"))));
        Assert.Equal("/accedi?errore=google", Target(await LoginAsync(app.NewClient(followRedirects: false), n => Identity(ApiFixture.NewEmail(), nonce: n, aud: "altra-app"))));
        Assert.Equal("/accedi?errore=google", Target(await LoginAsync(app.NewClient(followRedirects: false), n => Identity(ApiFixture.NewEmail(), nonce: n, iss: "https://evil.example"))));
        Assert.Equal("/accedi?errore=google", Target(await LoginAsync(app.NewClient(followRedirects: false), n => Identity(ApiFixture.NewEmail(), nonce: n, validFor: TimeSpan.FromMinutes(-1)))));
        Assert.Equal("/accedi?errore=google", Target(await LoginAsync(app.NewClient(followRedirects: false), _ => null)));
    }

    [SkippableFact]
    public async Task The_flow_cookie_is_single_use()
    {
        RequireDb();
        var client = app.NewClient(followRedirects: false);
        var (_, state, nonce, _) = await StartAsync(client);
        fake.Handler = _ => Identity(ApiFixture.NewEmail(), nonce: nonce);

        var first = await client.GetAsync($"/api/auth/google/callback?code=c&state={Uri.EscapeDataString(state)}");
        var second = await client.GetAsync($"/api/auth/google/callback?code=c&state={Uri.EscapeDataString(state)}");

        Assert.Equal("/", Target(first));
        Assert.Equal("/accedi?errore=sessione", Target(second));
    }

    [SkippableFact]
    public async Task User_denying_access_at_google_comes_back_with_a_clear_reason()
    {
        RequireDb();
        var client = app.NewClient(followRedirects: false);
        await StartAsync(client);

        var response = await client.GetAsync("/api/auth/google/callback?error=access_denied");

        Assert.Equal("/accedi?errore=annullato", Target(response));
    }

    [SkippableFact]
    public async Task Suspended_users_cannot_sign_in_with_google()
    {
        RequireDb();
        var email = ApiFixture.NewEmail();
        var (_, _, id) = await app.LoginAsync(email);
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().Users.Where(u => u.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended));
        var client = app.NewClient(followRedirects: false);

        var response = await LoginAsync(client, nonce => Identity(email, nonce: nonce));

        Assert.Equal("/accedi?errore=sospeso", Target(response));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [SkippableTheory]
    [InlineData("//evil.example", "/")]
    [InlineData("https://evil.example/x", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/club/abc?x=1", "/club/abc?x=1")]
    public async Task The_return_path_cannot_point_to_other_sites(string next, string expected)
    {
        RequireDb();
        var client = app.NewClient(followRedirects: false);

        var response = await LoginAsync(client, nonce => Identity(ApiFixture.NewEmail(), nonce: nonce), next);

        Assert.Equal(expected, Target(response));
    }

    [SkippableFact]
    public async Task Deleting_the_account_removes_the_external_login()
    {
        RequireDb();
        var email = ApiFixture.NewEmail();
        var sub = "sub-" + Guid.NewGuid().ToString("N");
        var client = app.NewClient(followRedirects: false);
        await LoginAsync(client, nonce => Identity(email, sub, nonce));

        var delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me") { Content = JsonContent.Create(new { confirmEmail = email }) });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().ExternalLogins.AnyAsync(l => l.Subject == sub));
    }

    [Fact]
    public void Id_token_payload_is_parsed_without_trusting_unexpected_shapes()
    {
        string Jwt(object payload) => "e30." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".firma";

        var ok = GoogleClient.ParseIdToken(Jwt(new { sub = "1", email = "a@b.it", email_verified = true, iss = "accounts.google.com", aud = "x", exp = 4102444800L, nonce = "n" }));
        Assert.Equal("a@b.it", ok!.Email);
        Assert.True(ok.EmailVerified);
        Assert.Equal("n", ok.Nonce);

        Assert.False(GoogleClient.ParseIdToken(Jwt(new { sub = "1", email = "a@b.it", email_verified = "false", iss = "i", aud = "x", exp = 1L }))!.EmailVerified);
        Assert.Null(GoogleClient.ParseIdToken(Jwt(new { sub = "1", iss = "i", aud = "x", exp = 1L }))); // senza email
        Assert.Null(GoogleClient.ParseIdToken("non-un-jwt"));
    }
}
