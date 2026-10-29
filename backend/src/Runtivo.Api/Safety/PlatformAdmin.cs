using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runtivo.Api.Data;

namespace Runtivo.Api.Safety;

public class PlatformOptions
{
    /// <summary>Indirizzi email degli amministratori della piattaforma. Vuoto di default: nessuno lo è.</summary>
    public List<string> AdminEmails { get; set; } = [];
}

/// <summary>
/// Il ruolo di amministratore della piattaforma si assegna solo da configurazione e si verifica a ogni richiesta
/// amministrativa: non esiste alcun endpoint con cui diventarlo, e toglierlo dalla configurazione ha effetto subito.
/// </summary>
public static class PlatformAdmin
{
    public static bool IsAdminEmail(PlatformOptions options, string email) =>
        options.AdminEmails.Any(e => string.Equals(e?.Trim(), email, StringComparison.OrdinalIgnoreCase));

    public static async Task<bool> IsAsync(RuntivoDbContext db, IOptions<PlatformOptions> options, Guid userId, CancellationToken ct)
    {
        if (options.Value.AdminEmails.Count == 0) return false;
        var email = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        return email is not null && IsAdminEmail(options.Value, email);
    }
}
