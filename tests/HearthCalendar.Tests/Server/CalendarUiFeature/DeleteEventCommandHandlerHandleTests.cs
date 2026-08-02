using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class DeleteEventCommandHandlerHandleTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Ambiguous_delete_fails_without_removing_event()
    {
        var first = CandidateEvent() with { ReviewStatus = ReviewStatus.Approved };
        var second = CandidateEvent() with { ReviewStatus = ReviewStatus.Approved, Id = CalendarEventId.New() };
        var store = new RecordingStore();
        store.ApprovedEvents.AddRange([first, second]);
        var notifier = new RecordingNotifier(store);
        var handler = new DeleteEventCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<DeleteEventCommand>>(),
            NullLogger<DeleteEventCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeleteEventCommand("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Failed, result.Status);
        Assert.Equal(2, store.ApprovedEvents.Count);
        Assert.Contains(store.Audits, audit => audit.Action == AuditAction.EventDeleteRejected);
        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task Delete_command_removes_exact_match_and_writes_audit()
    {
        var approved = CandidateEvent() with { ReviewStatus = ReviewStatus.Approved };
        var store = new RecordingStore();
        store.ApprovedEvents.Add(approved);
        var notifier = new RecordingNotifier(store);
        var handler = new DeleteEventCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<DeleteEventCommand>>(),
            NullLogger<DeleteEventCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeleteEventCommand("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Empty(store.ApprovedEvents);
        Assert.Contains(store.Audits, audit => audit.Action == AuditAction.EventDeleted);
        Assert.Contains(notifier.Published, notification => notification.Type == CalendarUiNotifications.CalendarEventsChanged);
    }

    [Fact]
    public async Task Delete_command_returns_failed_when_store_detects_stale_event_match()
    {
        var approved = CandidateEvent() with { ReviewStatus = ReviewStatus.Approved };
        var store = new RecordingStore { ThrowStaleOnApprovedEventMutation = true };
        store.ApprovedEvents.Add(approved);
        var notifier = new RecordingNotifier(store);
        var handler = new DeleteEventCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<DeleteEventCommand>>(),
            NullLogger<DeleteEventCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeleteEventCommand("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Failed, result.Status);
        Assert.Single(store.ApprovedEvents);
        Assert.Contains(store.Audits, audit => audit.Action == AuditAction.EventDeleteRejected);
        Assert.Empty(notifier.Published);
    }
}
