using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Runtivo.Api.Tests;

public class HealthTests
{
    private const string TestDbVariable = "RUNTIVO_TEST_DB";

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Default", connectionString));

    [Fact]
    public async Task Liveness_responds_without_database()
    {
        await using var factory = CreateFactory("Host=127.0.0.1;Port=1;Timeout=1");
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_unavailable_when_database_is_unreachable()
    {
        await using var factory = CreateFactory("Host=127.0.0.1;Port=1;Timeout=1");
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [SkippableFact]
    public async Task Readiness_is_ok_with_real_database()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        Skip.If(string.IsNullOrEmpty(connectionString), $"{TestDbVariable} non impostata");

        await using var factory = CreateFactory(connectionString!);
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
