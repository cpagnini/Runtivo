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
    public Guid? SeriesId { get; set; }
    /// <summary>Data locale dell'occorrenza: con SeriesId è unica, così rigenerare non crea duplicati.</summary>
    public DateOnly? OccurrenceLocalDate { get; set; }
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

public class EventSeries
{
    public Guid Id { get; set; }
    public Guid ClubId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string MeetingPointLabel { get; set; }
    public EventVisibility Visibility { get; set; }
    public DayOfWeek Weekday { get; set; }
    public TimeOnly LocalStartTime { get; set; }
    public string TimeZoneId { get; set; } = "Europe/Rome";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    /// <summary>Gruppi di passo da copiare nelle nuove occorrenze (JSON di GroupTemplate).</summary>
    public required string GroupsJson { get; set; }
    public bool Enabled { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public record GroupTemplate(string Name, int? DistanceMeters, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, int? Capacity);
