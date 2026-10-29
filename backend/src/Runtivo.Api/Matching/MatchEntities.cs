namespace Runtivo.Api.Matching;

public enum RunType { Easy, Training, Social }
public enum DayBand { Morning, Midday, Evening }
public enum IntentStatus { Active, Paused }

/// <summary>Punto di ritrovo pubblico curato: unica fonte di posizione per annunci e uscite (niente GPS né indirizzi privati).</summary>
public class PublicPlace
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Annuncio di chi cerca compagni di corsa; uno solo per utente.</summary>
public class RunIntent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid HomePlaceId { get; set; }
    public int RadiusKm { get; set; }
    public int PaceMinSecPerKm { get; set; }
    public int PaceMaxSecPerKm { get; set; }
    public int DistanceMinMeters { get; set; }
    public int DistanceMaxMeters { get; set; }
    public RunType RunType { get; set; }
    /// <summary>Bit per fascia: vedi <see cref="Availability"/>.</summary>
    public int AvailabilityMask { get; set; }
    public bool AllowPairs { get; set; }
    public IntentStatus Status { get; set; } = IntentStatus.Active;
    public DateTimeOffset AdultConfirmedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UserBlock
{
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
