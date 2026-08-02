using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class RescheduleEventCommandHandlerHandleTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Passive_clashing_reschedule_is_staged_without_mutating_approved_events()
    {
        var approved = CandidateEvent() with { ReviewStatus = ReviewStatus.Approved };
        var clash = CandidateEvent() with
        {
            Id = CalendarEventId.New(),
            Title = "Adult A appointment",
            ReviewStatus = ReviewStatus.Approved,
            Time = new EventTime(Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(10, 30), false)
        };
        var store = new RecordingStore();
        store.ApprovedEvents.AddRange([approved, clash]);
        var notifier = new RecordingNotifier(store);
        var handler = new RescheduleEventCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<RescheduleEventCommand>>(),
            NullLogger<RescheduleEventCommandHandler>.Instance);

        var result = await handler.Handle(
            new RescheduleEventCommand(
                "Adult A dentist",
                Today,
                Today.AddDays(1),
                new TimeOnly(9, 0),
                new TimeOnly(9, 30),
                new TimeOnly(10, 0),
                new TimeOnly(10, 30),
                ReviewSourceMode.Passive.ToString()),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("Staged", result.Data.Status);
        Assert.Equal(2, store.ApprovedEvents.Count);
        Assert.Contains(store.Decisions, decision => decision.Status == ReviewStatus.Staged);
        Assert.Contains(notifier.Published, notification => notification.Type == CalendarUiNotifications.ReviewQueueChanged);
    }

    [Fact]
    public async Task Reschedule_command_returns_failed_when_store_detects_stale_event_match()
    {
        var approved = CandidateEvent() with { ReviewStatus = ReviewStatus.Approved };
        var store = new RecordingStore { ThrowStaleOnApprovedEventMutation = true };
        store.ApprovedEvents.Add(approved);
        var notifier = new RecordingNotifier(store);
        var handler = new RescheduleEventCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<RescheduleEventCommand>>(),
            NullLogger<RescheduleEventCommandHandler>.Instance);

        var result = await handler.Handle(
            new RescheduleEventCommand(
                "Adult A dentist",
                Today,
                Today.AddDays(1),
                new TimeOnly(9, 0),
                new TimeOnly(9, 30),
                new TimeOnly(10, 0),
                new TimeOnly(10, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Failed, result.Status);
        var unchanged = Assert.Single(store.ApprovedEvents);
        Assert.Equal(Today, unchanged.Time.Date);
        Assert.Contains(store.Audits, audit => audit.Action == AuditAction.EventRescheduleRejected);
        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task Reschedule_command_updates_exact_match_and_writes_audit()
    {
        var approved = CandidateEvent() with { ReviewStatus = ReviewStatus.Approved };
        var store = new RecordingStore();
        store.ApprovedEvents.Add(approved);
        var notifier = new RecordingNotifier(store);
        var handler = new RescheduleEventCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<RescheduleEventCommand>>(),
            NullLogger<RescheduleEventCommandHandler>.Instance);

        var result = await handler.Handle(
            new RescheduleEventCommand(
                "Adult A dentist",
                Today,
                Today.AddDays(1),
                new TimeOnly(9, 0),
                new TimeOnly(9, 30),
                new TimeOnly(10, 0),
                new TimeOnly(10, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        var rescheduled = Assert.Single(store.ApprovedEvents);
        Assert.Equal(approved.Id, rescheduled.Id);
        Assert.Equal(Today.AddDays(1), rescheduled.Time.Date);
        Assert.Contains(store.Audits, audit => audit.Action == AuditAction.EventRescheduled);
    }
}
