using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Common;

public static class Tokens
{
    /// <summary>Token casuale URL-safe; nel DB si salva solo l'hash.</summary>
    public static (string Token, string Hash) Generate()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static partial class Slugs
{
    public static string From(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var slug = NonAlnum().Replace(sb.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug.Length == 0 ? "club" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}

public static class Emails
{
    /// <summary>Restituisce l'email normalizzata (minuscola) o null se non valida.</summary>
    public static string? Normalize(string? input)
    {
        var value = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || value.Length > 254) return null;
        return MailAddress.TryCreate(value, out var addr) && addr.Address == value && value.Contains('.') ? value : null;
    }
}

public static class Http
{
    public static Guid UserId(this ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static IResult Problem(int status, string detail) =>
        Results.Problem(statusCode: status, detail: detail);

    public static void Audit(this RuntivoDbContext db, TimeProvider time, Guid? actor, string action, string targetType, object targetId) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(), ActorUserId = actor, Action = action,
            TargetType = targetType, TargetId = targetId.ToString()!, Timestamp = time.GetUtcNow(),
        });
}
