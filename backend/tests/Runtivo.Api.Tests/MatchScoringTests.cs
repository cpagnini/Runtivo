using Runtivo.Api.Matching;

namespace Runtivo.Api.Tests;

public class MatchScoringTests
{
    private static readonly int TuesdayEvening = Availability.SlotBit(DayOfWeek.Tuesday, DayBand.Evening);
    private static readonly int ThursdayEvening = Availability.SlotBit(DayOfWeek.Thursday, DayBand.Evening);

    private static IntentProfile Profile(int paceMin = 330, int paceMax = 360, int distMin = 8000, int distMax = 10000,
        RunType type = RunType.Social, int? mask = null, int radius = 10) =>
        new(paceMin, paceMax, distMin, distMax, type, mask ?? (TuesdayEvening | ThursdayEvening), radius);

    [Fact]
    public void Identical_profiles_at_the_same_spot_score_100()
    {
        var score = MatchScoring.Score(Profile(), Profile(), 0);
        Assert.Equal(100, score!.Total);
    }

    [Fact]
    public void Is_symmetric()
    {
        var a = Profile(paceMin: 320, paceMax: 350, distMin: 6000, distMax: 9000, type: RunType.Easy, radius: 12);
        var b = Profile(paceMin: 340, paceMax: 380, distMin: 8000, distMax: 12000, type: RunType.Social, mask: TuesdayEvening, radius: 20);

        Assert.Equal(MatchScoring.Score(a, b, 4)!.Total, MatchScoring.Score(b, a, 4)!.Total);
    }

    [Fact]
    public void Pace_gap_over_60_seconds_never_matches()
    {
        var fast = Profile(paceMin: 300, paceMax: 310);
        var slow = Profile(paceMin: 390, paceMax: 400);

        Assert.Null(MatchScoring.Score(fast, slow, 0));
    }

    [Fact]
    public void Single_value_pace_ranges_do_not_break()
    {
        var a = Profile(paceMin: 330, paceMax: 330);
        var b = Profile(paceMin: 330, paceMax: 330);

        var score = MatchScoring.Score(a, b, 1);

        Assert.NotNull(score);
        Assert.True(score!.Total >= MatchScoring.MinTotal);
    }

    [Fact]
    public void No_shared_time_slot_never_matches()
    {
        var a = Profile(mask: TuesdayEvening);
        var b = Profile(mask: ThursdayEvening);

        Assert.Null(MatchScoring.Score(a, b, 0));
    }

    [Fact]
    public void Radius_uses_the_smaller_of_the_two()
    {
        var narrow = Profile(radius: 5);
        var wide = Profile(radius: 30);

        Assert.Null(MatchScoring.Score(narrow, wide, 10));
        Assert.NotNull(MatchScoring.Score(narrow, wide, 4));
    }

    [Fact]
    public void Reasons_are_in_italian_and_mention_shared_days()
    {
        var score = MatchScoring.Score(Profile(mask: TuesdayEvening), Profile(mask: TuesdayEvening), 3)!;

        Assert.Contains(score.Reasons, r => r.Contains("martedì", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(TuesdayEvening, score.SharedMask);
    }

    [Fact]
    public void Score_drops_as_distance_grows()
    {
        var near = MatchScoring.Score(Profile(), Profile(), 1)!.Total;
        var far = MatchScoring.Score(Profile(), Profile(), 9)!.Total;

        Assert.True(near > far);
    }

    [Fact]
    public void Different_run_type_scores_lower_than_same_type()
    {
        var same = MatchScoring.Score(Profile(type: RunType.Social), Profile(type: RunType.Social), 2)!.Total;
        var different = MatchScoring.Score(Profile(type: RunType.Social), Profile(type: RunType.Training), 2)!.Total;

        Assert.True(same > different);
    }

    [Fact]
    public void Totals_below_the_threshold_are_not_returned()
    {
        // Passo e distanza a malapena compatibili, un solo slot in comune su molti, ai limiti del raggio.
        var a = Profile(paceMin: 300, paceMax: 310, distMin: 3000, distMax: 4000, type: RunType.Easy,
            mask: Availability.Mask(Enumerable.Range(0, 7).Select(d => ((DayOfWeek)d, DayBand.Evening))), radius: 10);
        var b = Profile(paceMin: 360, paceMax: 370, distMin: 10000, distMax: 14000, type: RunType.Training,
            mask: TuesdayEvening, radius: 10);

        Assert.Null(MatchScoring.Score(a, b, 9.5));
    }
}
