using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Auth;
using Runtivo.Api.Clubs;
using Runtivo.Api.Common;
using Runtivo.Api.Events;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;
using Runtivo.Api.Health;
using Runtivo.Api.Matching;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ?? new AppOptions();

builder.Services.AddSingleton(TimeProvider.System);
if (appOptions.TrustForwardedHeaders)
{
    // Il backend è raggiungibile solo dal frontend (proxy): l'IP del client arriva in X-Forwarded-For.
    // Senza questo, i limiti per IP vedrebbero tutti gli utenti come un unico indirizzo.
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}
builder.Services.AddHostedService<SeriesGenerationService>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddDbContext<RuntivoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

if (builder.Environment.IsDevelopment()) builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
else builder.Services.AddSingleton<IEmailSender, NotConfiguredEmailSender>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "runtivo.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = appOptions.RequireSecureCookie ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // API JSON: niente redirect alla pagina di login.
        // La sessione vale solo finché l'account esiste ed è attivo: cancellazione e sospensione hanno effetto subito.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var db = ctx.HttpContext.RequestServices.GetRequiredService<RuntivoDbContext>();
            if (!Guid.TryParse(id, out var userId) || !await db.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Tetto generale per IP su tutte le API (salvo gli health check).
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = appOptions.ApiRequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other"));
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = appOptions.AuthRequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<RuntivoDbContext>().Database.Migrate();
}

if (appOptions.TrustForwardedHeaders) app.UseForwardedHeaders();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

app.UseExceptionHandler();
app.UseStatusCodePages();

// Difesa CSRF: oltre a SameSite=Lax, le richieste che modificano dati devono portare un header
// personalizzato, che un form o un'immagine di un altro sito non può inviare.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) &&
        !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method) &&
        ctx.Request.Headers["X-Requested-With"] != "runtivo")
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { status = 403, detail = "Richiesta non consentita." });
        return;
    }
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: il processo risponde. Readiness: anche il database è raggiungibile.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapClubEndpoints();
app.MapEventEndpoints();
app.MapSeriesEndpoints();
app.MapIntentEndpoints();
app.MapMatchEndpoints();
app.MapInterestEndpoints();
app.MapRunEndpoints();

app.Run();

public partial class Program;
