using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Contracts.Ui;
using HearthCalendar.Shared.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class CalendarUiFeatureTests
{
    private static readonly DateOnly Today = new(2026, 7, 30);

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

    [Fact]
    public async Task Submit_command_persists_review_outcome_before_publishing_notifications()
    {
        var store = new RecordingStore();
        var notifier = new RecordingNotifier(store);
        var handler = new SubmitWebEventIntentCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<SubmitWebEventIntentCommand>>(),
            NullLogger<SubmitWebEventIntentCommandHandler>.Instance);

        var result = await handler.Handle(
            new SubmitWebEventIntentCommand("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.True(notifier.StoreHadPersistedDecisionWhenPublished);
        Assert.NotEmpty(notifier.Published);
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
    public async Task Edit_command_preserves_original_intent_and_creates_revised_intent()
    {
        var intent = Intent("Adult A dentist", new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var decision = StagedDecision(intent, CandidateEvent());
        var store = new RecordingStore();
        store.Intents.Add(intent);
        store.Decisions.Add(decision);
        var notifier = new RecordingNotifier(store);
        var handler = new EditReviewItemCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<EditReviewItemCommand>>(),
            NullLogger<EditReviewItemCommandHandler>.Instance);

        var result = await handler.Handle(
            new EditReviewItemCommand(decision.Id.Value, "Adult B dentist", Today, new TimeOnly(10, 0), new TimeOnly(10, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Contains(store.Intents, saved => saved.Id == intent.Id && saved.RawText == "Adult A dentist");
        Assert.Contains(store.Intents, saved => saved.Id != intent.Id && saved.RawText == "Adult B dentist");
        Assert.Contains(store.Intents, saved =>
            saved.Id != intent.Id &&
            saved.Payload?.Date == Today &&
            saved.Payload?.StartTime == new TimeOnly(10, 0) &&
            saved.Payload?.EndTime == new TimeOnly(10, 30));
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
                ReviewSourceMode.Passive),
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
    public async Task Admin_authorizer_requires_admin_scope()
    {
        var deniedAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var denied = await new ReviewQueueQueryAuthorizer(deniedAccessor).Authorize(new GetReviewQueueQuery(), CancellationToken.None);

        Assert.False(denied.IsAuthorized);
    }

    private static EventIntent Intent(string text, EventIntentPayload? payload = null) =>
        new(
            EventIntentId.New(),
            CalendarSource.Web,
            ReviewSourceMode.Interactive,
            text,
            payload,
            SubmittedAt(),
            ActorRef.System);

    private static ReviewDecision StagedDecision(EventIntent intent, CalendarEvent? calendarEvent) =>
        new(
            ReviewDecisionId.New(),
            intent.Id,
            ReviewStatus.Staged,
            DecisionMode.Automatic,
            [new DecisionReason(DecisionReasonCode.PastEvent, "Past non-reference events need confirmation.")],
            [],
            calendarEvent,
            SubmittedAt(),
            ActorRef.System);

    private static CalendarEvent CandidateEvent() =>
        CalendarEvent.Approved(
            CalendarEventId.New(),
            "Adult A dentist",
            new EventTime(Today, new TimeOnly(9, 0), new TimeOnly(9, 30), false),
            VirtualCalendar.AdultA,
            EventCategory.Personal,
            BusyStatus.Busy,
            [new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy)],
            CalendarSource.Web) with
        {
            ReviewStatus = ReviewStatus.Staged
        };

    private static DateTimeOffset SubmittedAt() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private sealed class RecordingNotifier(RecordingStore store) : ICalendarUpdateNotifier
    {
        public List<CalendarUpdateNotification> Published { get; } = [];

        public bool StoreHadPersistedDecisionWhenPublished { get; private set; }

        public Task PublishAsync(
            IReadOnlyList<CalendarUpdateNotification> notifications,
            CancellationToken cancellationToken)
        {
            StoreHadPersistedDecisionWhenPublished = store.Decisions.Count > 0;
            Published.AddRange(notifications);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStore : IHearthCalendarStore
    {
        public List<EventIntent> Intents { get; } = [];

        public List<ReviewDecision> Decisions { get; } = [];

        public List<CalendarEvent> ApprovedEvents { get; } = [];

        public List<AuditEntry> Audits { get; } = [];

        public bool ThrowStaleOnDecisionWrite { get; init; }

        public bool ThrowStaleOnApprovedEventMutation { get; init; }

        public Task StoreIntentAsync(EventIntent intent, CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            return Task.CompletedTask;
        }

        public Task StoreIntentWithAuditAsync(
            EventIntent intent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            Audits.Add(auditEntry);
            return Task.CompletedTask;
        }

        public Task<CalDavObjectUpsertResult> UpsertCalDavObjectAsync(
            CalDavObjectUpsert upsert,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EventIntent?> LoadIntentAsync(EventIntentId id, CancellationToken cancellationToken) =>
            Task.FromResult(Intents.SingleOrDefault(intent => intent.Id == id));

        public Task StoreReviewOutcomeAsync(
            EventIntent intent,
            ReviewOutcome outcome,
            CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            Decisions.Add(outcome.Decision);
            Audits.Add(outcome.AuditEntry);
            if (outcome.Decision.Event?.ReviewStatus == ReviewStatus.Approved)
            {
                ApprovedEvents.Add(outcome.Decision.Event);
            }

            return Task.CompletedTask;
        }

        public Task StoreAuditEntryAsync(AuditEntry auditEntry, CancellationToken cancellationToken)
        {
            Audits.Add(auditEntry);
            return Task.CompletedTask;
        }

        public Task<ReviewOutcome?> LoadReviewOutcomeAsync(
            ReviewDecisionId id,
            CancellationToken cancellationToken)
        {
            var decision = Decisions.SingleOrDefault(candidate => candidate.Id == id);
            var audit = Audits.FirstOrDefault(candidate => candidate.ReviewDecisionId == id);

            return Task.FromResult(decision is null || audit is null ? null : new ReviewOutcome(decision, audit));
        }

        public Task<ReviewDecision?> LoadReviewDecisionAsync(
            ReviewDecisionId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Decisions.SingleOrDefault(candidate => candidate.Id == id));

        public Task StoreReviewDecisionAsync(
            ReviewDecision decision,
            AuditEntry auditEntry,
            CancellationToken cancellationToken)
        {
            if (ThrowStaleOnDecisionWrite)
            {
                throw new StaleReviewDecisionException(decision.Id);
            }

            Decisions.RemoveAll(candidate => candidate.Id == decision.Id);
            Decisions.Add(decision);
            Audits.Add(auditEntry);
            if (decision.Event?.ReviewStatus == ReviewStatus.Approved)
            {
                ApprovedEvents.Add(decision.Event);
            }

            return Task.CompletedTask;
        }

        public Task StoreEditedReviewOutcomeAsync(
            ReviewDecision originalDecision,
            EventIntent revisedIntent,
            ReviewOutcome revisedOutcome,
            CancellationToken cancellationToken)
        {
            if (ThrowStaleOnDecisionWrite)
            {
                throw new StaleReviewDecisionException(originalDecision.Id);
            }

            Decisions.RemoveAll(candidate => candidate.Id == originalDecision.Id);
            Decisions.Add(originalDecision with { Status = ReviewStatus.Rejected });
            Intents.Add(revisedIntent);
            Decisions.Add(revisedOutcome.Decision);
            Audits.Add(revisedOutcome.AuditEntry);

            return Task.CompletedTask;
        }

        public Task DeleteApprovedEventAsync(
            CalendarEvent calendarEvent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken)
        {
            if (ThrowStaleOnApprovedEventMutation)
            {
                throw new StaleApprovedEventMutationException(calendarEvent.Id);
            }

            ApprovedEvents.RemoveAll(candidate => candidate.Id == calendarEvent.Id);
            Audits.Add(auditEntry);

            return Task.CompletedTask;
        }

        public Task RescheduleApprovedEventAsync(
            CalendarEvent originalEvent,
            CalendarEvent rescheduledEvent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken)
        {
            if (ThrowStaleOnApprovedEventMutation)
            {
                throw new StaleApprovedEventMutationException(originalEvent.Id);
            }

            ApprovedEvents.RemoveAll(candidate => candidate.Id == originalEvent.Id);
            ApprovedEvents.Add(rescheduledEvent);
            Audits.Add(auditEntry);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CalendarEvent>> QueryApprovedEventsAsync(
            DateOnly from,
            DateOnly to,
            VirtualCalendar calendar,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEvent>>(ApprovedEvents
                .Where(calendarEvent => calendarEvent.Time.Date >= from && calendarEvent.Time.Date <= to)
                .ToArray());

        public Task<CalendarEvent?> LoadApprovedEventAsync(
            CalendarEventId id,
            VirtualCalendar calendar,
            CancellationToken cancellationToken) =>
            Task.FromResult(ApprovedEvents.SingleOrDefault(calendarEvent => calendarEvent.Id == id));

        public Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReviewDecision>>(Decisions
                .Where(decision => decision.Status == ReviewStatus.Staged)
                .ToArray());

        public Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Audits);
    }
}
