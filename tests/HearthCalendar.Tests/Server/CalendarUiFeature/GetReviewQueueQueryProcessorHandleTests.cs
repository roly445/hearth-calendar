using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class GetReviewQueueQueryProcessorHandleTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Review_queue_query_returns_staged_items_with_reasons_and_candidate()
    {
        var intent = Intent("Adult A dentist", new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var decision = StagedDecision(intent, CandidateEvent());
        var store = new RecordingStore();
        store.Intents.Add(intent);
        store.Decisions.Add(decision);
        store.Audits.Add(CalendarUiAudits.ForDecision(decision));

        var result = await new GetReviewQueueQueryProcessor(store).Handle(new GetReviewQueueQuery(), CancellationToken.None);

        Assert.Equal(QueryResultStatus.Succeeded, result.Status);
        await Verifier.Verify(result.Data);
    }
}
