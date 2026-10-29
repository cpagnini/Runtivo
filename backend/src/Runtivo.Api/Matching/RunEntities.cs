namespace Runtivo.Api.Matching;

public enum RunStatus { Proposed, Confirmed, Cancelled }
public enum ParticipantStatus { Pending, Confirmed, Declined, Left }

/// <summary>«Mi interessa» da una persona a un'altra: privato finché non è reciproco.</summary>
public class Interest
{
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Uscita spontanea in un luogo pubblico, da 2 persone in su.</summary>
public class SpontaneousRun
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid PlaceId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public string TimeZoneId { get; set; } = "Europe/Rome";
    public int? DistanceMeters { get; set; }
    public int? PaceMinSecPerKm { get; set; }
    public int? PaceMaxSecPerKm { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Proposed;
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class RunParticipant
{
    public Guid RunId { get; set; }
    public Guid UserId { get; set; }
    public ParticipantStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
