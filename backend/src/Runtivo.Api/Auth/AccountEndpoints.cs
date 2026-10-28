using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Auth;

public record NotificationPrefsDto(bool EventChangesEmail, bool RemindersEmail, bool DiscoveryEmails);
public record DeleteAccountDto(string? ConfirmEmail);

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization();

        // Le preferenze sono salvate e rispettate fin da ora; l'invio delle email arriverà con il provider.
        me.MapGet("/notifications", async (ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var prefs = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == p.UserId(), ct);
            return Results.Ok(prefs is null ? new NotificationPrefsDto(true, false, false)
                : new NotificationPrefsDto(prefs.EventChangesEmail, prefs.RemindersEmail, prefs.DiscoveryEmails));
        });

        me.MapPut("/notifications", async (NotificationPrefsDto dto, ClaimsPrincipal p, RuntivoDbContext db,
            TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var prefs = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (prefs is null)
            {
                prefs = new NotificationPreference { UserId = userId };
                db.NotificationPreferences.Add(prefs);
            }
            prefs.EventChangesEmail = dto.EventChangesEmail;
            prefs.RemindersEmail = dto.RemindersEmail;
            prefs.DiscoveryEmails = dto.DiscoveryEmails;
            prefs.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(dto);
        });

        // Portabilità: tutti i dati che la piattaforma conserva su chi è loggato, e solo i suoi.
        me.MapGet("/export", async (ClaimsPrincipal p, RuntivoDbContext db, HttpContext http, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Http.Problem(401, "Sessione non valida.");

            var memberships = await (from m in db.Memberships
                                     join c in db.Clubs on m.ClubId equals c.Id
                                     where m.UserId == userId
                                     select new { Club = c.Name, Role = m.Role.ToString(), Status = m.Status.ToString(), m.JoinedAt }).ToListAsync(ct);
            var rsvps = await (from r in db.Rsvps
                               join e in db.Events on r.EventId equals e.Id
                               join g in db.PaceGroups on r.PaceGroupId equals g.Id into groups
                               from g in groups.DefaultIfEmpty()
                               where r.UserId == userId
                               orderby e.StartsAtUtc
                               select new { Event = e.Title, e.StartsAtUtc, Status = r.Status.ToString(), Group = g != null ? g.Name : null, r.CreatedAt, r.UpdatedAt }).ToListAsync(ct);
            var prefs = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            var audit = await db.AuditEntries.AsNoTracking().Where(a => a.ActorUserId == userId).OrderBy(a => a.Timestamp)
                .Select(a => new { a.Action, a.TargetType, a.TargetId, a.Timestamp }).ToListAsync(ct);

            http.Response.Headers.ContentDisposition = "attachment; filename=\"runtivo-i-miei-dati.json\"";
            return Results.Ok(new
            {
                ExportedAt = time.GetUtcNow(),
                Profile = new { user.Id, user.Email, user.DisplayName, user.City, user.PreferredPaceSecPerKm, user.CreatedAt },
                Notifications = prefs is null ? new NotificationPrefsDto(true, false, false)
                    : new NotificationPrefsDto(prefs.EventChangesEmail, prefs.RemindersEmail, prefs.DiscoveryEmails),
                Memberships = memberships,
                Rsvps = rsvps,
                Activity = audit,
            });
        });

        me.MapDelete("", async ([FromBody] DeleteAccountDto dto, ClaimsPrincipal p, RuntivoDbContext db, HttpContext http,
            TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Http.Problem(401, "Sessione non valida.");
            if (Emails.Normalize(dto.ConfirmEmail) != user.Email)
                return Http.Problem(400, "Scrivi il tuo indirizzo email per confermare.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Chi è l'ultimo admin di un club non può sparire lasciando i membri senza gestione:
            // se il club ha altri membri va prima passato il ruolo; se è solo suo, viene eliminato con lui.
            var adminClubs = await (from m in db.Memberships
                                    join c in db.Clubs on m.ClubId equals c.Id
                                    where m.UserId == userId && m.Role == ClubRole.Admin && m.Status == MembershipStatus.Active
                                    select new { c.Id, c.Name }).ToListAsync(ct);
            var clubsToDelete = new List<Guid>();
            foreach (var club in adminClubs)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Clubs\" WHERE \"Id\" = {club.Id} FOR UPDATE", ct);
                var otherAdmins = await db.Memberships.CountAsync(m => m.ClubId == club.Id && m.UserId != userId &&
                    m.Role == ClubRole.Admin && m.Status == MembershipStatus.Active, ct);
                if (otherAdmins > 0) continue;
                var otherMembers = await db.Memberships.CountAsync(m => m.ClubId == club.Id && m.UserId != userId && m.Status == MembershipStatus.Active, ct);
                if (otherMembers > 0)
                    return Http.Problem(409, $"Sei l'unico amministratore di «{club.Name}», che ha altri membri: assegna prima il ruolo a qualcun altro.");
                clubsToDelete.Add(club.Id);
            }

            if (clubsToDelete.Count > 0) await db.Clubs.Where(c => clubsToDelete.Contains(c.Id)).ExecuteDeleteAsync(ct);
            await db.PaceGroups.Where(g => g.LeaderUserId == userId).ExecuteUpdateAsync(s => s.SetProperty(g => g.LeaderUserId, (Guid?)null), ct);
            await db.Invitations.Where(i => i.CreatedByUserId == userId || i.InvitedEmail == user.Email).ExecuteDeleteAsync(ct);
            await db.LoginTokens.Where(t => t.Email == user.Email).ExecuteDeleteAsync(ct);
            await db.NotificationPreferences.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);
            // Il registro di audit resta, ma senza più il collegamento alla persona.
            await db.AuditEntries.Where(a => a.ActorUserId == userId).ExecuteUpdateAsync(s => s.SetProperty(a => a.ActorUserId, (Guid?)null), ct);
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct); // iscrizioni e RSVP seguono a cascata
            db.Audit(time, null, "account.delete", "User", userId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }
}
