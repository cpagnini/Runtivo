namespace Runtivo.Api.Domain;

public enum EventVisibility { Public, ClubOnly }
public enum EventStatus { Scheduled, Cancelled }
public enum RsvpStatus { Going, NotGoing }

public class RunEvent
{
    public Guid Id { get; set; }
    public Guid ClubId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public string TimeZoneId { get; set; } = "Europe/Rome";
    public required string MeetingPointLabel { get; set; }
    public EventVisibility Visibility { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Scheduled;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<PaceGroup> Groups { get; set; } = [];
}

public class PaceGroup
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public int? DistanceMeters { get; set; }
    public int? PaceMinSecPerKm { get; set; }
    public int? PaceMaxSecPerKm { get; set; }
    public Guid? LeaderUserId { get; set; }
    public int? Capacity { get; set; }
    public int SortOrder { get; set; }
}

public class EventRsvp
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public Guid? PaceGroupId { get; set; }
    public RsvpStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
