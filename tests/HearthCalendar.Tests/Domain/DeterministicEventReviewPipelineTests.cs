using HearthCalendar.Shared.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class DeterministicEventReviewPipelineTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    [Fact]
    public Task Birthday_routes_to_events_as_yearly_reference()
    {
        var decision = Review("Adult B birthday on 25 July");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Anniversary_routes_to_events_as_yearly_reference()
    {
        var decision = Review("Family anniversary on 1 August");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Undated_reference_event_is_staged_for_review()
    {
        var decision = Review("Adult B birthday");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Child_responsibility_records_clear_responsible_adult()
    {
        var decision = Review("Child swimming with Adult B");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Family_event_routes_to_family_calendar_with_everyone()
    {
        var decision = Review("Family BBQ");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Personal_event_routes_to_named_person_calendar()
    {
        var decision = Review("Dentist for Adult A");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Ambiguous_input_is_staged_for_review()
    {
        var decision = Review("dentist");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Passive_past_non_reference_event_is_staged()
    {
        var intent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(new DateOnly(2026, 7, 28), new TimeOnly(10, 0), new TimeOnly(11, 0)),
            ReviewSourceMode.Passive);

        var decision = Pipeline().Review(intent);

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Interactive_past_non_reference_event_is_rejected()
    {
        var intent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(new DateOnly(2026, 7, 28), new TimeOnly(10, 0), new TimeOnly(11, 0)),
            ReviewSourceMode.Interactive);

        var decision = Pipeline().Review(intent);

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Past_birthday_is_allowed_as_yearly_reference_event()
    {
        var intent = Intent(
            "Adult B birthday on 25 July",
            new EventIntentPayload(new DateOnly(2026, 7, 25), null, null),
            ReviewSourceMode.Passive);

        var decision = Pipeline().Review(intent);

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Adult_and_family_overlap_produces_a_clash()
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

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Busy_event_overlapping_reference_event_does_not_clash()
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

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Child_event_does_not_self_clash_with_managed_responsibility_projection()
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

        return Verifier.Verify(new
        {
            Candidate = DescribeEvent(candidate),
            Clashes = clashes.Select(DescribeClash)
        });
    }

    [Fact]
    public Task Review_with_audit_returns_audit_entry_for_decision()
    {
        var intent = Intent("Family BBQ");
        var outcome = Pipeline().ReviewWithAudit(intent);

        return Verifier.Verify(new
        {
            Decision = DescribeDecision(outcome.Decision),
            AuditEntry = new
            {
                outcome.AuditEntry.Action,
                Actor = outcome.AuditEntry.Actor.Id,
                outcome.AuditEntry.OccurredAt,
                outcome.AuditEntry.Summary,
                HasIntentLink = outcome.AuditEntry.IntentId is not null,
                HasCalendarEventLink = outcome.AuditEntry.CalendarEventId is not null,
                HasReviewDecisionLink = outcome.AuditEntry.ReviewDecisionId is not null,
                outcome.AuditEntry.Metadata
            }
        });
    }

    [Fact]
    public Task Virtual_calendar_views_include_only_matching_approved_events()
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

        return Verifier.Verify(new
        {
            AdultA = adultAEvents.Select(DescribeEvent),
            Review = reviewEvents.Select(DescribeEvent)
        });
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

    private static object DescribeDecision(ReviewDecision decision) => new
    {
        Status = decision.Status.ToString(),
        Mode = decision.Mode.ToString(),
        Event = DescribeEvent(decision.Event),
        Reasons = decision.Reasons.Select(reason => new
        {
            Code = reason.Code.ToString(),
            reason.Message
        }).ToArray(),
        Clashes = decision.Clashes.Select(DescribeClash).ToArray(),
        DecidedAt = decision.DecidedAt.ToString("O"),
        DecidedBy = decision.DecidedBy.Id
    };

    private static object? DescribeEvent(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
        {
            return null;
        }

        return new
        {
            calendarEvent.Title,
            PrimaryCalendar = calendarEvent.PrimaryCalendar.ToString(),
            Category = calendarEvent.Category.ToString(),
            BusyStatus = calendarEvent.BusyStatus.ToString(),
            ReviewStatus = calendarEvent.ReviewStatus.ToString(),
            Time = new
            {
                Date = calendarEvent.Time.Date.ToString("O"),
                StartTime = calendarEvent.Time.StartTime?.ToString("HH:mm:ss"),
                EndTime = calendarEvent.Time.EndTime?.ToString("HH:mm:ss"),
                calendarEvent.Time.IsAllDay
            },
            Recurrence = calendarEvent.Recurrence?.Frequency.ToString(),
            Participants = calendarEvent.Participants.Select(participant => new
            {
                Person = participant.Person.DisplayName,
                PersonId = participant.Person.Id.Value,
                Role = participant.Role.ToString(),
                BusyStatus = participant.BusyStatus.ToString()
            }).ToArray(),
            ResponsibleAdult = calendarEvent.ResponsibleAdult is null
                ? null
                : new
                {
                    Adult = calendarEvent.ResponsibleAdult.Adult.DisplayName,
                    AdultId = calendarEvent.ResponsibleAdult.Adult.Id.Value,
                    Kind = calendarEvent.ResponsibleAdult.Kind.ToString(),
                    Source = calendarEvent.ResponsibleAdult.Source.ToString()
                },
            HasParentEvent = calendarEvent.ParentEventId is not null
        };
    }

    private static object DescribeClash(Clash clash) => new
    {
        Severity = clash.Severity.ToString(),
        clash.Summary,
        AffectedPeople = clash.AffectedPeople.Select(person => new
        {
            person.DisplayName,
            PersonId = person.Id.Value
        }).ToArray()
    };
}
