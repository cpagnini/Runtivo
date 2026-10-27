namespace Runtivo.Api.Common;

public class AppOptions
{
    public string PublicBaseUrl { get; set; } = "http://localhost:3000";
    public bool RequireSecureCookie { get; set; } = true;
    public int AuthRequestsPerMinute { get; set; } = 20;
}

public interface IEmailSender
{
    Task SendLoginLinkAsync(string to, string link, CancellationToken ct);
    Task SendClubInvitationAsync(string to, string clubName, string link, CancellationToken ct);
}

/// <summary>Solo sviluppo: scrive il link nel log al posto di inviare un'email.</summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> log) : IEmailSender
{
    public Task SendLoginLinkAsync(string to, string link, CancellationToken ct)
    {
        log.LogInformation("[DEV] Link di accesso per {To}: {Link}", to, link);
        return Task.CompletedTask;
    }

    public Task SendClubInvitationAsync(string to, string clubName, string link, CancellationToken ct)
    {
        log.LogInformation("[DEV] Invito a {Club} per {To}: {Link}", clubName, to, link);
        return Task.CompletedTask;
    }
}

/// <summary>Produzione finché non è scelto un provider email: fallisce in modo esplicito.</summary>
public class NotConfiguredEmailSender : IEmailSender
{
    public Task SendLoginLinkAsync(string to, string link, CancellationToken ct) =>
        throw new InvalidOperationException("Provider email non configurato.");

    public Task SendClubInvitationAsync(string to, string clubName, string link, CancellationToken ct) =>
        throw new InvalidOperationException("Provider email non configurato.");
}
