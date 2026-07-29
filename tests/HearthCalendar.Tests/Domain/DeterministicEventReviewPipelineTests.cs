using HearthCalendar.Shared.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class DeterministicEventReviewPipelineTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    [Fact]
    public void Birthday_routes_to_events_as_yearly_reference()
    {
        var decision = Review("Adult B birthday on 25 July");

        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.NotNull(decision.Event);
        Assert.Equal(VirtualCalendar.Events, decision.Event.PrimaryCalendar);
        Assert.Equal(EventCategory.Birthday, decision.Event.Category);
        Assert.Equal(BusyStatus.Free, decision.Event.BusyStatus);
        Assert.Equal(new DateOnly(2026, 7, 25), decision.Event.Time.Date);
        Assert.Equal(RecurrenceFrequency.Yearly, decision.Event.Recurrence?.Frequency);
        Assert.Contains(decision.Event.Participants, participant => participant.Person.Id == KnownPeople.AdultB.Id);
    }

    [Fact]
    public void Anniversary_routes_to_events_as_yearly_reference()
    {
        var decision = Review("Family anniversary on 1 August");

        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.NotNull(decision.Event);
        Assert.Equal(VirtualCalendar.Events, decision.Event.PrimaryCalendar);
        Assert.Equal(EventCategory.Anniversary, decision.Event.Category);
        Assert.Equal(BusyStatus.Free, decision.Event.BusyStatus);
        Assert.Equal(new DateOnly(2026, 8, 1), decision.Event.Time.Date);
        Assert.Equal(RecurrenceFrequency.Yearly, decision.Event.Recurrence?.Frequency);
    }

    [Fact]
    public void Undated_reference_event_is_staged_for_review()
    {
        var decision = Review("Adult B birthday");

        Assert.Equal(ReviewStatus.Staged, decision.Status);
        Assert.Null(decision.Event);
        Assert.Contains(decision.Reasons, reason => reason.Code == DecisionReasonCode.MissingDate);
    }

    [Fact]
    public void Child_responsibility_records_clear_responsible_adult()
    {
        var decision = Review("Child swimming with Adult B");

        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.NotNull(decision.Event);
        Assert.Equal(VirtualCalendar.Child, decision.Event.PrimaryCalendar);
        Assert.Equal(
            KnownPeople.Child.Id,
            Assert.Single(decision.Event.Participants, participant => participant.Role == ParticipationRole.Child).Person.Id);
        Assert.Equal(KnownPeople.AdultB.Id, decision.Event.ResponsibleAdult?.Adult.Id);
    }

    [Fact]
    public void Family_event_routes_to_family_calendar_with_everyone()
    {
        var decision = Review("Family BBQ");

        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.NotNull(decision.Event);
        Assert.Equal(VirtualCalendar.Family, decision.Event.PrimaryCalendar);
        Assert.Contains(decision.Event.Participants, participant => participant.Person.Id == KnownPeople.AdultA.Id);
        Assert.Contains(decision.Event.Participants, participant => participant.Person.Id == KnownPeople.AdultB.Id);
        Assert.Contains(decision.Event.Participants, participant => participant.Person.Id == KnownPeople.Child.Id);
    }

    [Fact]
    public void Personal_event_routes_to_named_person_calendar()
    {
        var decision = Review("Dentist for Adult A");

        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.NotNull(decision.Event);
        Assert.Equal(VirtualCalendar.AdultA, decision.Event.PrimaryCalendar);
        Assert.Contains(decision.Event.Participants, participant => participant.Person.Id == KnownPeople.AdultA.Id);
    }

    [Fact]
    public void Ambiguous_input_is_staged_for_review()
    {
        var decision = Review("dentist");

        Assert.Equal(ReviewStatus.Staged, decision.Status);
        Assert.Null(decision.Event);
        Assert.Contains(decision.Reasons, reason => reason.Code == DecisionReasonCode.AmbiguousIntent);
    }

    [Fact]
    public void Passive_past_non_reference_event_is_staged()
    {
        var intent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(new DateOnly(2026, 7, 28), new TimeOnly(10, 0), new TimeOnly(11, 0)),
            ReviewSourceMode.Passive);

        var decision = Pipeline().Review(intent);

        Assert.Equal(ReviewStatus.Staged, decision.Status);
        Assert.Contains(decision.Reasons, reason => reason.Code == DecisionReasonCode.PastEvent);
    }

    [Fact]
    public void Interactive_past_non_reference_event_is_rejected()
    {
        var intent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(new DateOnly(2026, 7, 28), new TimeOnly(10, 0), new TimeOnly(11, 0)),
            ReviewSourceMode.Interactive);

        var decision = Pipeline().Review(intent);

        Assert.Equal(ReviewStatus.Rejected, decision.Status);
        Assert.Contains(decision.Reasons, reason => reason.Code == DecisionReasonCode.PastEvent);
    }

    [Fact]
    public void Past_birthday_is_allowed_as_yearly_reference_event()
    {
        var intent = Intent(
            "Adult B birthday on 25 July",
            new EventIntentPayload(new DateOnly(2026, 7, 25), null, null),
            ReviewSourceMode.Passive);

        var decision = Pipeline().Review(intent);

        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.Equal(EventCategory.Birthday, decision.Event?.Category);
        Assert.Equal(RecurrenceFrequency.Yearly, decision.Event?.Recurrence?.Frequency);
    }

    [Fact]
    public void Adult_and_family_overlap_produces_a_clash()
    {
        var existing = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Family BBQ",
            new EventTime(Today, new TimeOnly(12, 0), new TimeOnly(14, 0), false),
            VirtualCalendar.Family,
            EventCategory.Family,
            BusyStatus.Busy,
            KnownPeople.All.Select(person => new Participant(person, ParticipationRole.Attendee, BusyStatus.Busy)).ToArray(),
            CalendarSource.Test);

        var intent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(Today, new TimeOnly(13, 0), new TimeOnly(13, 30)),
            ReviewSourceMode.Passive);

        var decision = Pipeline(existing).Review(intent);

        Assert.Equal(ReviewStatus.Staged, decision.Status);
        Assert.Single(decision.Clashes);
        Assert.Equal(ClashSeverity.Warning, decision.Clashes[0].Severity);
        Assert.Contains(KnownPeople.AdultA.Id, decision.Clashes[0].AffectedPeople.Select(person => person.Id));
    }

    [Fact]
    public void Busy_event_overlapping_reference_event_does_not_clash()
    {
        var existing = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Adult B birthday",
            new EventTime(Today, new TimeOnly(12, 0), new TimeOnly(14, 0), false),
            VirtualCalendar.Events,
            EventCategory.Birthday,
            BusyStatus.Free,
            [new Participant(KnownPeople.AdultB, ParticipationRole.Attendee, BusyStatus.Free)],
            CalendarSource.Test,
            new RecurrenceRule(RecurrenceFrequency.Yearly));
        var intent = Intent(
            "Dentist for Adult B",
            new EventIntentPayload(Today, new TimeOnly(13, 0), new TimeOnly(13, 30)),
            ReviewSourceMode.Passive);

        var decision = Pipeline(existing).Review(intent);

        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.Empty(decision.Clashes);
    }

    [Fact]
    public void Child_event_does_not_self_clash_with_managed_responsibility_projection()
    {
        var parentId = CalendarEventId.New();
        var candidate = CalendarEvent.Approved(
            parentId,
            "Child swimming",
            new EventTime(Today, new TimeOnly(15, 0), new TimeOnly(16, 0), false),
            VirtualCalendar.Child,
            EventCategory.Responsibility,
            BusyStatus.Busy,
            [
                new Participant(KnownPeople.Child, ParticipationRole.Child, BusyStatus.Busy),
                new Participant(KnownPeople.AdultB, ParticipationRole.ResponsibleAdult, BusyStatus.Busy)
            ],
            CalendarSource.Test,
            responsibleAdult: new ResponsibleAdult(KnownPeople.AdultB, ResponsibilityKind.Attending, ResponsibilitySource.Inferred));
        var projection = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Child swimming responsibility",
            candidate.Time,
            VirtualCalendar.AdultB,
            EventCategory.Responsibility,
            BusyStatus.Busy,
            [new Participant(KnownPeople.AdultB, ParticipationRole.ResponsibleAdult, BusyStatus.Busy)],
            CalendarSource.Test,
            parentEventId: parentId);

        var clashes = ClashDetector.FindClashes(candidate, [projection]);

        Assert.Empty(clashes);
    }

    [Fact]
    public void Review_with_audit_returns_audit_entry_for_decision()
    {
        var intent = Intent("Family BBQ");
        var outcome = Pipeline().ReviewWithAudit(intent);

        Assert.Equal(ReviewStatus.Approved, outcome.Decision.Status);
        Assert.Equal(AuditAction.EventApproved, outcome.AuditEntry.Action);
        Assert.Equal(intent.Id, outcome.AuditEntry.IntentId);
        Assert.Equal(outcome.Decision.Id, outcome.AuditEntry.ReviewDecisionId);
        Assert.Equal(outcome.Decision.Event?.Id, outcome.AuditEntry.CalendarEventId);
    }

    [Fact]
    public void Virtual_calendar_views_include_only_matching_approved_events()
    {
        var adultAEvent = Review("Dentist for Adult A").Event ?? throw new InvalidOperationException("Expected approved event.");
        var familyEvent = Review("Family BBQ").Event ?? throw new InvalidOperationException("Expected approved event.");
        var stagedEvent = familyEvent with { Id = CalendarEventId.New(), ReviewStatus = ReviewStatus.Staged };
        var rejectedEvent = adultAEvent with { Id = CalendarEventId.New(), ReviewStatus = ReviewStatus.Rejected };

        var adultAEvents = VirtualCalendarViews.ForCalendar(
            VirtualCalendar.AdultA,
            [adultAEvent, familyEvent, stagedEvent, rejectedEvent]);
        var reviewEvents = VirtualCalendarViews.ForCalendar(
            VirtualCalendar.Review,
            [adultAEvent, familyEvent, stagedEvent, rejectedEvent]);

        Assert.Equal([adultAEvent.Id, familyEvent.Id], adultAEvents.Select(calendarEvent => calendarEvent.Id));
        Assert.Equal([stagedEvent.Id], reviewEvents.Select(calendarEvent => calendarEvent.Id));
    }

    private static ReviewDecision Review(string text) => Pipeline().Review(Intent(text));

    private static DeterministicEventReviewPipeline Pipeline(params CalendarEvent[] existingEvents) =>
        new(Today, existingEvents);

    private static EventIntent Intent(
        string text,
        EventIntentPayload? payload = null,
        ReviewSourceMode sourceMode = ReviewSourceMode.Passive) =>
        new(
            EventIntentId.New(),
            CalendarSource.Test,
            sourceMode,
            text,
            payload,
            new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            ActorRef.System);
}
