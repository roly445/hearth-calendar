using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class ReviewWithAuditAsyncTests : AiAssistedEventReviewPipelineTestBase
{
    [Fact]
    public async Task Deterministic_approval_does_not_call_ai_provider()
    {
        var outcome = await Pipeline(new ThrowingAiReviewProvider()).ReviewWithAuditAsync(Intent("Family BBQ"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Deterministic_clash_staging_wins_over_ai_approval_suggestion()
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
        var suggestion = Suggestion(
            title: "Dentist for Adult A",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.98m);
        var intent = Intent(
            "dentist",
            new EventIntentPayload(Today, new TimeOnly(13, 0), new TimeOnly(13, 30)));

        var outcome = await Pipeline(new StubAiReviewProvider(suggestion), existing).ReviewWithAuditAsync(intent);

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Deterministic_recurrence_safety_wins_over_ai_suggestion()
    {
        var suggestion = Suggestion(
            title: "Adult B birthday",
            calendar: VirtualCalendar.Events,
            participants: [KnownPeople.AdultB.Id],
            confidence: 0.98m,
            recurrence: new RecurrenceRule(RecurrenceFrequency.Yearly));

        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("Adult B birthday"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_for_projection_calendar_is_not_applied()
    {
        var suggestion = Suggestion(
            title: "Dentist",
            calendar: VirtualCalendar.Combined,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.95m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_resolves_allowed_ambiguity_when_safety_passes()
    {
        var suggestion = Suggestion(
            title: "Dentist for Adult A",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.92m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_with_calendar_participant_mismatch_is_not_applied()
    {
        var suggestion = Suggestion(
            title: "Dentist",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultB.Id],
            confidence: 0.95m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_with_unknown_participant_is_not_applied()
    {
        var suggestion = Suggestion(
            title: "Dentist",
            calendar: VirtualCalendar.AdultA,
            participants: [new PersonId("unknown-person")],
            confidence: 0.95m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Low_confidence_suggestion_is_recorded_but_not_applied_automatically()
    {
        var suggestion = Suggestion(
            title: "Dentist for Adult A",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.4m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task No_op_provider_leaves_deterministic_behavior_unchanged()
    {
        var intent = Intent("Dentist for Adult A");
        var outcome = await Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAuditAsync(intent);

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Provider_failure_degrades_to_deterministic_review()
    {
        var outcome = await Pipeline(new ThrowingAiReviewProvider()).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Provider_payload_uses_public_minimal_context_only()
    {
        var provider = new CapturingAiReviewProvider();
        var intent = Intent(
            "dentist",
            new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));

        await Pipeline(provider).ReviewWithAuditAsync(intent);

        await Verifier.Verify(new
        {
            Request = DescribeRequest(provider.Request ?? throw new InvalidOperationException("Expected captured request."))
        });
    }

    [Fact]
    public async Task Responsible_adult_suggestion_must_reference_an_adult()
    {
        var suggestion = Suggestion(
            title: "Child swimming",
            calendar: VirtualCalendar.Child,
            participants: [KnownPeople.Child.Id, KnownPeople.AdultB.Id],
            confidence: 0.95m,
            responsibleAdult: KnownPeople.Child.Id);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("swimming"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }
}
