namespace Runtivo.Api.Domain;

public enum UserStatus { Active, Suspended }
public enum ClubStatus { Active, Archived }
public enum ClubRole { Member, Leader, Admin }
public enum MembershipStatus { Pending, Active }

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? City { get; set; }
    public int? PreferredPaceSecPerKm { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

public class LoginToken
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public class Club
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public string? City { get; set; }
    public ClubStatus Status { get; set; } = ClubStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ClubMembership
{
    public Guid ClubId { get; set; }
    public Guid UserId { get; set; }
    public ClubRole Role { get; set; }
    public MembershipStatus Status { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public User? User { get; set; }
}

public class ClubInvitation
{
    public Guid Id { get; set; }
    public Guid ClubId { get; set; }
    public string? InvitedEmail { get; set; }
    public required string TokenHash { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public required string TargetId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class NotificationPreference
{
    public Guid UserId { get; set; }
    public bool EventChangesEmail { get; set; } = true;
    public bool RemindersEmail { get; set; }
    public bool DiscoveryEmails { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
