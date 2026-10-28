using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Data;

public class RuntivoDbContext(DbContextOptions<RuntivoDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<ClubMembership> Memberships => Set<ClubMembership>();
    public DbSet<ClubInvitation> Invitations => Set<ClubInvitation>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<RunEvent> Events => Set<RunEvent>();
    public DbSet<PaceGroup> PaceGroups => Set<PaceGroup>();
    public DbSet<EventRsvp> Rsvps => Set<EventRsvp>();
    public DbSet<EventSeries> Series => Set<EventSeries>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(60);
            e.Property(x => x.City).HasMaxLength(80);
            e.HasIndex(x => x.Email).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Users_Pace",
                "\"PreferredPaceSecPerKm\" IS NULL OR \"PreferredPaceSecPerKm\" BETWEEN 120 AND 1200"));
        });

        b.Entity<LoginToken>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.Email, x.CreatedAt });
        });

        b.Entity<Club>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Slug).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.City).HasMaxLength(80);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<ClubMembership>(e =>
        {
            e.HasKey(x => new { x.ClubId, x.UserId });
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<ClubInvitation>(e =>
        {
            e.Property(x => x.InvitedEmail).HasMaxLength(254);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RunEvent>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.TimeZoneId).HasMaxLength(50);
            e.Property(x => x.MeetingPointLabel).HasMaxLength(120);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Groups).WithOne().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClubId, x.StartsAtUtc });
            e.HasOne<EventSeries>().WithMany().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.SeriesId, x.OccurrenceLocalDate }).IsUnique();
        });

        b.Entity<EventSeries>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.MeetingPointLabel).HasMaxLength(120);
            e.Property(x => x.TimeZoneId).HasMaxLength(50);
            e.HasOne<Club>().WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClubId, x.Enabled });
        });

        b.Entity<PaceGroup>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(40);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PaceGroups_Pace",
                    "(\"PaceMinSecPerKm\" IS NULL OR \"PaceMinSecPerKm\" > 0) AND (\"PaceMaxSecPerKm\" IS NULL OR \"PaceMaxSecPerKm\" > 0) " +
                    "AND (\"PaceMinSecPerKm\" IS NULL OR \"PaceMaxSecPerKm\" IS NULL OR \"PaceMinSecPerKm\" <= \"PaceMaxSecPerKm\")");
                t.HasCheckConstraint("CK_PaceGroups_Capacity", "\"Capacity\" IS NULL OR \"Capacity\" > 0");
                t.HasCheckConstraint("CK_PaceGroups_Distance", "\"DistanceMeters\" IS NULL OR \"DistanceMeters\" > 0");
            });
        });

        b.Entity<EventRsvp>(e =>
        {
            e.HasOne<RunEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // Un solo RSVP per utente ed evento; il gruppo dell'RSVP deve esistere.
            e.HasOne<PaceGroup>().WithMany().HasForeignKey(x => x.PaceGroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
            e.HasIndex(x => x.PaceGroupId);
            e.ToTable(t => t.HasCheckConstraint("CK_Rsvps_GroupWhenGoing",
                "(\"Status\" = 'Going' AND \"PaceGroupId\" IS NOT NULL) OR (\"Status\" <> 'Going' AND \"PaceGroupId\" IS NULL)"));
        });

        b.Entity<NotificationPreference>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne<User>().WithOne().HasForeignKey<NotificationPreference>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.TargetType).HasMaxLength(40);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.HasIndex(x => x.Timestamp);
        });
    }
}
