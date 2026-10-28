using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Events;

public record GroupInput(string? Name, int? DistanceMeters, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, Guid? LeaderUserId, int? Capacity);
public record CreateEventDto(string? Title, string? Description, DateTimeOffset? StartsAt, string? MeetingPointLabel,
    EventVisibility? Visibility, List<GroupInput>? Groups);
public record UpdateEventDto(string? Title, string? Description, DateTimeOffset? StartsAt, string? MeetingPointLabel, EventVisibility? Visibility);
public record RsvpDto(RsvpStatus? Status, Guid? PaceGroupId);

public record GroupDto(Guid Id, string Name, int? DistanceMeters, int? PaceMinSecPerKm, int? PaceMaxSecPerKm,
    Guid? LeaderUserId, string? LeaderName, int? Capacity, int GoingCount, List<string>? Participants);
public record MyRsvpDto(RsvpStatus Status, Guid? PaceGroupId);
public record EventDto(Guid Id, Guid ClubId, string ClubName, string Title, string? Description, DateTimeOffset StartsAtUtc,
    string TimeZoneId, string MeetingPointLabel, EventVisibility Visibility, EventStatus Status, ClubRole? MyRole,
    List<GroupDto> Groups, MyRsvpDto? MyRsvp, Guid? SeriesId);
public record EventSummaryDto(Guid Id, string Title, DateTimeOffset StartsAtUtc, string MeetingPointLabel,
    EventVisibility Visibility, EventStatus Status, int GroupCount, int GoingCount);
public record MyEventDto(Guid EventId, string Title, DateTimeOffset StartsAtUtc, string MeetingPointLabel, EventStatus EventStatus,
    Guid ClubId, string ClubName, string GroupName);

public static class EventEndpoints
{
    private const string DefaultTimeZone = "Europe/Rome";
    private const int MaxGroups = 8;

    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapPost("/api/clubs/{clubId:guid}/events", async (Guid clubId, CreateEventDto dto, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            if (await EnsureClubAdminAsync(db, clubId, userId, ct) is { } denied) return denied;

            var now = time.GetUtcNow();
            if (Validate(dto.Title, dto.Description, dto.MeetingPointLabel) is { } err) return Http.Problem(400, err);
            if (dto.StartsAt is not { } startsAt || startsAt <= now) return Http.Problem(400, "La data di inizio deve essere nel futuro.");
            if (dto.Visibility is not { } visibility || !Enum.IsDefined(visibility)) return Http.Problem(400, "Visibilità non valida.");
            if (dto.Groups is not { Count: >= 1 and <= MaxGroups } groups)
                return Http.Problem(400, $"Servono da 1 a {MaxGroups} gruppi di passo.");

            var ev = new RunEvent
            {
                Id = Guid.NewGuid(), ClubId = clubId, Title = dto.Title!.Trim(), Description = Clean(dto.Description),
                StartsAtUtc = startsAt.ToUniversalTime(), TimeZoneId = DefaultTimeZone,
                MeetingPointLabel = dto.MeetingPointLabel!.Trim(), Visibility = visibility,
                CreatedByUserId = userId, CreatedAt = now,
            };
            for (var i = 0; i < groups.Count; i++)
            {
                if (await ValidateGroupAsync(db, clubId, groups[i], ct) is { } groupError)
                    return Http.Problem(400, $"Gruppo {i + 1}: {groupError}");
                ev.Groups.Add(ToGroup(Guid.NewGuid(), ev.Id, groups[i], i));
            }
            db.Events.Add(ev);
            db.Audit(time, userId, "event.create", "Event", ev.Id);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/events/{ev.Id}", new { ev.Id });
        }).RequireAuthorization();

