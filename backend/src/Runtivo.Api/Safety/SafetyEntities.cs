namespace Runtivo.Api.Safety;

public enum ReportReason { Harassment, FakeProfile, Unsafe, Spam, Other }
public enum ReportStatus { Open, Dismissed, ActionTaken }

/// <summary>
/// Segnalazione di una persona. Niente chiavi esterne sugli utenti: la segnalazione resta utile alla moderazione
/// anche se una delle due persone cancella l'account (il segnalante viene reso anonimo, la persona segnalata resta identificata).
/// </summary>
public class Report
{
    public Guid Id { get; set; }
    public Guid? ReporterUserId { get; set; }
    public Guid ReportedUserId { get; set; }
    public Guid? RunId { get; set; }
    public ReportReason Reason { get; set; }
    public string? Details { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
}
