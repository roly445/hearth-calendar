using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class ReviewTests : DeterministicEventReviewPipelineTestBase
{
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
    public Task Ambiguous_input_is_staged_for_review()
    {
        var decision = Review("dentist");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Anniversary_routes_to_events_as_yearly_reference()
    {
        var decision = Review("Family anniversary on 1 August");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Birthday_routes_to_events_as_yearly_reference()
    {
        var decision = Review("Adult B birthday on 25 July");

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
    public Task Personal_event_routes_to_named_person_calendar()
    {
        var decision = Review("Dentist for Adult A");

        return Verifier.Verify(DescribeDecision(decision));
    }

    [Fact]
    public Task Undated_reference_event_is_staged_for_review()
    {
        var decision = Review("Adult B birthday");

        return Verifier.Verify(DescribeDecision(decision));
    }
}
