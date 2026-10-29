using Runtivo.Api.Matching;

namespace Runtivo.Api.Tests;

public class GeoTests
{
    [Fact]
    public void Distance_between_Prato_and_Florence_centres_is_about_16_km()
    {
        var km = Geo.DistanceKm(43.8790, 11.0963, 43.7687, 11.2626);
        Assert.InRange(km, 15, 19);
    }

    [Fact]
    public void Distance_to_itself_is_zero() => Assert.Equal(0, Geo.DistanceKm(43.8, 11.1, 43.8, 11.1), 6);

    [Fact]
    public void Bounding_box_contains_points_within_the_radius()
    {
        var (minLat, maxLat, minLon, maxLon) = Geo.BoundingBox(43.8, 11.1, 10);
        // Circa 9 km a nord e circa 9 km a est (1 grado di longitudine ≈ 80 km a questa latitudine).
        Assert.InRange(43.8 + 9 / 111.32, minLat, maxLat);
        Assert.InRange(11.1 + 9 / 80.0, minLon, maxLon);
        Assert.True(43.8 + 12 / 111.32 > maxLat);
    }

    [Fact]
    public void Availability_mask_round_trips()
    {
        var slots = new[] { (DayOfWeek.Tuesday, DayBand.Evening), (DayOfWeek.Thursday, DayBand.Evening) };
        var mask = Availability.Mask(slots);

        Assert.Equal(2, Availability.Slots(mask).Count);
        Assert.Equal(mask, Availability.Mask(Availability.Slots(mask)));
        Assert.Contains((DayOfWeek.Tuesday, DayBand.Evening), Availability.Slots(mask));
    }
}
