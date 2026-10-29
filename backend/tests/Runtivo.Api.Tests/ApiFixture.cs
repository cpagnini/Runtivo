using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;

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
    public IServiceProvider Services => _factory!.Services;

    private readonly WebApplicationFactory<Program>? _factory;

    public ApiFixture() : this(null) { }

    /// <summary>Crea un'app separata con configurazione aggiuntiva (vedi il costruttore privato).</summary>
    public static ApiFixture WithSettings(IDictionary<string, string> extraSettings) => new(extraSettings);

    /// <summary>
    /// Con <paramref name="extraSettings"/> crea un'app separata (stesso database, orologio e posta propri) con
    /// configurazione aggiuntiva: serve ai test che devono cambiare opzioni come gli amministratori o i limiti.
    /// </summary>
    private ApiFixture(IDictionary<string, string>? extraSettings)
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
            b.UseSetting("App:NewAccountDays", "0");
            foreach (var (key, value) in extraSettings ?? new Dictionary<string, string>()) b.UseSetting(key, value);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IEmailSender>();
                s.AddSingleton<IEmailSender>(Mail);
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Time);
            });
        });
        if (extraSettings is null) ResetMatchingData(connectionString);
    }

    /// <summary>
    /// Gli annunci sopravvivono tra un'esecuzione e l'altra e farebbero comparire "vecchi" candidati nei match.
    /// Si svuotano all'avvio, ma solo se il database è chiaramente di test (il nome contiene "test").
    /// </summary>
    private void ResetMatchingData(string connectionString)
    {
        if (!connectionString.Contains("test", StringComparison.OrdinalIgnoreCase)) return;
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntivoDbContext>();
        db.UserBlocks.ExecuteDelete();
        db.RunIntents.ExecuteDelete();
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

    /// <summary>Come <see cref="LoginAsync"/>, ma con un nome visibile scelto (serve per pubblicare un annuncio).</summary>
    public async Task<(HttpClient Client, string Email, Guid UserId)> LoginNamedAsync(string? email = null)
    {
        var login = await LoginAsync(email);
        var patched = await login.Client.PatchAsJsonAsync("/api/me", new { displayName = "Runner " + Guid.NewGuid().ToString("N")[..6] });
        patched.EnsureSuccessStatusCode();
        return login;
    }

    public void Dispose() => _factory?.Dispose();
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
