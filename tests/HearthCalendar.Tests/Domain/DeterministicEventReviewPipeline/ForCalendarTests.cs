using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class ForCalendarTests : DeterministicEventReviewPipelineTestBase
{
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

    [Fact]
    public Task Virtual_calendar_views_use_injected_household_metadata_for_child_membership()
    {
        var child = new Person(new PersonId("child-a"), "Child A", PersonKind.Child);
        var guardian = new Person(new PersonId("adult-c"), "Adult C", PersonKind.Adult);
        var childEvent = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Child A swimming",
            new EventTime(Today, new TimeOnly(16, 0), new TimeOnly(17, 0), false),
            VirtualCalendar.AdultA,
            EventCategory.Responsibility,
            BusyStatus.Busy,
            [
                new Participant(child, ParticipationRole.Child, BusyStatus.Busy),
                new Participant(guardian, ParticipationRole.ResponsibleAdult, BusyStatus.Busy)
            ],
            CalendarSource.Test,
            responsibleAdult: new ResponsibleAdult(guardian, ResponsibilityKind.Attending, ResponsibilitySource.Inferred));
        var adultOnlyEvent = childEvent with
        {
            Id = CalendarEventId.New(),
            Title = "Adult C dentist",
            Participants = [new Participant(guardian, ParticipationRole.Attendee, BusyStatus.Busy)],
            ResponsibleAdult = null
        };
        var metadata = new StubHouseholdMetadata([guardian, child]);

        var childView = VirtualCalendarViews.ForCalendar(
            VirtualCalendar.Child,
            [childEvent, adultOnlyEvent],
            metadata);

        return Verifier.Verify(childView.Select(calendarEvent => new
        {
            calendarEvent.Title,
            Participants = calendarEvent.Participants.Select(participant => new
            {
                PersonId = participant.Person.Id.Value,
                Role = participant.Role.ToString()
            })
        }));
    }

    private sealed class StubHouseholdMetadata(IReadOnlyList<Person> people) : IHouseholdMetadata
    {
        public IReadOnlyList<HouseholdMember> Members =>
            people
                .Select(person => new HouseholdMember(new HouseholdMemberId(person.Id.Value), person.DisplayName, person.Kind.ToHouseholdMemberKind()))
                .ToArray();

        public IReadOnlyList<HouseholdRelationship> Relationships => [];

        public IReadOnlyList<Person> People => people;

        public Person? FindPerson(PersonId personId) =>
            people.FirstOrDefault(person => person.Id == personId);

        public Person? FindReferencedPerson(string normalizedText) =>
            people.FirstOrDefault(person => normalizedText.Contains(person.Id.Value.Replace("-", " ", StringComparison.Ordinal), StringComparison.Ordinal));

        public Person? FindReferencedAdult(string normalizedText) =>
            people.FirstOrDefault(person =>
                person.Kind == PersonKind.Adult &&
                normalizedText.Contains(person.Id.Value.Replace("-", " ", StringComparison.Ordinal), StringComparison.Ordinal));

        public Person? FindDefaultChild() =>
            people.FirstOrDefault(person => person.Kind == PersonKind.Child);

        public Person? FindDefaultPersonForCalendar(VirtualCalendar calendar) =>
            calendar == VirtualCalendar.Child ? FindDefaultChild() : null;

        public VirtualCalendar? FindPrimaryCalendarFor(Person person) =>
            IsChild(person) ? VirtualCalendar.Child : VirtualCalendar.AdultA;

        public bool IsFamilyParticipantSet(IReadOnlySet<PersonId> participantIds) =>
            people.All(person => participantIds.Contains(person.Id));

        public bool IsChildResponsibilitySet(IReadOnlySet<PersonId> participantIds, PersonId? responsibleAdultId) =>
            FindDefaultChild() is { } child &&
            responsibleAdultId is not null &&
            participantIds.Contains(child.Id) &&
            participantIds.Contains(responsibleAdultId.Value);

        public bool IsChild(Person person) =>
            person.Kind == PersonKind.Child;
    }
}

file static class PersonKindTestExtensions
{
    public static HouseholdMemberKind ToHouseholdMemberKind(this PersonKind kind) =>
        kind == PersonKind.Child ? HouseholdMemberKind.Child : HouseholdMemberKind.Adult;
}
