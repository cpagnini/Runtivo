using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Matching;

namespace Runtivo.Api.Safety;

public record ReportInput(Guid? ReportedUserId, Guid? RunId, ReportReason? Reason, string? Details);

public static class ReportEndpoints
{
    private const int DailyLimit = 10;
    private const int MaxDetails = 500;

    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapPost("/api/reports", async (ReportInput dto, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var me = p.UserId();
            var now = time.GetUtcNow();

            if (dto.ReportedUserId is not { } reportedId) return Http.Problem(400, "Indica la persona da segnalare.");
            if (reportedId == me) return Http.Problem(400, "Non puoi segnalare te stesso.");
            if (dto.Reason is not { } reason || !Enum.IsDefined(reason)) return Http.Problem(400, "Scegli un motivo per la segnalazione.");
            var details = string.IsNullOrWhiteSpace(dto.Details) ? null : dto.Details.Trim();
            if (details?.Length > MaxDetails) return Http.Problem(400, $"I dettagli possono essere al massimo {MaxDetails} caratteri.");
            if (!await db.Users.AnyAsync(u => u.Id == reportedId, ct)) return Http.Problem(404, "Persona non trovata.");

            // Con un'uscita di mezzo, segnalante e persona segnalata devono essere entrambi partecipanti.
            if (dto.RunId is { } runId)
            {
                var both = await db.RunParticipants.CountAsync(x => x.RunId == runId && (x.UserId == me || x.UserId == reportedId), ct);
                if (both < 2) return Http.Problem(404, "Persona non trovata.");
            }

            // Ripetere la stessa segnalazione aperta nelle ultime 24 ore non ne crea un'altra.
            var since = now.AddHours(-24);
            var existing = await db.Reports.FirstOrDefaultAsync(r => r.ReporterUserId == me && r.ReportedUserId == reportedId &&
                r.Status == ReportStatus.Open && r.CreatedAt > since, ct);
            if (existing is not null) return Results.Ok(new { existing.Id });

            if (await db.Reports.CountAsync(r => r.ReporterUserId == me && r.CreatedAt > since, ct) >= DailyLimit)
                return Http.Problem(429, "Hai fatto molte segnalazioni oggi: riprova domani.");

            var report = new Report
            {
                Id = Guid.NewGuid(), ReporterUserId = me, ReportedUserId = reportedId, RunId = dto.RunId,
                Reason = reason, Details = details, CreatedAt = now,
            };
            db.Reports.Add(report);
            db.Audit(time, me, "report.create", "Report", report.Id);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/reports/{report.Id}", new { report.Id });
        }).RequireAuthorization();
    }
}
