using HearthCalendar.Shared.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class EventMutationPolicyTests
{
    private static readonly DateOnly Today = new(2026, 7, 30);

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

    private static CalendarEvent AdultAEvent(
        string title,
        DateOnly date,
        TimeOnly? startTime,
        TimeOnly? endTime) =>
        CalendarEvent.Approved(
            CalendarEventId.New(),
            title,
            new EventTime(date, startTime, endTime, startTime is null && endTime is null),
            VirtualCalendar.AdultA,
            EventCategory.Personal,
            BusyStatus.Busy,
            [new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy)],
            CalendarSource.Test);
}
