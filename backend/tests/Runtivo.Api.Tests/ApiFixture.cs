using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Runtivo.Api.Common;

namespace Runtivo.Api.Tests;

public class CapturingEmailSender : IEmailSender
{
    public record Sent(string To, string Link);

    private readonly List<Sent> _sent = [];

    public IReadOnlyList<Sent> To(string email)
    {
        lock (_sent) return _sent.Where(s => s.To == email).ToList();
    }

    public Task SendLoginLinkAsync(string to, string link, CancellationToken ct) => Add(to, link);
    public Task SendClubInvitationAsync(string to, string clubName, string link, CancellationToken ct) => Add(to, link);

    private Task Add(string to, string link)
    {
        lock (_sent) _sent.Add(new Sent(to, link));
        return Task.CompletedTask;
    }
}

/// <summary>Un'unica app di test su PostgreSQL reale (RUNTIVO_TEST_DB), condivisa dai test di integrazione.</summary>
public class ApiFixture : IDisposable
{
    public const string TestDbVariable = "RUNTIVO_TEST_DB";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public CapturingEmailSender Mail { get; } = new();
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
    public bool Available { get; }

    private readonly WebApplicationFactory<Program>? _factory;

    public ApiFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        if (string.IsNullOrEmpty(connectionString)) return;
        Available = true;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", connectionString);
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("App:RequireSecureCookie", "false");
            b.UseSetting("App:AuthRequestsPerMinute", "100000");
            b.UseSetting("App:ApiRequestsPerMinute", "1000000");
            b.UseSetting("Series:BackgroundGeneration", "false");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IEmailSender>();
                s.AddSingleton<IEmailSender>(Mail);
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Time);
            });
        });
    }

    public HttpClient NewClient(bool csrfHeader = true)
    {
        var client = _factory!.CreateClient();
        if (csrfHeader) client.DefaultRequestHeaders.Add("X-Requested-With", "runtivo");
        return client;
    }

    public static string NewEmail() => $"u{Guid.NewGuid():N}@example.com";

    public static string TokenFromLink(string link) => link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];

    /// <summary>Esegue il flusso completo link → verifica e restituisce un client con la sessione attiva.</summary>
    public async Task<(HttpClient Client, string Email, Guid UserId)> LoginAsync(string? email = null)
    {
        email ??= NewEmail();
        var client = NewClient();
        Assert.Equal(System.Net.HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/request-link", new { email })).StatusCode);
        var token = TokenFromLink(Mail.To(email)[^1].Link);
        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { token });
        verify.EnsureSuccessStatusCode();
        var user = await verify.Content.ReadFromJsonAsync<JsonElement>();
        return (client, email, user.GetProperty("id").GetGuid());
    }

    public void Dispose() => _factory?.Dispose();
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
