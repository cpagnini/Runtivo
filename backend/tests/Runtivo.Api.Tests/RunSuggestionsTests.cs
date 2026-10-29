using Runtivo.Api.Matching;

namespace Runtivo.Api.Tests;

public class RunSuggestionsTests
{
    private static PublicPlace Place(string name, string city, double lat, double lon) => new() { Id = Guid.NewGuid(), Name = name, City = city, Latitude = lat, Longitude = lon };

    private static readonly PublicPlace PratoCentre = Place("Piazza del Comune", "Prato", 43.8790, 11.0963);
    private static readonly PublicPlace Cascine = Place("Parco delle Cascine", "Firenze", 43.7795, 11.2160);
    private static readonly PublicPlace Fortezza = Place("Fortezza da Basso", "Firenze", 43.7803, 11.2432);
    private static readonly PublicPlace SantaCroce = Place("Piazza Santa Croce", "Firenze", 43.7687, 11.2626);
    private static readonly PublicPlace[] All = [PratoCentre, Cascine, Fortezza, SantaCroce];

    private static double Worst(PublicPlace meeting, PublicPlace a, PublicPlace b) =>
        Math.Max(Geo.DistanceKm(meeting.Latitude, meeting.Longitude, a.Latitude, a.Longitude), Geo.DistanceKm(meeting.Latitude, meeting.Longitude, b.Latitude, b.Longitude));

    [Fact]
    public void Best_place_is_never_worse_for_the_farthest_person_than_meeting_at_either_home()
    {
        var best = RunSuggestions.BestMeetingPlace(All, PratoCentre, SantaCroce);

        Assert.True(Worst(best, PratoCentre, SantaCroce) <= Worst(PratoCentre, PratoCentre, SantaCroce));
        Assert.True(Worst(best, PratoCentre, SantaCroce) <= Worst(SantaCroce, PratoCentre, SantaCroce));
        Assert.Equal(All.Min(p => Worst(p, PratoCentre, SantaCroce)), Worst(best, PratoCentre, SantaCroce), 6);
    }

    [Fact]
    public void Two_people_in_the_same_place_meet_there() =>
        Assert.Equal(Cascine.Id, RunSuggestions.BestMeetingPlace(All, Cascine, Cascine).Id);

    [Fact]
    public void Next_slot_is_at_the_band_time_in_Rome()
    {
        // Mercoledì 4 novembre 2026; il martedì successivo (10 novembre) alle 19:00 italiane = 18:00Z (ora solare).
        var now = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        var slot = RunSuggestions.NextSharedSlot(Availability.SlotBit(DayOfWeek.Tuesday, DayBand.Evening), now);

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.Zero), slot);
    }

    [Fact]
    public void Next_slot_is_at_least_24_hours_ahead()
    {
        // Martedì 10 novembre alle 13:00 italiane: le 19:00 di oggi sono troppo vicine, si passa alla settimana dopo.
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var slot = RunSuggestions.NextSharedSlot(Availability.SlotBit(DayOfWeek.Tuesday, DayBand.Evening), now);

        Assert.Equal(new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero), slot);
    }

    [Fact]
    public void Next_slot_picks_the_earliest_of_several_shared_slots()
    {
        var now = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero); // lunedì
        var mask = Availability.Mask([(DayOfWeek.Saturday, DayBand.Evening), (DayOfWeek.Thursday, DayBand.Morning)]);

        // Giovedì 5 novembre, 08:00 italiane = 07:00Z.
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero), RunSuggestions.NextSharedSlot(mask, now));
    }

    [Fact]
    public void No_shared_slot_returns_null() =>
        Assert.Null(RunSuggestions.NextSharedSlot(0, new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)));
}
