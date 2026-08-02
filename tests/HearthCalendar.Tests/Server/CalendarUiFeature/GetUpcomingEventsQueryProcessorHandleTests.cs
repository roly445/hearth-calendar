using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class GetUpcomingEventsQueryProcessorHandleTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Upcoming_events_query_returns_approved_items_only()
    {
        var store = new RecordingStore();
        store.ApprovedEvents.Add(CandidateEvent() with { ReviewStatus = ReviewStatus.Approved });

        var result = await new GetUpcomingEventsQueryProcessor(store).Handle(
            new GetUpcomingEventsQuery(Today, Today.AddDays(7)),
            CancellationToken.None);

        Assert.Equal(QueryResultStatus.Succeeded, result.Status);
        Assert.Single(result.Data.Items);
        Assert.Equal("Adult A dentist", result.Data.Items[0].Title);
    }
}
