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
}
