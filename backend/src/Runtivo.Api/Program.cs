using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Data;
using Runtivo.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<RuntivoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Liveness: il processo risponde. Readiness: anche il database è raggiungibile.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.Run();

public partial class Program;
