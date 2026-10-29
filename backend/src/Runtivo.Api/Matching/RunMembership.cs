using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Data;

namespace Runtivo.Api.Matching;

public static class RunMembership
{
    /// <summary>
    /// Fa uscire una persona dalle sue uscite future non annullate (invitato → rifiutata, confermato → uscito) e
    /// riapplica le regole di stato: l'uscita prosegue se restano abbastanza persone, altrimenti si annulla.
    /// Con <paramref name="onlyRunsWithUser"/> riguarda solo le uscite in cui c'è anche quella persona (caso del blocco).
    /// Se esiste già una transazione la riusa (cancellazione account), altrimenti ne apre una.
    /// </summary>
    public static async Task LeaveFutureRunsAsync(RuntivoDbContext db, Guid userId, Guid? onlyRunsWithUser, DateTimeOffset now, CancellationToken ct)
    {
        var runIds = await (from part in db.RunParticipants
                            join run in db.SpontaneousRuns on part.RunId equals run.Id
                            where part.UserId == userId && (part.Status == ParticipantStatus.Pending || part.Status == ParticipantStatus.Confirmed)
                                  && run.Status != RunStatus.Cancelled && run.StartsAtUtc > now
                            select run.Id).ToListAsync(ct);
        if (onlyRunsWithUser is { } other)
        {
            var shared = await db.RunParticipants
                .Where(x => runIds.Contains(x.RunId) && x.UserId == other && (x.Status == ParticipantStatus.Pending || x.Status == ParticipantStatus.Confirmed))
                .Select(x => x.RunId).ToListAsync(ct);
            runIds = shared;
        }
        if (runIds.Count == 0) return;

        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

        foreach (var runId in runIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SpontaneousRuns\" WHERE \"Id\" = {runId} FOR UPDATE", ct);
            var run = await db.SpontaneousRuns.FirstAsync(r => r.Id == runId, ct);
            var participants = await db.RunParticipants.Where(x => x.RunId == runId).ToListAsync(ct);
            var me = participants.First(x => x.UserId == userId);
            if (me.Status is ParticipantStatus.Declined or ParticipantStatus.Left) continue;

            me.Status = me.Status == ParticipantStatus.Pending ? ParticipantStatus.Declined : ParticipantStatus.Left;
            me.UpdatedAt = now;
            var outcome = await RunEndpoints.ResolveAsync(db, participants, ct);
            run.Status = outcome.Status;
            run.CancelReason = outcome.CancelReason;
            run.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }
}
