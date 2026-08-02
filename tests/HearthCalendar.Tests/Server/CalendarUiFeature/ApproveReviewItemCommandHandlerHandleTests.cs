using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class ApproveReviewItemCommandHandlerHandleTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Approve_command_rejects_non_staged_decision_without_mutation()
    {
        var intent = Intent("Adult A dentist", new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var approved = StagedDecision(intent, CandidateEvent()) with { Status = ReviewStatus.Approved };
        var store = new RecordingStore();
        store.Decisions.Add(approved);
        var notifier = new RecordingNotifier(store);

        var result = await new ApproveReviewItemCommandHandler(store, notifier).Handle(
            new ApproveReviewItemCommand(approved.Id.Value),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Failed, result.Status);
        Assert.Single(store.Decisions);
        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task Approve_command_returns_failed_when_store_detects_stale_decision()
    {
        var intent = Intent("Adult A dentist", new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var decision = StagedDecision(intent, CandidateEvent());
        var store = new RecordingStore { ThrowStaleOnDecisionWrite = true };
        store.Decisions.Add(decision);
        var notifier = new RecordingNotifier(store);

        var result = await new ApproveReviewItemCommandHandler(store, notifier).Handle(
            new ApproveReviewItemCommand(decision.Id.Value),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Failed, result.Status);
        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task Approve_command_updates_decision_and_publishes_calendar_change()
    {
        var intent = Intent("Adult A dentist", new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var decision = StagedDecision(intent, CandidateEvent());
        var store = new RecordingStore();
        store.Decisions.Add(decision);
        var notifier = new RecordingNotifier(store);

        var result = await new ApproveReviewItemCommandHandler(store, notifier).Handle(
            new ApproveReviewItemCommand(decision.Id.Value),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Contains(store.Decisions, saved => saved.Id == decision.Id && saved.Status == ReviewStatus.Approved);
        Assert.Contains(notifier.Published, notification => notification.Type == CalendarUiNotifications.CalendarEventsChanged);
    }
}
