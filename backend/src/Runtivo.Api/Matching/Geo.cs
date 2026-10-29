namespace Runtivo.Api.Matching;

public static class Geo
{
    private const double EarthRadiusKm = 6371;
    private const double KmPerDegreeLatitude = 111.32;

    /// <summary>Distanza in linea d'aria (formula di Haversine).</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Riquadro che contiene il cerchio di dato raggio: serve come prefiltro economico prima di Haversine.</summary>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon) BoundingBox(double lat, double lon, double radiusKm)
    {
        var dLat = radiusKm / KmPerDegreeLatitude;
        var dLon = radiusKm / (KmPerDegreeLatitude * Math.Cos(ToRad(lat)));
        return (lat - dLat, lat + dLat, lon - dLon, lon + dLon);
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180;
}

/// <summary>Disponibilità settimanale come maschera di bit: 7 giorni × 3 fasce.</summary>
public static class Availability
{
    public static int SlotBit(DayOfWeek day, DayBand band) => 1 << ((int)day * 3 + (int)band);

    public static int Mask(IEnumerable<(DayOfWeek Day, DayBand Band)> slots) =>
        slots.Aggregate(0, (mask, s) => mask | SlotBit(s.Day, s.Band));

    public static IReadOnlyList<(DayOfWeek Day, DayBand Band)> Slots(int mask)
    {
        var result = new List<(DayOfWeek, DayBand)>();
        for (var day = 0; day < 7; day++)
            for (var band = 0; band < 3; band++)
                if ((mask & (1 << (day * 3 + band))) != 0) result.Add(((DayOfWeek)day, (DayBand)band));
        return result;
    }
}
