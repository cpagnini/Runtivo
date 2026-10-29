using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Auth;

/// <summary>Legame tra un account Runtivo e l'identità su un provider esterno (per ora solo Google), riconosciuta dal suo "subject".</summary>
public class ExternalLogin
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string Subject { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class GoogleAuthEndpoints
{
    private const string Provider = "google";
    private const string CookieName = "runtivo.oauth";
    private const string CookiePath = "/api/auth/google";
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);
    private static readonly string[] Issuers = ["https://accounts.google.com", "accounts.google.com"];

    private record FlowState(string State, string Verifier, string Nonce, string Next, DateTimeOffset ExpiresAt);

    public static void MapGoogleAuthEndpoints(this WebApplication app)
    {
        // Il frontend mostra il pulsante solo se il provider è configurato.
        app.MapGet("/api/auth/providers", (IOptions<GoogleOptions> google) => Results.Ok(new { google = google.Value.Enabled }));

        var auth = app.MapGroup("/api/auth/google");

        // Avvio: flusso "authorization code" con PKCE, state e nonce; lo stato del flusso resta in un cookie protetto e a vita breve.
        auth.MapGet("/start", (string? next, HttpContext http, IOptions<GoogleOptions> google, IOptions<AppOptions> app,
            IDataProtectionProvider protection, TimeProvider time) =>
        {
            if (!google.Value.Enabled) return Http.Problem(404, "Accesso con Google non disponibile.");

            var verifier = Tokens.Generate().Token; // 43 caratteri URL-safe: lunghezza valida per PKCE
            var flow = new FlowState(Tokens.Generate().Token, verifier, Tokens.Generate().Token, SafeNext(next), time.GetUtcNow() + FlowLifetime);
            http.Response.Cookies.Append(CookieName, protection.CreateProtector("runtivo.google.flow").Protect(JsonSerializer.Serialize(flow)), new CookieOptions
            {
                HttpOnly = true,
                // Lax: il ritorno da Google è una navigazione di primo livello, e solo così il cookie viene rimandato.
                SameSite = SameSiteMode.Lax,
                Secure = app.Value.RequireSecureCookie,
                MaxAge = FlowLifetime,
                Path = CookiePath,
            });

            var query = new Dictionary<string, string>
            {
                ["client_id"] = google.Value.ClientId!,
                ["redirect_uri"] = RedirectUri(app.Value),
                ["response_type"] = "code",
                ["scope"] = "openid email", // solo l'email: nome e foto non si chiedono
                ["state"] = flow.State,
                ["nonce"] = flow.Nonce,
                ["code_challenge"] = Challenge(verifier),
                ["code_challenge_method"] = "S256",
                ["prompt"] = "select_account",
            };
            return Results.Redirect($"{AuthorizeEndpoint}?{string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"))}");
        }).RequireRateLimiting("auth");

        auth.MapGet("/callback", async (string? code, string? state, string? error, HttpContext http, RuntivoDbContext db,
            IGoogleClient client, IOptions<GoogleOptions> google, IOptions<AppOptions> app, IDataProtectionProvider protection,
            TimeProvider time, CancellationToken ct) =>
        {
            if (!google.Value.Enabled) return Http.Problem(404, "Accesso con Google non disponibile.");
            var now = time.GetUtcNow();

            // Il cookie del flusso vale una sola volta: si legge e si elimina sempre.
            var flow = ReadFlow(http, protection);
            http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath });

            if (!string.IsNullOrEmpty(error)) return Fail("annullato");
            if (flow is null || flow.ExpiresAt <= now || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(code) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(flow.State)))
                return Fail("sessione");

            var identity = await client.ExchangeCodeAsync(code, flow.Verifier, RedirectUri(app.Value), ct);
            if (identity is null) return Fail("google");
            // Emittente, destinatario, scadenza e nonce: chi ha rubato un codice o un token di un'altra app non entra.
            if (!Issuers.Contains(identity.Issuer) || identity.Audience != google.Value.ClientId || identity.ExpiresAt <= now ||
                string.IsNullOrEmpty(identity.Nonce) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(identity.Nonce), Encoding.UTF8.GetBytes(flow.Nonce)))
                return Fail("google");
            // Collegare un account per email è sicuro solo se Google garantisce che l'email è verificata.
            var email = Emails.Normalize(identity.Email);
            if (!identity.EmailVerified || email is null) return Fail("email");

            var link = await db.ExternalLogins.FirstOrDefaultAsync(l => l.Provider == Provider && l.Subject == identity.Subject, ct);
            var user = link is not null
                ? await db.Users.FirstOrDefaultAsync(u => u.Id == link.UserId, ct)
                : await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            if (user is null)
            {
                // Il nome visibile resta quello predefinito: non si importa il nome del profilo Google.
                var local = email[..email.IndexOf('@')];
                user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = now, DisplayName = local.Length > 60 ? local[..60] : local };
                db.Users.Add(user);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    user = await db.Users.FirstAsync(u => u.Email == email, ct);
                }
            }
            if (user.Status != UserStatus.Active) return Fail("sospeso");

            if (link is null)
            {
                db.ExternalLogins.Add(new ExternalLogin { Id = Guid.NewGuid(), UserId = user.Id, Provider = Provider, Subject = identity.Subject, CreatedAt = now });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); /* accesso parallelo: il legame esiste già */ }
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme));
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect(flow.Next);
        }).RequireRateLimiting("auth");
    }

    private static IResult Fail(string reason) => Results.Redirect($"/accedi?errore={reason}");

    private static string RedirectUri(AppOptions app) => $"{app.PublicBaseUrl.TrimEnd('/')}/api/auth/google/callback";

    /// <summary>Solo percorsi interni del sito: niente indirizzi assoluti né "//" (redirect verso altri siti).</summary>
    private static string SafeNext(string? next) =>
        next is { Length: > 0 } && next[0] == '/' && !next.StartsWith("//", StringComparison.Ordinal) && !next.Contains('\\') ? next : "/";

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static FlowState? ReadFlow(HttpContext http, IDataProtectionProvider protection)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var cookie) || string.IsNullOrEmpty(cookie)) return null;
        try
        {
            return JsonSerializer.Deserialize<FlowState>(protection.CreateProtector("runtivo.google.flow").Unprotect(cookie));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }
}
