using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class FindClashesTests : DeterministicEventReviewPipelineTestBase
{
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
}
