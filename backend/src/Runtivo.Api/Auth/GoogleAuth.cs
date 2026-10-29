using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Runtivo.Api.Auth;

public class GoogleOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>L'accesso con Google compare solo se le credenziali sono configurate.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Ciò che serve dell'identità restituita da Google: nome, foto e altri dati del profilo non si leggono mai.</summary>
public record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Nonce, string Issuer, string Audience, DateTimeOffset ExpiresAt);

public interface IGoogleClient
{
    /// <summary>Scambia il codice con l'identità; null se Google rifiuta lo scambio o la risposta non è leggibile.</summary>
    Task<GoogleIdentity?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct);
}

/// <summary>
/// Scambio del codice con Google sul canale diretto server-a-server (TLS): per le regole di OpenID Connect l'id_token
/// ricevuto così non richiede la verifica della firma, ma emittente, destinatario, scadenza e nonce si controllano comunque
/// nell'endpoint.
/// </summary>
public class GoogleClient(HttpClient http, IOptions<GoogleOptions> options) : IGoogleClient
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public async Task<GoogleIdentity?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = options.Value.ClientId!,
            ["client_secret"] = options.Value.ClientSecret!,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier,
        });

        try
        {
            using var response = await http.PostAsync(TokenEndpoint, form, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!body.RootElement.TryGetProperty("id_token", out var idToken) || idToken.GetString() is not { } jwt) return null;
            return ParseIdToken(jwt);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or FormatException)
        {
            return null;
        }
    }

    public static GoogleIdentity? ParseIdToken(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        using var payload = JsonDocument.Parse(Convert.FromBase64String(PadBase64(parts[1].Replace('-', '+').Replace('_', '/'))));
        var root = payload.RootElement;

        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        // Google indica email_verified come booleano (a volte come stringa).
        var verified = root.TryGetProperty("email_verified", out var ev) && (ev.ValueKind == JsonValueKind.True || (ev.ValueKind == JsonValueKind.String && ev.GetString() == "true"));
        if (Str("sub") is not { } sub || Str("email") is not { } email || Str("iss") is not { } iss || Str("aud") is not { } aud) return null;
        if (!root.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expSeconds)) return null;

        return new GoogleIdentity(sub, email, verified, Str("nonce"), iss, aud, DateTimeOffset.FromUnixTimeSeconds(expSeconds));
    }

    private static string PadBase64(string s) => s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
}