        // Eventi futuri del club: i pubblici per tutti, quelli riservati solo ai membri attivi.
        app.MapGet("/api/clubs/{clubId:guid}/events", async (Guid clubId, ClaimsPrincipal p, RuntivoDbContext db,
            TimeProvider time, CancellationToken ct) =>
        {
            if (!await db.Clubs.AnyAsync(c => c.Id == clubId && c.Status == ClubStatus.Active, ct))
                return Http.Problem(404, "Club non trovato.");
            var isMember = await IsActiveMemberAsync(db, clubId, p, ct);
            var now = time.GetUtcNow();

            var rows = await db.Events
                .Where(e => e.ClubId == clubId && e.StartsAtUtc > now && (isMember || e.Visibility == EventVisibility.Public))
                .OrderBy(e => e.StartsAtUtc)
                .Select(e => new EventSummaryDto(e.Id, e.Title, e.StartsAtUtc, e.MeetingPointLabel, e.Visibility, e.Status,
                    e.Groups.Count, db.Rsvps.Count(r => r.EventId == e.Id && r.Status == RsvpStatus.Going)))
                .Take(100).ToListAsync(ct);
            return Results.Ok(rows);
        });

        app.MapGet("/api/events/{eventId:guid}", async (Guid eventId, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var access = await LoadAccessAsync(db, eventId, p, ct);
            return access is null ? NotFound() : Results.Ok(await BuildEventDtoAsync(db, access, ct));
        });

