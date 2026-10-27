using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Auth;
using Runtivo.Api.Clubs;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ?? new AppOptions();

builder.Services.AddSingleton(TimeProvider.System);
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
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
app.MapClubEndpoints();

app.Run();

public partial class Program;
