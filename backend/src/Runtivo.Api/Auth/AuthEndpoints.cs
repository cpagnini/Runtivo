using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Auth;

public record RequestLinkDto(string? Email);
public record VerifyDto(string? Token);
public record UpdateProfileDto(string? DisplayName, string? City, int? PreferredPaceSecPerKm);
public record UserDto(Guid Id, string Email, string DisplayName, string? City, int? PreferredPaceSecPerKm);

public static class AuthEndpoints
{
    private static readonly TimeSpan LoginTokenLifetime = TimeSpan.FromMinutes(15);
    private const int MaxLinksPerEmailPerWindow = 3;

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.DisplayName, u.City, u.PreferredPaceSecPerKm);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        // Risponde sempre 202, esista o no l'account (anti-enumerazione).
        auth.MapPost("/request-link", async (RequestLinkDto dto, RuntivoDbContext db, IEmailSender mail,
            TimeProvider time, IOptions<AppOptions> opts, CancellationToken ct) =>
        {
            var email = Emails.Normalize(dto.Email);
            if (email is null) return Http.Problem(400, "Indirizzo email non valido.");

            var now = time.GetUtcNow();
            var recent = await db.LoginTokens.CountAsync(t => t.Email == email && t.CreatedAt > now - LoginTokenLifetime, ct);
            if (recent < MaxLinksPerEmailPerWindow)
            {
                var (token, hash) = Tokens.Generate();
                db.LoginTokens.Add(new LoginToken
                {
                    Id = Guid.NewGuid(), Email = email, TokenHash = hash,
                    CreatedAt = now, ExpiresAt = now + LoginTokenLifetime,
                });
                await db.SaveChangesAsync(ct);
                var link = $"{opts.Value.PublicBaseUrl.TrimEnd('/')}/accedi/verifica?token={token}";
                await mail.SendLoginLinkAsync(email, link, ct);
            }
            return Results.Accepted();
        }).RequireRateLimiting("auth");

        // POST (non GET): gli anteprima-link dei client di posta non consumano il token.
        auth.MapPost("/verify", async (VerifyDto dto, RuntivoDbContext db, HttpContext http,
            TimeProvider time, CancellationToken ct) =>
        {
            const string invalid = "Link non valido o scaduto.";
            if (string.IsNullOrWhiteSpace(dto.Token)) return Http.Problem(400, invalid);

            var now = time.GetUtcNow();
            var hash = Tokens.Hash(dto.Token);
            var token = await db.LoginTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (token is null || token.ExpiresAt <= now) return Http.Problem(400, invalid);

            // Consumo atomico: un solo chiamante può vincere anche con richieste concorrenti.
            var claimed = await db.LoginTokens
                .Where(t => t.Id == token.Id && t.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
            if (claimed == 0) return Http.Problem(400, invalid);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == token.Email, ct);
            if (user is null)
            {
                var local = token.Email[..token.Email.IndexOf('@')];
                user = new User
                {
                    Id = Guid.NewGuid(), Email = token.Email, CreatedAt = now,
                    DisplayName = local.Length > 60 ? local[..60] : local,
                };
                db.Users.Add(user);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    db.Entry(user).State = EntityState.Detached;
                    user = await db.Users.FirstAsync(u => u.Email == token.Email, ct);
                }
            }
            if (user.Status != UserStatus.Active) return Http.Problem(403, "Account non attivo.");

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(ToDto(user));
        }).RequireRateLimiting("auth");

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet("", async (ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([p.UserId()], ct);
            return user is null ? Http.Problem(401, "Sessione non valida.") : Results.Ok(ToDto(user));
        });

        me.MapPatch("", async (UpdateProfileDto dto, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([p.UserId()], ct);
            if (user is null) return Http.Problem(401, "Sessione non valida.");

            if (dto.DisplayName is not null)
            {
                var name = dto.DisplayName.Trim();
                if (name.Length is < 1 or > 60) return Http.Problem(400, "Il nome deve avere da 1 a 60 caratteri.");
                user.DisplayName = name;
            }
            if (dto.City is not null)
            {
                var city = dto.City.Trim();
                if (city.Length > 80) return Http.Problem(400, "La località può avere al massimo 80 caratteri.");
                user.City = city.Length == 0 ? null : city;
            }
            if (dto.PreferredPaceSecPerKm is { } pace)
            {
                if (pace is < 120 or > 1200) return Http.Problem(400, "Passo non valido.");
                user.PreferredPaceSecPerKm = pace;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(user));
        });
    }
}
