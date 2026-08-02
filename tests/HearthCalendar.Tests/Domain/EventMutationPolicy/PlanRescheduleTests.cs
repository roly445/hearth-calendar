using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class PlanRescheduleTests : EventMutationPolicyTestBase
{
    [Fact]
    public void Clashing_reschedule_is_staged()
    {
        var target = AdultAEvent("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var clash = AdultAEvent("Adult A appointment", Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(10, 30));

        var plan = EventMutationPolicy.PlanReschedule(
            "Adult A dentist",
            Today,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30),
            Today.AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(10, 30),
            [target],
            [target, clash]);

        Assert.Equal(MutationPlanStatus.Staged, plan.Status);
        Assert.NotEmpty(plan.Clashes);
    }

    [Fact]
    public void Confident_reschedule_updates_existing_event_shape()
    {
        var target = AdultAEvent("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));

        var plan = EventMutationPolicy.PlanReschedule(
            "Adult A dentist",
            Today,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30),
            Today.AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(10, 30),
            [target],
            [target]);

        Assert.Equal(MutationPlanStatus.Approved, plan.Status);
        Assert.Equal(target.Id, plan.RescheduledEvent?.Id);
        Assert.Equal(Today.AddDays(1), plan.RescheduledEvent?.Time.Date);
        Assert.Equal(new TimeOnly(10, 0), plan.RescheduledEvent?.Time.StartTime);
    }

    [Fact]
    public void Duplicate_looking_reschedule_is_rejected()
    {
        var target = AdultAEvent("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var duplicate = AdultAEvent("Adult A dentist", Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(10, 30));

        var plan = EventMutationPolicy.PlanReschedule(
            "Adult A dentist",
            Today,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30),
            Today.AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(10, 30),
            [target],
            [target, duplicate]);

        Assert.Equal(MutationPlanStatus.Rejected, plan.Status);
        Assert.Contains(plan.Reasons, reason => reason.Code == DecisionReasonCode.DuplicateEventMatch);
    }
}
