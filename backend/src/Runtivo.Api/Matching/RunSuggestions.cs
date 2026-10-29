using Runtivo.Api.Events;

namespace Runtivo.Api.Matching;

/// <summary>Suggerimenti automatici per una proposta di uscita: dove incontrarsi, quando, con quale passo e distanza.</summary>
public static class RunSuggestions
{
    private const string TimeZoneId = "Europe/Rome";
    private static readonly TimeSpan MinNotice = TimeSpan.FromHours(24);
    private const int HorizonDays = 14;

    /// <summary>Il luogo che minimizza la distanza della persona più lontana; a parità, la somma delle due distanze.</summary>
    public static PublicPlace BestMeetingPlace(IEnumerable<PublicPlace> places, PublicPlace a, PublicPlace b) =>
        places
            .Select(p => (Place: p, Da: Geo.DistanceKm(p.Latitude, p.Longitude, a.Latitude, a.Longitude), Db: Geo.DistanceKm(p.Latitude, p.Longitude, b.Latitude, b.Longitude)))
            .OrderBy(x => Math.Max(x.Da, x.Db)).ThenBy(x => x.Da + x.Db)
            .First().Place;

    public static TimeOnly BandTime(DayBand band) => band switch
    {
        DayBand.Morning => new TimeOnly(8, 0),
        DayBand.Midday => new TimeOnly(12, 30),
        _ => new TimeOnly(19, 0),
    };

    /// <summary>Il primo istante utile (almeno 24 ore da ora, entro 14 giorni) tra le fasce in comune; null se non ce n'è.</summary>
    public static DateTimeOffset? NextSharedSlot(int sharedMask, DateTimeOffset now, string timeZoneId = TimeZoneId)
    {
        var from = Recurrence.LocalToday(now, timeZoneId);
        var to = from.AddDays(HorizonDays);
        DateTimeOffset? best = null;
        foreach (var (day, band) in Availability.Slots(sharedMask))
            foreach (var (_, startsAt) in Recurrence.Occurrences(day, BandTime(band), timeZoneId, from, to))
            {
                if (startsAt < now + MinNotice) continue;
                if (best is null || startsAt < best) best = startsAt;
                break; // le occorrenze sono in ordine: la prima utile di questa fascia basta
            }
        return best;
    }

    /// <summary>Intervallo comune a due intervalli, se esiste.</summary>
    public static (int Min, int Max)? Overlap(int minA, int maxA, int minB, int maxB)
    {
        var min = Math.Max(minA, minB);
        var max = Math.Min(maxA, maxB);
        return min <= max ? (min, max) : null;
    }
}
