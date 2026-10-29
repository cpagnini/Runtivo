using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Common;
using Runtivo.Api.Data;

namespace Runtivo.Api.Matching;

public record SlotDto(DayOfWeek Day, DayBand Band);
public record IntentInputDto(Guid? HomePlaceId, int? RadiusKm, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, int? DistanceMinMeters,
    int? DistanceMaxMeters, RunType? RunType, List<SlotDto>? Availability, bool? AllowPairs, bool? ConfirmAdult, int? ExpiresInDays);
public record IntentDto(Guid Id, Guid HomePlaceId, string HomePlaceName, string City, int RadiusKm, int PaceMinSecPerKm, int PaceMaxSecPerKm,
    int DistanceMinMeters, int DistanceMaxMeters, RunType RunType, List<SlotDto> Availability, bool AllowPairs, IntentStatus Status, DateTimeOffset ExpiresAt);
public record PlaceDto(Guid Id, string Name, string City);

public static class IntentEndpoints
{
    private const int DefaultDays = 30;
    private const int MaxDays = 90;

    public static void MapIntentEndpoints(this WebApplication app)
    {
        // Elenco pubblico dei punti di ritrovo: senza coordinate, che restano interne al calcolo delle distanze.
        app.MapGet("/api/places", async (string? city, RuntivoDbContext db, CancellationToken ct) =>
        {
            var query = db.PublicPlaces.AsNoTracking().Where(p => p.IsActive);
            if (!string.IsNullOrWhiteSpace(city)) query = query.Where(p => p.City == city.Trim());
            return Results.Ok(await query.OrderBy(p => p.City).ThenBy(p => p.Name).Select(p => new PlaceDto(p.Id, p.Name, p.City)).ToListAsync(ct));
        });

        var me = app.MapGroup("/api/me/intent").RequireAuthorization();

        me.MapGet("", async (ClaimsPrincipal p, RuntivoDbContext db, CancellationToken ct) =>
        {
            var dto = await LoadDtoAsync(db, p.UserId(), ct);
            return dto is null ? Http.Problem(404, "Non hai ancora pubblicato un annuncio.") : Results.Ok(dto);
        });

        me.MapPut("", async (IntentInputDto dto, ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var now = time.GetUtcNow();

            if (Validate(dto) is { } error) return Http.Problem(400, error);
            if (!await db.PublicPlaces.AnyAsync(x => x.Id == dto.HomePlaceId && x.IsActive, ct)) return Http.Problem(400, "Scegli una zona dall'elenco.");

            var intent = await db.RunIntents.FirstOrDefaultAsync(i => i.UserId == userId, ct);
            // La dichiarazione di maggiore età si chiede alla prima pubblicazione e non si azzera con le modifiche.
            if (intent is null && dto.ConfirmAdult != true) return Http.Problem(400, "Per pubblicare l'annuncio devi confermare di avere almeno 18 anni.");
            if (intent is null)
            {
                intent = new RunIntent { Id = Guid.NewGuid(), UserId = userId, AdultConfirmedAt = now, CreatedAt = now };
                db.RunIntents.Add(intent);
            }

            intent.HomePlaceId = dto.HomePlaceId!.Value;
            intent.RadiusKm = dto.RadiusKm!.Value;
            intent.PaceMinSecPerKm = dto.PaceMinSecPerKm!.Value;
            intent.PaceMaxSecPerKm = dto.PaceMaxSecPerKm!.Value;
            intent.DistanceMinMeters = dto.DistanceMinMeters!.Value;
            intent.DistanceMaxMeters = dto.DistanceMaxMeters!.Value;
            intent.RunType = dto.RunType!.Value;
            intent.AvailabilityMask = Availability.Mask(dto.Availability!.Select(s => (s.Day, s.Band)));
            intent.AllowPairs = dto.AllowPairs ?? true;
            intent.Status = IntentStatus.Active;
            intent.ExpiresAt = now.AddDays(dto.ExpiresInDays ?? DefaultDays);
            intent.UpdatedAt = now;
            db.Audit(time, userId, "intent.publish", "RunIntent", intent.Id);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await LoadDtoAsync(db, userId, ct));
        });

        me.MapPost("/pause", async (ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var intent = await db.RunIntents.FirstOrDefaultAsync(i => i.UserId == userId, ct);
            if (intent is null) return Http.Problem(404, "Non hai ancora pubblicato un annuncio.");
            intent.Status = IntentStatus.Paused;
            intent.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(await LoadDtoAsync(db, userId, ct));
        });

        me.MapPost("/resume", async (ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var now = time.GetUtcNow();
            var intent = await db.RunIntents.FirstOrDefaultAsync(i => i.UserId == userId, ct);
            if (intent is null) return Http.Problem(404, "Non hai ancora pubblicato un annuncio.");
            intent.Status = IntentStatus.Active;
            if (intent.ExpiresAt <= now) intent.ExpiresAt = now.AddDays(DefaultDays);
            intent.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await LoadDtoAsync(db, userId, ct));
        });

        me.MapDelete("", async (ClaimsPrincipal p, RuntivoDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var userId = p.UserId();
            var removed = await db.RunIntents.Where(i => i.UserId == userId).ExecuteDeleteAsync(ct);
            if (removed > 0)
            {
                db.Audit(time, userId, "intent.delete", "User", userId);
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });
    }

    private static string? Validate(IntentInputDto d)
    {
        if (d.HomePlaceId is null) return "Scegli una zona dall'elenco.";
        if (d.RadiusKm is not (>= 5 and <= 30)) return "Il raggio deve essere tra 5 e 30 km.";
        if (d.PaceMinSecPerKm is not (>= 120 and <= 1200) || d.PaceMaxSecPerKm is not (>= 120 and <= 1200)) return "Il passo non è valido.";
        if (d.PaceMinSecPerKm > d.PaceMaxSecPerKm) return "Il passo minimo non può superare il massimo.";
        if (d.DistanceMinMeters is not (>= 1000 and <= 60000) || d.DistanceMaxMeters is not (>= 1000 and <= 60000)) return "La distanza deve essere tra 1 e 60 km.";
        if (d.DistanceMinMeters > d.DistanceMaxMeters) return "La distanza minima non può superare la massima.";
        if (d.RunType is not { } type || !Enum.IsDefined(type)) return "Scegli il tipo di corsa.";
        if (d.Availability is not { Count: > 0 } slots) return "Indica almeno un giorno e una fascia oraria in cui sei libero.";
        if (slots.Any(s => !Enum.IsDefined(s.Day) || !Enum.IsDefined(s.Band))) return "Disponibilità non valida.";
        if (d.ExpiresInDays is { } days && days is < 1 or > MaxDays) return $"La scadenza può essere al massimo di {MaxDays} giorni.";
        return null;
    }

    internal static async Task<IntentDto?> LoadDtoAsync(RuntivoDbContext db, Guid userId, CancellationToken ct)
    {
        var row = await (from ri in db.RunIntents.AsNoTracking()
                         join pl in db.PublicPlaces on ri.HomePlaceId equals pl.Id
                         where ri.UserId == userId
                         select new { Intent = ri, pl.Name, pl.City }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var i = row.Intent;
        return new IntentDto(i.Id, i.HomePlaceId, row.Name, row.City, i.RadiusKm, i.PaceMinSecPerKm, i.PaceMaxSecPerKm, i.DistanceMinMeters,
            i.DistanceMaxMeters, i.RunType, Availability.Slots(i.AvailabilityMask).Select(s => new SlotDto(s.Day, s.Band)).ToList(),
            i.AllowPairs, i.Status, i.ExpiresAt);
    }
}
