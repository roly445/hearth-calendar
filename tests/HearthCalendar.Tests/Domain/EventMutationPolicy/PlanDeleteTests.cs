using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class PlanDeleteTests : EventMutationPolicyTestBase
{
    [Fact]
    public void Ambiguous_delete_does_not_claim_success()
    {
        var first = AdultAEvent("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var second = AdultAEvent("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));

        var plan = EventMutationPolicy.PlanDelete(
            "Adult A dentist",
            Today,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30),
            [first, second]);

        Assert.Equal(MutationPlanStatus.Staged, plan.Status);
        Assert.Null(plan.MatchedEvent);
    }

    [Fact]
    public void Exact_delete_matches_one_approved_event()
    {
        var target = AdultAEvent("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));

        var plan = EventMutationPolicy.PlanDelete(
            "adult a dentist",
            Today,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30),
            [target]);

        Assert.Equal(MutationPlanStatus.Approved, plan.Status);
        Assert.Equal(target.Id, plan.MatchedEvent?.Id);
    }
}
