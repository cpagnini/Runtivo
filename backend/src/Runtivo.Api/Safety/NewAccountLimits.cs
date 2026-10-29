using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Data;

namespace Runtivo.Api.Safety;

/// <summary>
/// Limiti più stretti per gli account appena registrati, per rendere poco utile creare profili usa e getta
/// per contattare molte persone. Passato il periodo (<c>App:NewAccountDays</c>, 0 = disattivato) valgono i limiti normali.
/// </summary>
public static class NewAccountLimits
{
    public const int InterestsPerDay = 5;
    public const int OpenProposals = 1;

    public static bool IsNew(DateTimeOffset createdAt, DateTimeOffset now, int newAccountDays) =>
        newAccountDays > 0 && now - createdAt < TimeSpan.FromDays(newAccountDays);

    public static async Task<bool> IsNewAsync(RuntivoDbContext db, Guid userId, DateTimeOffset now, int newAccountDays, CancellationToken ct)
    {
        if (newAccountDays <= 0) return false;
        var createdAt = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.CreatedAt).FirstAsync(ct);
        return IsNew(createdAt, now, newAccountDays);
    }

    /// <summary>Lock di transazione per persona e tipo di azione: conteggio e inserimento diventano atomici anche con richieste parallele.</summary>
    public static Task LockAsync(RuntivoDbContext db, string action, Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({action + ':' + userId}, 0))", ct);
}