        app.MapPatch("/api/events/{eventId:guid}", async (Guid eventId, UpdateEventDto dto, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var access = await LoadAccessAsync(db, eventId, p, ct);
            if (access is null) return NotFound();
            if (!access.IsAdmin) return Http.Problem(403, "Non hai i permessi per modificare questo evento.");
            var ev = access.Event;
            if (ev.Status == EventStatus.Cancelled) return Http.Problem(409, "L'evento è annullato e non si può modificare.");

            if (Validate(dto.Title ?? ev.Title, dto.Description, dto.MeetingPointLabel ?? ev.MeetingPointLabel) is { } err)
                return Http.Problem(400, err);
            if (dto.Visibility is { } v && !Enum.IsDefined(v)) return Http.Problem(400, "Visibilità non valida.");
            if (dto.StartsAt is { } s && s <= time.GetUtcNow()) return Http.Problem(400, "La data di inizio deve essere nel futuro.");

            if (dto.Title is not null) ev.Title = dto.Title.Trim();
            if (dto.Description is not null) ev.Description = Clean(dto.Description);
            if (dto.MeetingPointLabel is not null) ev.MeetingPointLabel = dto.MeetingPointLabel.Trim();
            if (dto.Visibility is { } vis) ev.Visibility = vis;
            if (dto.StartsAt is { } at) ev.StartsAtUtc = at.ToUniversalTime();
            db.Audit(time, p.UserId(), "event.update", "Event", ev.Id);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildEventDtoAsync(db, access, ct));
        }).RequireAuthorization();

        // Sostituisce i dati di un gruppo. La capienza non può scendere sotto i partecipanti già confermati.
        app.MapPut("/api/events/{eventId:guid}/groups/{groupId:guid}", async (Guid eventId, Guid groupId, GroupInput dto,
            ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var access = await LoadAccessAsync(db, eventId, p, ct);
            if (access is null) return NotFound();
            if (!access.IsAdmin) return Http.Problem(403, "Non hai i permessi per modificare questo evento.");
            if (access.Event.Status == EventStatus.Cancelled) return Http.Problem(409, "L'evento è annullato e non si può modificare.");
            if (await ValidateGroupAsync(db, access.Event.ClubId, dto, ct) is { } err) return Http.Problem(400, err);
            if (!await db.PaceGroups.AnyAsync(g => g.Id == groupId && g.EventId == eventId, ct)) return Http.Problem(404, "Gruppo non trovato.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockGroupAsync(db, groupId, ct);
            var group = await db.PaceGroups.FirstAsync(g => g.Id == groupId, ct);
            if (dto.Capacity is { } cap)
            {
                var going = await CountGoingAsync(db, groupId, ct);
                if (cap < going) return Http.Problem(409, $"Nel gruppo ci sono già {going} partecipanti: la capienza non può essere inferiore.");
            }
            group.Name = dto.Name!.Trim();
            group.DistanceMeters = dto.DistanceMeters;
            group.PaceMinSecPerKm = dto.PaceMinSecPerKm;
            group.PaceMaxSecPerKm = dto.PaceMaxSecPerKm;
            group.LeaderUserId = dto.LeaderUserId;
            group.Capacity = dto.Capacity;
            db.Audit(time, p.UserId(), "event.group.update", "PaceGroup", groupId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(await BuildEventDtoAsync(db, access, ct));
        }).RequireAuthorization();

        app.MapPost("/api/events/{eventId:guid}/cancel", async (Guid eventId, ClaimsPrincipal p, RuntivoDbContext db,
            TimeProvider time, CancellationToken ct) =>
        {
            var access = await LoadAccessAsync(db, eventId, p, ct);
            if (access is null) return NotFound();
            if (!access.IsAdmin) return Http.Problem(403, "Non hai i permessi per annullare questo evento.");
            if (access.Event.Status != EventStatus.Cancelled)
            {
                access.Event.Status = EventStatus.Cancelled;
                db.Audit(time, p.UserId(), "event.cancel", "Event", eventId);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(await BuildEventDtoAsync(db, access, ct));
        }).RequireAuthorization();

        // L'RSVP riguarda sempre l'utente della sessione. Idempotente: ripeterlo non crea duplicati.
        app.MapPut("/api/events/{eventId:guid}/rsvp", async (Guid eventId, RsvpDto dto, ClaimsPrincipal p,
            RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var access = await LoadAccessAsync(db, eventId, p, ct);
            if (access is null) return NotFound();
            var ev = access.Event;
            if (dto.Status is not { } status || !Enum.IsDefined(status)) return Http.Problem(400, "Risposta non valida.");
            if (ev.Status == EventStatus.Cancelled) return Http.Problem(409, "L'evento è stato annullato.");
            var now = time.GetUtcNow();
            if (ev.StartsAtUtc <= now) return Http.Problem(409, "L'evento è già iniziato.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            Guid? groupId = null;
            if (status == RsvpStatus.Going)
            {
                if (dto.PaceGroupId is not { } requested ||
                    !await db.PaceGroups.AnyAsync(g => g.Id == requested && g.EventId == eventId, ct))
                    return Http.Problem(400, "Scegli un gruppo di questo evento.");
                groupId = requested;

                // Il lock sulla riga del gruppo serializza le richieste concorrenti sullo stesso gruppo:
                // controllo di capienza e scrittura avvengono nella stessa transazione.
                await LockGroupAsync(db, requested, ct);
                var group = await db.PaceGroups.AsNoTracking().FirstAsync(g => g.Id == requested, ct);
                var existingForGroup = await db.Rsvps.AnyAsync(r => r.EventId == eventId && r.UserId == userId &&
                    r.Status == RsvpStatus.Going && r.PaceGroupId == requested, ct);
                if (group.Capacity is { } capacity && !existingForGroup && await CountGoingAsync(db, requested, ct) >= capacity)
                    return Http.Problem(409, "Il gruppo è al completo: la tua adesione attuale non è cambiata.");
            }

            var rsvp = await db.Rsvps.FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId, ct);
            if (rsvp is null)
            {
                rsvp = new EventRsvp { Id = Guid.NewGuid(), EventId = eventId, UserId = userId, CreatedAt = now };
                db.Rsvps.Add(rsvp);
            }
            rsvp.Status = status;
            rsvp.PaceGroupId = groupId;
            rsvp.UpdatedAt = now;
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Due richieste simultanee dello stesso utente su gruppi diversi: vince la prima.
                return Http.Problem(409, "Esiste già una richiesta in corso, riprova.");
            }
            return Results.Ok(new MyRsvpDto(rsvp.Status, rsvp.PaceGroupId));
        }).RequireAuthorization();

        app.MapDelete("/api/events/{eventId:guid}/rsvp", async (Guid eventId, ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var userId = p.UserId();
            await db.Rsvps.Where(r => r.EventId == eventId && r.UserId == userId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/api/me/events", async (ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var now = time.GetUtcNow();
            var rows = await (from r in db.Rsvps
                              join e in db.Events on r.EventId equals e.Id
                              join g in db.PaceGroups on r.PaceGroupId equals g.Id
                              join c in db.Clubs on e.ClubId equals c.Id
                              where r.UserId == userId && r.Status == RsvpStatus.Going && e.StartsAtUtc > now
                              orderby e.StartsAtUtc
                              select new MyEventDto(e.Id, e.Title, e.StartsAtUtc, e.MeetingPointLabel, e.Status, c.Id, c.Name, g.Name))
                .Take(100).ToListAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization();
    }

    // ---- accesso e visibilità ----

    private record EventAccess(RunEvent Event, string ClubName, Guid? UserId, ClubRole? Role)
    {
        public bool IsMember => Role is not null;
        public bool IsAdmin => Role == ClubRole.Admin;
    }

    /// <summary>
    /// Carica l'evento se l'utente può vederlo; altrimenti null (anche per gli eventi riservati: 404, non 403,
    /// così l'esistenza di un evento privato non si deduce dall'id).
    /// </summary>
    private static async Task<EventAccess?> LoadAccessAsync(RuntivoDbContext db, Guid eventId, ClaimsPrincipal p, CancellationToken ct)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return null;
        var clubName = await db.Clubs.Where(c => c.Id == ev.ClubId && c.Status == ClubStatus.Active).Select(c => c.Name).FirstOrDefaultAsync(ct);
        if (clubName is null) return null;

        Guid? userId = p.Identity?.IsAuthenticated == true ? p.UserId() : null;
        ClubRole? role = null;
        if (userId is { } uid)
            role = await db.Memberships.Where(m => m.ClubId == ev.ClubId && m.UserId == uid && m.Status == MembershipStatus.Active)
                .Select(m => (ClubRole?)m.Role).FirstOrDefaultAsync(ct);

        if (ev.Visibility == EventVisibility.ClubOnly && role is null) return null;
        return new EventAccess(ev, clubName, userId, role);
    }

    private static async Task<bool> IsActiveMemberAsync(RuntivoDbContext db, Guid clubId, ClaimsPrincipal p, CancellationToken ct)
    {
        if (p.Identity?.IsAuthenticated != true) return false;
        var userId = p.UserId();
        return await db.Memberships.AnyAsync(m => m.ClubId == clubId && m.UserId == userId && m.Status == MembershipStatus.Active, ct);
    }

    internal static async Task<IResult?> EnsureClubAdminAsync(RuntivoDbContext db, Guid clubId, Guid userId, CancellationToken ct)
    {
        if (!await db.Clubs.AnyAsync(c => c.Id == clubId && c.Status == ClubStatus.Active, ct)) return Http.Problem(404, "Club non trovato.");
        var isAdmin = await db.Memberships.AnyAsync(m => m.ClubId == clubId && m.UserId == userId &&
            m.Status == MembershipStatus.Active && m.Role == ClubRole.Admin, ct);
        return isAdmin ? null : Http.Problem(403, "Non hai i permessi per gestire questo club.");
    }

    private static IResult NotFound() => Http.Problem(404, "Evento non trovato.");

    // ---- risposta ----

    private static async Task<EventDto> BuildEventDtoAsync(RuntivoDbContext db, EventAccess access, CancellationToken ct)
    {
        var ev = access.Event;
        var groups = await db.PaceGroups.AsNoTracking().Where(g => g.EventId == ev.Id).OrderBy(g => g.SortOrder).ToListAsync(ct);
        var going = await (from r in db.Rsvps
                           join u in db.Users on r.UserId equals u.Id
                           where r.EventId == ev.Id && r.Status == RsvpStatus.Going
                           orderby r.CreatedAt
                           select new { r.PaceGroupId, u.DisplayName }).ToListAsync(ct);
        var leaderIds = groups.Where(g => g.LeaderUserId != null).Select(g => g.LeaderUserId!.Value).ToList();
        var leaders = await db.Users.Where(u => leaderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        // Privacy: i nomi dei partecipanti li vedono solo i membri del club; gli altri vedono i contatori.
        var groupDtos = groups.Select(g => new GroupDto(g.Id, g.Name, g.DistanceMeters, g.PaceMinSecPerKm, g.PaceMaxSecPerKm,
            access.IsMember ? g.LeaderUserId : null,
            g.LeaderUserId is { } l && leaders.TryGetValue(l, out var n) ? n : null,
            g.Capacity, going.Count(x => x.PaceGroupId == g.Id),
            access.IsMember ? going.Where(x => x.PaceGroupId == g.Id).Select(x => x.DisplayName).ToList() : null)).ToList();

        MyRsvpDto? mine = null;
        if (access.UserId is { } uid)
        {
            var r = await db.Rsvps.AsNoTracking().FirstOrDefaultAsync(x => x.EventId == ev.Id && x.UserId == uid, ct);
            if (r is not null) mine = new MyRsvpDto(r.Status, r.PaceGroupId);
        }
        return new EventDto(ev.Id, ev.ClubId, access.ClubName, ev.Title, ev.Description, ev.StartsAtUtc, ev.TimeZoneId,
            ev.MeetingPointLabel, ev.Visibility, ev.Status, access.Role, groupDtos, mine, ev.SeriesId);
    }

    // ---- validazione e utilità ----

    internal static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static string? Validate(string? title, string? description, string? meetingPoint)
    {
        if (title?.Trim().Length is not (>= 2 and <= 100)) return "Il titolo deve avere da 2 a 100 caratteri.";
        if (description?.Length > 1000) return "La descrizione può avere al massimo 1000 caratteri.";
        if (meetingPoint?.Trim().Length is not (>= 2 and <= 120)) return "Il luogo di ritrovo deve avere da 2 a 120 caratteri.";
        return null;
    }

    internal static async Task<string?> ValidateGroupAsync(RuntivoDbContext db, Guid clubId, GroupInput g, CancellationToken ct)
    {
        if (g.Name?.Trim().Length is not (>= 1 and <= 40)) return "il nome deve avere da 1 a 40 caratteri.";
        if (g.DistanceMeters is { } d && d is < 500 or > 100_000) return "la distanza deve essere tra 0,5 e 100 km.";
        if (g.PaceMinSecPerKm is { } a && a is < 120 or > 1200) return "il passo minimo non è valido.";
        if (g.PaceMaxSecPerKm is { } b && b is < 120 or > 1200) return "il passo massimo non è valido.";
        if (g.PaceMinSecPerKm is { } lo && g.PaceMaxSecPerKm is { } hi && lo > hi) return "il passo minimo non può superare il massimo.";
        if (g.Capacity is { } c && c is < 1 or > 500) return "la capienza deve essere tra 1 e 500.";
        if (g.LeaderUserId is { } leader &&
            !await db.Memberships.AnyAsync(m => m.ClubId == clubId && m.UserId == leader && m.Status == MembershipStatus.Active, ct))
            return "il capogruppo deve essere un membro del club.";
        return null;
    }

    internal static PaceGroup ToGroup(Guid id, Guid eventId, GroupInput g, int order) => new()
    {
        Id = id, EventId = eventId, Name = g.Name!.Trim(), DistanceMeters = g.DistanceMeters,
        PaceMinSecPerKm = g.PaceMinSecPerKm, PaceMaxSecPerKm = g.PaceMaxSecPerKm,
        LeaderUserId = g.LeaderUserId, Capacity = g.Capacity, SortOrder = order,
    };

    private static Task LockGroupAsync(RuntivoDbContext db, Guid groupId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PaceGroups\" WHERE \"Id\" = {groupId} FOR UPDATE", ct);

    private static Task<int> CountGoingAsync(RuntivoDbContext db, Guid groupId, CancellationToken ct) =>
        db.Rsvps.CountAsync(r => r.PaceGroupId == groupId && r.Status == RsvpStatus.Going, ct);
}
