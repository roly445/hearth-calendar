using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public abstract class CalendarUiFeatureTestBase
{
    protected static readonly DateOnly Today = new(2026, 7, 30);


    protected static EventIntent Intent(string text, EventIntentPayload? payload = null) =>
        new(
            EventIntentId.New(),
            CalendarSource.Web,
            ReviewSourceMode.Interactive,
            text,
            payload,
            SubmittedAt(),
            ActorRef.System);

    protected static ReviewDecision StagedDecision(EventIntent intent, CalendarEvent? calendarEvent) =>
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

    protected static CalendarEvent CandidateEvent() =>
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

    protected static DateTimeOffset SubmittedAt() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    protected sealed class RecordingNotifier(RecordingStore store) : ICalendarUpdateNotifier
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

    protected sealed class RecordingStore : IHearthCalendarStore
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
