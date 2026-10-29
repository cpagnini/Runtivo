using Runtivo.Api.Matching;

namespace Runtivo.Api.Tests;

public class RunRulesTests
{
    private static ParticipantState P(ParticipantStatus status, bool allowsPairs = true) => new(Guid.NewGuid(), status, allowsPairs);

    [Fact]
    public void Two_confirmed_pair_who_allow_pairs_is_confirmed()
    {
        var outcome = RunRules.Resolve([P(ParticipantStatus.Confirmed), P(ParticipantStatus.Confirmed)]);
        Assert.Equal(RunStatus.Confirmed, outcome.Status);
        Assert.Null(outcome.CancelReason);
    }

    [Fact]
    public void Pending_participant_keeps_the_run_proposed()
    {
        var outcome = RunRules.Resolve([P(ParticipantStatus.Confirmed), P(ParticipantStatus.Pending)]);
        Assert.Equal(RunStatus.Proposed, outcome.Status);
    }

    [Fact]
    public void Declined_participants_are_ignored_when_everyone_else_confirmed()
    {
        var outcome = RunRules.Resolve([P(ParticipantStatus.Confirmed), P(ParticipantStatus.Confirmed), P(ParticipantStatus.Declined)]);
        Assert.Equal(RunStatus.Confirmed, outcome.Status);
    }

    [Fact]
    public void Fewer_than_two_active_participants_cancels_with_a_reason()
    {
        var outcome = RunRules.Resolve([P(ParticipantStatus.Confirmed), P(ParticipantStatus.Left), P(ParticipantStatus.Declined)]);
        Assert.Equal(RunStatus.Cancelled, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.CancelReason));
    }

    [Fact]
    public void Pair_with_someone_who_disallows_pairs_is_cancelled()
    {
        var outcome = RunRules.Resolve([P(ParticipantStatus.Confirmed), P(ParticipantStatus.Confirmed, allowsPairs: false), P(ParticipantStatus.Left)]);
        Assert.Equal(RunStatus.Cancelled, outcome.Status);
        Assert.Contains("3", outcome.CancelReason);
    }

    [Fact]
    public void Group_of_three_ignores_the_pair_preference()
    {
        var outcome = RunRules.Resolve([P(ParticipantStatus.Confirmed, false), P(ParticipantStatus.Confirmed, false), P(ParticipantStatus.Confirmed, false)]);
        Assert.Equal(RunStatus.Confirmed, outcome.Status);
    }

    [Fact]
    public void Cancelled_stays_cancelled()
    {
        var outcome = RunRules.Resolve([P(ParticipantStatus.Confirmed), P(ParticipantStatus.Confirmed)], alreadyCancelled: true);
        Assert.Equal(RunStatus.Cancelled, outcome.Status);
    }
}
