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

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.TargetType).HasMaxLength(40);
            e.Property(x => x.TargetId).HasMaxLength(64);
            e.HasIndex(x => x.Timestamp);
        });
    }
}
