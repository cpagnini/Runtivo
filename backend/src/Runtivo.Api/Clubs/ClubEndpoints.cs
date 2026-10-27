using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Clubs;

public record CreateClubDto(string? Name, string? Description, string? City);
public record JoinClubDto(string? InviteToken);
public record CreateInvitationDto(string? Email);
public record ChangeRoleDto(ClubRole? Role);
public record ClubDto(Guid Id, string Name, string Slug, string? Description, string? City,
    ClubRole? MyRole, MembershipStatus? MyStatus);
public record MemberDto(Guid UserId, string DisplayName, string Email, ClubRole Role, MembershipStatus Status, DateTimeOffset JoinedAt);
public record MembershipDto(Guid ClubId, Guid UserId, ClubRole Role, MembershipStatus Status);

public static class ClubEndpoints
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public static void MapClubEndpoints(this WebApplication app)
    {
        var clubs = app.MapGroup("/api/clubs");

        clubs.MapPost("", async (CreateClubDto dto, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var name = dto.Name?.Trim();
            if (name is null || name.Length is < 2 or > 80) return Http.Problem(400, "Il nome del club deve avere da 2 a 80 caratteri.");
            var description = dto.Description?.Trim();
            if (description?.Length > 500) return Http.Problem(400, "La descrizione può avere al massimo 500 caratteri.");
            var city = dto.City?.Trim();
            if (city?.Length > 80) return Http.Problem(400, "La località può avere al massimo 80 caratteri.");

            var userId = p.UserId();
            var now = time.GetUtcNow();
            var baseSlug = Slugs.From(name);
            var slug = baseSlug;
            if (await db.Clubs.AnyAsync(c => c.Slug == slug, ct))
                slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..4]}";

            var club = new Club
            {
                Id = Guid.NewGuid(), Name = name, Slug = slug, CreatedAt = now,
                Description = string.IsNullOrEmpty(description) ? null : description,
                City = string.IsNullOrEmpty(city) ? null : city,
            };
            db.Clubs.Add(club);
            db.Memberships.Add(new ClubMembership
            {
                ClubId = club.Id, UserId = userId, Role = ClubRole.Admin,
                Status = MembershipStatus.Active, JoinedAt = now,
            });
            db.Audit(time, userId, "club.create", "Club", club.Id);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Http.Problem(409, "Esiste già un club con questo nome, riprova."); }

            return Results.Created($"/api/clubs/{club.Id}",
                new ClubDto(club.Id, club.Name, club.Slug, club.Description, club.City, ClubRole.Admin, MembershipStatus.Active));
        }).RequireAuthorization();

        clubs.MapGet("/mine", async (ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var rows = await (from m in db.Memberships
                              join c in db.Clubs on m.ClubId equals c.Id
                              where m.UserId == userId && c.Status == ClubStatus.Active
                              orderby c.Name
                              select new ClubDto(c.Id, c.Name, c.Slug, c.Description, c.City, m.Role, m.Status)).ToListAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization();

        // Le informazioni di base del club sono pubbliche; ruolo e stato solo per chi è loggato.
        clubs.MapGet("/{clubId:guid}", async (Guid clubId, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId && c.Status == ClubStatus.Active, ct);
            if (club is null) return Http.Problem(404, "Club non trovato.");

            ClubMembership? mine = null;
            if (p.Identity?.IsAuthenticated == true)
            {
                var userId = p.UserId();
                mine = await db.Memberships.FirstOrDefaultAsync(m => m.ClubId == clubId && m.UserId == userId, ct);
            }
            return Results.Ok(new ClubDto(club.Id, club.Name, club.Slug, club.Description, club.City, mine?.Role, mine?.Status));
        });

        // Iscrizione: con invito diventa subito attiva, altrimenti resta in attesa di approvazione.
        clubs.MapPost("/{clubId:guid}/memberships", async (Guid clubId, JoinClubDto dto, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var now = time.GetUtcNow();
            if (!await db.Clubs.AnyAsync(c => c.Id == clubId && c.Status == ClubStatus.Active, ct))
                return Http.Problem(404, "Club non trovato.");

            var existing = await db.Memberships.FirstOrDefaultAsync(m => m.ClubId == clubId && m.UserId == userId, ct);
            if (existing is not null && existing.Status == MembershipStatus.Active)
                return Results.Ok(new MembershipDto(clubId, userId, existing.Role, existing.Status));

            var status = MembershipStatus.Pending;
            if (!string.IsNullOrWhiteSpace(dto.InviteToken))
            {
                var hash = Tokens.Hash(dto.InviteToken);
                var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash && i.ClubId == clubId, ct);
                var userEmail = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstAsync(ct);
                if (invitation is null || invitation.ExpiresAt <= now ||
                    (invitation.InvitedEmail is not null && invitation.InvitedEmail != userEmail))
                    return Http.Problem(400, "Invito non valido o scaduto.");

                var claimed = await db.Invitations
                    .Where(i => i.Id == invitation.Id && i.UsedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAt, now), ct);
                if (claimed == 0) return Http.Problem(400, "Invito non valido o scaduto.");
                status = MembershipStatus.Active;
            }
            else if (existing is not null)
            {
                return Results.Ok(new MembershipDto(clubId, userId, existing.Role, existing.Status));
            }

            if (existing is null)
            {
                db.Memberships.Add(new ClubMembership
                {
                    ClubId = clubId, UserId = userId, Role = ClubRole.Member, Status = status, JoinedAt = now,
                });
            }
            else
            {
                existing.Status = status;
                existing.JoinedAt = now;
            }
            db.Audit(time, userId, status == MembershipStatus.Active ? "membership.join" : "membership.request", "Club", clubId);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Http.Problem(409, "Richiesta già presente."); }
            return Results.Ok(new MembershipDto(clubId, userId, ClubRole.Member, status));
        }).RequireAuthorization();

        clubs.MapGet("/{clubId:guid}/members", async (Guid clubId, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            if (await EnsureAdminAsync(db, clubId, p.UserId(), ct) is { } denied) return denied;
            var members = await db.Memberships.Where(m => m.ClubId == clubId)
                .OrderBy(m => m.Status).ThenBy(m => m.User!.DisplayName)
                .Select(m => new MemberDto(m.UserId, m.User!.DisplayName, m.User.Email, m.Role, m.Status, m.JoinedAt))
                .ToListAsync(ct);
            return Results.Ok(members);
        }).RequireAuthorization();

        clubs.MapPost("/{clubId:guid}/invitations", async (Guid clubId, CreateInvitationDto dto, ClaimsPrincipal p,
            RuntivoDbContext db, IEmailSender mail, TimeProvider time, IOptions<AppOptions> opts, CancellationToken ct) =>
        {
            var userId = p.UserId();
            if (await EnsureAdminAsync(db, clubId, userId, ct) is { } denied) return denied;

            string? email = null;
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                email = Emails.Normalize(dto.Email);
                if (email is null) return Http.Problem(400, "Indirizzo email non valido.");
            }

            var now = time.GetUtcNow();
            var (token, hash) = Tokens.Generate();
            var invitation = new ClubInvitation
            {
                Id = Guid.NewGuid(), ClubId = clubId, InvitedEmail = email, TokenHash = hash,
                CreatedByUserId = userId, CreatedAt = now, ExpiresAt = now + InvitationLifetime,
            };
            db.Invitations.Add(invitation);
            db.Audit(time, userId, "invitation.create", "Club", clubId);
            await db.SaveChangesAsync(ct);

            var link = $"{opts.Value.PublicBaseUrl.TrimEnd('/')}/club/invito?club={clubId}&token={token}";
            if (email is not null)
            {
                var clubName = await db.Clubs.Where(c => c.Id == clubId).Select(c => c.Name).FirstAsync(ct);
                await mail.SendClubInvitationAsync(email, clubName, link, ct);
            }
            // Il token in chiaro viene mostrato solo qui: nel DB resta solo l'hash.
            return Results.Created($"/api/clubs/{clubId}/invitations/{invitation.Id}",
                new { invitation.Id, Token = token, Link = link, invitation.ExpiresAt });
        }).RequireAuthorization();

        clubs.MapPost("/{clubId:guid}/memberships/{userId:guid}/approve", async (Guid clubId, Guid userId, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var actor = p.UserId();
            if (await EnsureAdminAsync(db, clubId, actor, ct) is { } denied) return denied;
            var m = await db.Memberships.FirstOrDefaultAsync(x => x.ClubId == clubId && x.UserId == userId, ct);
            if (m is null) return Http.Problem(404, "Richiesta non trovata.");
            if (m.Status == MembershipStatus.Pending)
            {
                m.Status = MembershipStatus.Active;
                m.JoinedAt = time.GetUtcNow();
                db.Audit(time, actor, "membership.approve", "User", userId);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new MembershipDto(clubId, userId, m.Role, m.Status));
        }).RequireAuthorization();

        clubs.MapPut("/{clubId:guid}/memberships/{userId:guid}/role", async (Guid clubId, Guid userId, ChangeRoleDto dto,
            ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var actor = p.UserId();
            if (dto.Role is null || !Enum.IsDefined(dto.Role.Value)) return Http.Problem(400, "Ruolo non valido.");
            if (await EnsureAdminAsync(db, clubId, actor, ct) is { } denied) return denied;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockClubAsync(db, clubId, ct);
            var m = await db.Memberships.FirstOrDefaultAsync(x => x.ClubId == clubId && x.UserId == userId, ct);
            if (m is null || m.Status != MembershipStatus.Active) return Http.Problem(404, "Membro non trovato.");
            if (m.Role == ClubRole.Admin && dto.Role != ClubRole.Admin && await IsLastAdminAsync(db, clubId, ct))
                return Http.Problem(409, "Il club deve avere almeno un amministratore.");

            m.Role = dto.Role.Value;
            db.Audit(time, actor, "membership.role", "User", userId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new MembershipDto(clubId, userId, m.Role, m.Status));
        }).RequireAuthorization();

        // Un admin può rimuovere chiunque; ciascuno può uscire dal club (o ritirare la propria richiesta).
        clubs.MapDelete("/{clubId:guid}/memberships/{userId:guid}", async (Guid clubId, Guid userId, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var actor = p.UserId();
            if (actor != userId && await EnsureAdminAsync(db, clubId, actor, ct) is { } denied) return denied;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockClubAsync(db, clubId, ct);
            var m = await db.Memberships.FirstOrDefaultAsync(x => x.ClubId == clubId && x.UserId == userId, ct);
            if (m is null) return Http.Problem(404, "Membro non trovato.");
            if (m.Role == ClubRole.Admin && m.Status == MembershipStatus.Active && await IsLastAdminAsync(db, clubId, ct))
                return Http.Problem(409, "Il club deve avere almeno un amministratore.");

            db.Memberships.Remove(m);
            db.Audit(time, actor, "membership.remove", "User", userId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    /// <summary>Null se l'utente è admin attivo del club; altrimenti la risposta di errore (404 / 403).</summary>
    private static async Task<IResult?> EnsureAdminAsync(RuntivoDbContext db, Guid clubId, Guid userId, CancellationToken ct)
    {
        if (!await db.Clubs.AnyAsync(c => c.Id == clubId, ct)) return Http.Problem(404, "Club non trovato.");
        var isAdmin = await db.Memberships.AnyAsync(m => m.ClubId == clubId && m.UserId == userId &&
            m.Status == MembershipStatus.Active && m.Role == ClubRole.Admin, ct);
        return isAdmin ? null : Http.Problem(403, "Non hai i permessi per gestire questo club.");
    }

    /// <summary>Serializza le modifiche ai ruoli dello stesso club, così il controllo "ultimo admin" è affidabile.</summary>
    private static Task LockClubAsync(RuntivoDbContext db, Guid clubId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Clubs\" WHERE \"Id\" = {clubId} FOR UPDATE", ct);

    private static async Task<bool> IsLastAdminAsync(RuntivoDbContext db, Guid clubId, CancellationToken ct) =>
        await db.Memberships.CountAsync(m => m.ClubId == clubId && m.Role == ClubRole.Admin &&
            m.Status == MembershipStatus.Active, ct) <= 1;
}
