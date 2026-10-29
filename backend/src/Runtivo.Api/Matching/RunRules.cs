namespace Runtivo.Api.Matching;

public record ParticipantState(Guid UserId, ParticipantStatus Status, bool AllowsPairs);

public record RunOutcome(RunStatus Status, string? CancelReason);

/// <summary>
/// Stato di un'uscita a partire dai suoi partecipanti. Funzione pura, usata da conferma, rifiuto, uscita dal gruppo,
/// blocco e cancellazione account, così tutte le strade arrivano alla stessa conclusione.
/// </summary>
public static class RunRules
{
    public static RunOutcome Resolve(IEnumerable<ParticipantState> participants, bool alreadyCancelled = false)
    {
        if (alreadyCancelled) return new RunOutcome(RunStatus.Cancelled, null);

        var active = participants.Where(p => p.Status is ParticipantStatus.Pending or ParticipantStatus.Confirmed).ToList();
        if (active.Count < 2) return new RunOutcome(RunStatus.Cancelled, "Non ci sono abbastanza partecipanti.");

        // Un gruppo che si riduce a due persone resta valido solo se entrambe accettano di correre in coppia.
        if (active.Count == 2 && active.Any(p => !p.AllowsPairs))
            return new RunOutcome(RunStatus.Cancelled, "Non è più un gruppo di almeno 3 persone e qualcuno preferiva non correre in coppia.");

        return active.All(p => p.Status == ParticipantStatus.Confirmed)
            ? new RunOutcome(RunStatus.Confirmed, null)
            : new RunOutcome(RunStatus.Proposed, null);
    }
}
