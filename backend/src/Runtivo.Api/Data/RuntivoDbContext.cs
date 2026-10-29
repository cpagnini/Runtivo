using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Domain;
using Runtivo.Api.Matching;
using Runtivo.Api.Safety;

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
    public DbSet<PublicPlace> PublicPlaces => Set<PublicPlace>();
    public DbSet<RunIntent> RunIntents => Set<RunIntent>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Interest> Interests => Set<Interest>();
    public DbSet<SpontaneousRun> SpontaneousRuns => Set<SpontaneousRun>();
    public DbSet<RunParticipant> RunParticipants => Set<RunParticipant>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<SafetyShare> SafetyShares => Set<SafetyShare>();

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

        b.Entity<PublicPlace>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(60);
            e.HasIndex(x => new { x.City, x.Name }).IsUnique();
            // Coordinate approssimate dei punti di ritrovo: da verificare prima del lancio pubblico.
            e.HasData(
                Place("5f1c2a10-0000-4000-8000-000000000001", "Piazza del Comune", "Prato", 43.8790, 11.0963),
                Place("5f1c2a10-0000-4000-8000-000000000002", "Piazza Mercatale", "Prato", 43.8810, 11.0990),
                Place("5f1c2a10-0000-4000-8000-000000000003", "Parco delle Cascine", "Firenze", 43.7795, 11.2160),
                Place("5f1c2a10-0000-4000-8000-000000000004", "Piazzale Michelangelo", "Firenze", 43.7629, 11.2650),
                Place("5f1c2a10-0000-4000-8000-000000000005", "Piazza Santa Croce", "Firenze", 43.7687, 11.2626),
                Place("5f1c2a10-0000-4000-8000-000000000006", "Fortezza da Basso", "Firenze", 43.7803, 11.2432));
        });

        b.Entity<RunIntent>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PublicPlace>().WithMany().HasForeignKey(x => x.HomePlaceId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_RunIntents_Pace", "\"PaceMinSecPerKm\" BETWEEN 120 AND 1200 AND \"PaceMaxSecPerKm\" BETWEEN 120 AND 1200 AND \"PaceMinSecPerKm\" <= \"PaceMaxSecPerKm\"");
                t.HasCheckConstraint("CK_RunIntents_Distance", "\"DistanceMinMeters\" >= 1000 AND \"DistanceMinMeters\" <= \"DistanceMaxMeters\" AND \"DistanceMaxMeters\" <= 60000");
                t.HasCheckConstraint("CK_RunIntents_Radius", "\"RadiusKm\" BETWEEN 5 AND 30");
                t.HasCheckConstraint("CK_RunIntents_Availability", "\"AvailabilityMask\" > 0");
            });
        });

        b.Entity<UserBlock>(e =>
        {
            e.HasKey(x => new { x.BlockerId, x.BlockedId });
            e.HasIndex(x => x.BlockedId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_UserBlocks_NotSelf", "\"BlockerId\" <> \"BlockedId\""));
        });

        b.Entity<Interest>(e =>
        {
            e.HasKey(x => new { x.FromUserId, x.ToUserId });
            e.HasIndex(x => x.ToUserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_Interests_NotSelf", "\"FromUserId\" <> \"ToUserId\""));
        });

        b.Entity<SpontaneousRun>(e =>
        {
            e.Property(x => x.TimeZoneId).HasMaxLength(50);
            e.Property(x => x.CancelReason).HasMaxLength(200);
            e.HasIndex(x => new { x.Status, x.StartsAtUtc });
            e.HasIndex(x => x.CreatedByUserId);
            e.HasOne<PublicPlace>().WithMany().HasForeignKey(x => x.PlaceId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_SpontaneousRuns_Pace",
                "(\"PaceMinSecPerKm\" IS NULL OR \"PaceMaxSecPerKm\" IS NULL OR \"PaceMinSecPerKm\" <= \"PaceMaxSecPerKm\")"));
        });

        b.Entity<RunParticipant>(e =>
        {
            e.HasKey(x => new { x.RunId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne<SpontaneousRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Report>(e =>
        {
            e.Property(x => x.Details).HasMaxLength(500);
            e.Property(x => x.ResolutionNote).HasMaxLength(500);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.ReportedUserId);
            e.HasIndex(x => x.ReporterUserId);
        });

        b.Entity<SafetyShare>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.RunId, x.UserId });
            e.HasOne<SpontaneousRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.TargetType).HasMaxLength(40);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.HasIndex(x => x.Timestamp);
        });
    }

    private static PublicPlace Place(string id, string name, string city, double lat, double lon) =>
        new() { Id = Guid.Parse(id), Name = name, City = city, Latitude = lat, Longitude = lon };
}
