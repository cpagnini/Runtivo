using Microsoft.EntityFrameworkCore;
using Runtivo.Api.Data;
using Runtivo.Api.Domain;

namespace Runtivo.Api.Matching;

/// <summary>Il mio annuncio (attivo e non scaduto) con il luogo di partenza.</summary>
public record MyIntentContext(RunIntent Intent, PublicPlace Place);

/// <summary>Una persona compatibile con me, con punteggio e distanza.</summary>
public record Compatible(Guid UserId, string DisplayName, string City, MatchScore Score, double DistanceKm, RunIntent Intent, PublicPlace Place);

/// <summary>
/// Compatibilità tra persone: la usano l'elenco dei match, gli interessi e gli inviti alle uscite, così le regole
/// (annuncio attivo, account attivo, nessun blocco, punteggio sopra soglia) sono identiche ovunque.
/// </summary>
public static class MatchService
{
    private const int MaxCandidates = 500;

    public static IntentProfile ToProfile(RunIntent i) =>
        new(i.PaceMinSecPerKm, i.PaceMaxSecPerKm, i.DistanceMinMeters, i.DistanceMaxMeters, i.RunType, i.AvailabilityMask, i.RadiusKm);

    /// <summary>Null se la persona non ha un annuncio attivo e non scaduto: senza, non può vedere né contattare nessuno.</summary>
    public static async Task<MyIntentContext?> LoadMineAsync(RuntivoDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var mine = await (from i in db.RunIntents.AsNoTracking()
                          join pl in db.PublicPlaces.AsNoTracking() on i.HomePlaceId equals pl.Id
                          where i.UserId == userId
                          select new MyIntentContext(i, pl)).FirstOrDefaultAsync(ct);
        return mine is not null && mine.Intent.Status == IntentStatus.Active && mine.Intent.ExpiresAt > now ? mine : null;
    }

    public static async Task<List<Compatible>> FindAsync(RuntivoDbContext db, MyIntentContext mine, DateTimeOffset now, CancellationToken ct)
    {
        var (minLat, maxLat, minLon, maxLon) = Geo.BoundingBox(mine.Place.Latitude, mine.Place.Longitude, mine.Intent.RadiusKm);
        var rows = await Candidates(db, mine.Intent.UserId, now)
            .Where(c => c.Place.Latitude >= minLat && c.Place.Latitude <= maxLat && c.Place.Longitude >= minLon && c.Place.Longitude <= maxLon)
            .OrderByDescending(c => c.Intent.UpdatedAt)
            .Take(MaxCandidates).ToListAsync(ct);
        return Score(mine, rows);
    }

    /// <summary>Come <see cref="FindAsync"/> ma per una sola persona: null se non è compatibile (per qualunque motivo).</summary>
    public static async Task<Compatible?> FindOneAsync(RuntivoDbContext db, MyIntentContext mine, Guid otherUserId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await Candidates(db, mine.Intent.UserId, now).Where(c => c.UserId == otherUserId).ToListAsync(ct);
        return Score(mine, rows).FirstOrDefault();
    }

    /// <summary>Per ciascuna delle persone indicate, l'uscita futura non annullata che ho in comune con lei (se esiste).</summary>
    public static async Task<Dictionary<Guid, Guid>> ActiveRunsWithAsync(RuntivoDbContext db, Guid me, IReadOnlyCollection<Guid> others, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await (from mine in db.RunParticipants
                          join other in db.RunParticipants on mine.RunId equals other.RunId
                          join run in db.SpontaneousRuns on mine.RunId equals run.Id
                          where mine.UserId == me && (mine.Status == ParticipantStatus.Pending || mine.Status == ParticipantStatus.Confirmed)
                                && others.Contains(other.UserId) && (other.Status == ParticipantStatus.Pending || other.Status == ParticipantStatus.Confirmed)
                                && run.Status != RunStatus.Cancelled && run.StartsAtUtc > now
                          orderby run.StartsAtUtc
                          select new { other.UserId, run.Id }).ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.First().Id);
    }

    private class Row
    {
        public Guid UserId { get; set; }
        public required string DisplayName { get; set; }
        public required PublicPlace Place { get; set; }
        public required RunIntent Intent { get; set; }
    }

    private static IQueryable<Row> Candidates(RuntivoDbContext db, Guid me, DateTimeOffset now) =>
        from i in db.RunIntents.AsNoTracking()
        join pl in db.PublicPlaces.AsNoTracking() on i.HomePlaceId equals pl.Id
        join u in db.Users.AsNoTracking() on i.UserId equals u.Id
        where i.UserId != me && i.Status == IntentStatus.Active && i.ExpiresAt > now && u.Status == UserStatus.Active
              && !db.UserBlocks.Any(b => (b.BlockerId == me && b.BlockedId == i.UserId) || (b.BlockerId == i.UserId && b.BlockedId == me))
        select new Row { UserId = i.UserId, DisplayName = u.DisplayName, Place = pl, Intent = i };

    private static List<Compatible> Score(MyIntentContext mine, IEnumerable<Row> rows)
    {
        var myProfile = ToProfile(mine.Intent);
        var result = new List<Compatible>();
        foreach (var r in rows)
        {
            var distance = Geo.DistanceKm(mine.Place.Latitude, mine.Place.Longitude, r.Place.Latitude, r.Place.Longitude);
            if (MatchScoring.Score(myProfile, ToProfile(r.Intent), distance) is { } score)
                result.Add(new Compatible(r.UserId, r.DisplayName, r.Place.City, score, distance, r.Intent, r.Place));
        }
        return result;
    }
}
