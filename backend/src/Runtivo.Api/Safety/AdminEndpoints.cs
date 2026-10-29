using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;
using Runtivo.Api.Matching;

namespace Runtivo.Api.Safety;

public record AdminReportDto(Guid Id, ReportReason Reason, string? Details, ReportStatus Status, DateTimeOffset CreatedAt, Guid ReportedUserId,
    string ReportedName, UserStatus? ReportedStatus, string? ReporterName, int OpenReportsAgainstTarget, Guid? RunId);
public record ResolveReportInput(string? Action, string? Note);
public record SuspendInput(string? Reason);

public static class AdminEndpoints
{
    private const int MaxNote = 500;

    public static void MapAdminEndpoints(this WebApplication app)
    {
        // Tutte le API amministrative: 403 a chiunque non sia nell'elenco degli amministratori.
        var admin = app.MapGroup("/api/admin").RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var db = http.RequestServices.GetRequiredService<RuntivoDbContext>();
            var options = http.RequestServices.GetRequiredService<IOptions<PlatformOptions>>();
            return await PlatformAdmin.IsAsync(db, options, http.User.UserId(), http.RequestAborted)
                ? await next(ctx)
                : Http.Problem(403, "Non hai i permessi per questa operazione.");
        });

        admin.MapGet("/reports", async (ReportStatus? status, RuntivoDbContext db, CancellationToken ct) =>
        {
            var wanted = status ?? ReportStatus.Open;
            var rows = await (from r in db.Reports.AsNoTracking()
                              where r.Status == wanted
                              join target in db.Users.AsNoTracking() on r.ReportedUserId equals target.Id into targets
                              from target in targets.DefaultIfEmpty()
                              join reporter in db.Users.AsNoTracking() on r.ReporterUserId equals reporter.Id into reporters
                              from reporter in reporters.DefaultIfEmpty()
                              orderby r.CreatedAt descending
                              select new AdminReportDto(r.Id, r.Reason, r.Details, r.Status, r.CreatedAt, r.ReportedUserId,
                                  target != null ? target.DisplayName : "(account eliminato)", target != null ? target.Status : null,
                                  reporter != null ? reporter.DisplayName : null,
                                  db.Reports.Count(x => x.ReportedUserId == r.ReportedUserId && x.Status == ReportStatus.Open), r.RunId))
                .Take(100).ToListAsync(ct);
            return Results.Ok(rows);
        });

        admin.MapPost("/reports/{reportId:guid}/resolve", async (Guid reportId, ResolveReportInput dto, ClaimsPrincipal p, RuntivoDbContext db,
            IOptions<PlatformOptions> options, TimeProvider time, CancellationToken ct) =>
        {
            var adminId = p.UserId();
            var now = time.GetUtcNow();
            if (dto.Action is not ("Dismiss" or "Suspend")) return Http.Problem(400, "Azione non valida.");
            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (note?.Length > MaxNote) return Http.Problem(400, $"La nota può essere al massimo di {MaxNote} caratteri.");

            var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
            if (report is null) return Http.Problem(404, "Segnalazione non trovata.");
            if (report.Status != ReportStatus.Open) return Http.Problem(409, "La segnalazione è già stata esaminata.");

            if (dto.Action == "Dismiss")
            {
                Close(report, ReportStatus.Dismissed, adminId, note, now);
                db.Audit(time, adminId, "admin.report.dismiss", "Report", reportId);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { report.Id, Status = report.Status.ToString() });
            }

            var result = await SuspendAsync(db, options.Value, time, adminId, report.ReportedUserId, note, ct);
            if (result is not null) return result;
            return Results.Ok(new { report.Id, Status = ReportStatus.ActionTaken.ToString() });
        });

        admin.MapPost("/users/{userId:guid}/suspend", async (Guid userId, SuspendInput dto, ClaimsPrincipal p, RuntivoDbContext db,
            IOptions<PlatformOptions> options, TimeProvider time, CancellationToken ct) =>
        {
            var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (reason?.Length > MaxNote) return Http.Problem(400, $"Il motivo può essere al massimo di {MaxNote} caratteri.");
            return await SuspendAsync(db, options.Value, time, p.UserId(), userId, reason, ct) ?? Results.Ok(new { UserId = userId, Status = UserStatus.Suspended.ToString() });
        });

        admin.MapPost("/users/{userId:guid}/unsuspend", async (Guid userId, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Http.Problem(404, "Utente non trovato.");
            if (user.Status == UserStatus.Suspended)
            {
                user.Status = UserStatus.Active;
                db.Audit(time, p.UserId(), "admin.user.unsuspend", "User", userId);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { UserId = userId, Status = user.Status.ToString() });
        });
    }

    private static void Close(Report report, ReportStatus status, Guid adminId, string? note, DateTimeOffset now)
    {
        report.Status = status;
        report.ReviewedAt = now;
        report.ReviewedByUserId = adminId;
        report.ResolutionNote = note;
    }

    /// <summary>
    /// Sospende l'account: non può più accedere (la sessione aperta cade alla richiesta successiva), l'annuncio va in pausa,
    /// le uscite future vengono lasciate con le solite regole e tutte le segnalazioni aperte su di lui si chiudono.
    /// Restituisce una risposta di errore, oppure null se è andata a buon fine.
    /// </summary>
    private static async Task<IResult?> SuspendAsync(RuntivoDbContext db, PlatformOptions options, TimeProvider time, Guid adminId, Guid targetId, string? note, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (targetId == adminId) return Http.Problem(400, "Non puoi sospendere il tuo stesso account.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == targetId, ct);
        if (user is null) return Http.Problem(404, "Utente non trovato.");
        if (PlatformAdmin.IsAdminEmail(options, user.Email)) return Http.Problem(400, "Non si può sospendere un amministratore.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        user.Status = UserStatus.Suspended;
        await db.RunIntents.Where(i => i.UserId == targetId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, IntentStatus.Paused), ct);
        await RunMembership.LeaveFutureRunsAsync(db, targetId, null, now, ct);
        var open = await db.Reports.Where(r => r.ReportedUserId == targetId && r.Status == ReportStatus.Open).ToListAsync(ct);
        foreach (var r in open) Close(r, ReportStatus.ActionTaken, adminId, note, now);
        db.Audit(time, adminId, "admin.user.suspend", "User", targetId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }
}
