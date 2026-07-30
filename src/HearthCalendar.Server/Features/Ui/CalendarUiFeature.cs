using System.Security.Claims;
using BluQube.Authorization;
using BluQube.Commands;
using BluQube.Queries;
using FluentValidation;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.SignalR;
using HearthCalendar.Shared.Contracts.Ui;
using HearthCalendar.Shared.Domain;
using Microsoft.AspNetCore.SignalR;

namespace HearthCalendar.Server.Features.Ui;

public sealed class GetReviewQueueQueryProcessor(IHearthCalendarStore store)
    : IQueryProcessor<GetReviewQueueQuery, ReviewQueueResult>
{
    public async ValueTask<QueryResult<ReviewQueueResult>> Handle(
        GetReviewQueueQuery request,
        CancellationToken cancellationToken)
    {
        var decisions = await store.QueryReviewQueueAsync(cancellationToken);
        var items = new List<ReviewQueueItemDto>();

        foreach (var decision in decisions)
        {
            var intent = await store.LoadIntentAsync(decision.IntentId, cancellationToken);
            var outcome = await store.LoadReviewOutcomeAsync(decision.Id, cancellationToken);
            if (intent is null)
            {
                continue;
            }

            items.Add(CalendarUiMapping.ToReviewQueueItem(intent, outcome?.Decision ?? decision, outcome?.AiSuggestion));
        }

        return items.Count == 0
            ? QueryResult<ReviewQueueResult>.Empty()
            : QueryResult<ReviewQueueResult>.Succeeded(new ReviewQueueResult(items));
    }
}

public sealed class GetUpcomingEventsQueryProcessor(IHearthCalendarStore store)
    : IQueryProcessor<GetUpcomingEventsQuery, UpcomingEventsResult>
{
    public async ValueTask<QueryResult<UpcomingEventsResult>> Handle(
        GetUpcomingEventsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.To < request.From)
        {
            return QueryResult<UpcomingEventsResult>.Failed();
        }

        var events = await store.QueryApprovedEventsAsync(
            request.From,
            request.To,
            request.Calendar,
            cancellationToken);
        var items = events.Select(CalendarUiMapping.ToCalendarEventSummary).ToArray();

        return items.Length == 0
            ? QueryResult<UpcomingEventsResult>.Empty()
            : QueryResult<UpcomingEventsResult>.Succeeded(new UpcomingEventsResult(items));
    }
}

public sealed class SubmitWebEventIntentCommandValidator
    : AbstractValidator<SubmitWebEventIntentCommand>
{
    public SubmitWebEventIntentCommandValidator()
    {
        RuleFor(command => command.RawText)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class SubmitWebEventIntentCommandHandler(
    IHearthCalendarStore store,
    ICalendarUpdateNotifier notifier,
    IEnumerable<IValidator<SubmitWebEventIntentCommand>> validators,
    ILogger<SubmitWebEventIntentCommandHandler> logger)
    : CommandHandler<SubmitWebEventIntentCommand, ReviewActionResult>(validators, logger)
{
    protected override async Task<CommandResult<ReviewActionResult>> HandleInternal(
        SubmitWebEventIntentCommand request,
        CancellationToken cancellationToken)
    {
        var intent = new EventIntent(
            EventIntentId.New(),
            CalendarSource.Web,
            ReviewSourceMode.Interactive,
            request.RawText.Trim(),
            new EventIntentPayload(request.Date, request.StartTime, request.EndTime),
            DateTimeOffset.UtcNow,
            ActorRef.System);
        var existingEvents = request.Date is null
            ? []
            : await store.QueryApprovedEventsAsync(
                request.Date.Value,
                request.Date.Value,
                VirtualCalendar.Combined,
                cancellationToken);
        var outcome = await new DeterministicEventReviewPipeline(
                DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
                existingEvents)
            .ReviewWithAuditAsync(intent, cancellationToken);

        await store.StoreReviewOutcomeAsync(intent, outcome, cancellationToken);
        await notifier.PublishAsync(
            CalendarUiNotifications.For(outcome.Decision),
            cancellationToken);

        return CommandResult<ReviewActionResult>.Succeeded(CalendarUiMapping.ToReviewActionResult(outcome.Decision));
    }
}

public sealed class ApproveReviewItemCommandHandler(
    IHearthCalendarStore store,
    ICalendarUpdateNotifier notifier)
    : ICommandHandler<ApproveReviewItemCommand, ReviewActionResult>
{
    public async ValueTask<CommandResult<ReviewActionResult>> Handle(
        ApproveReviewItemCommand request,
        CancellationToken cancellationToken)
    {
        var decision = await store.LoadReviewDecisionAsync(
            new ReviewDecisionId(request.ReviewDecisionId),
            cancellationToken);
        if (decision is null)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("REVIEW_ITEM_NOT_FOUND", "The staged item was not found."));
        }

        if (decision.Status != ReviewStatus.Staged)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("REVIEW_ITEM_NOT_STAGED", "Only staged review items can be changed."));
        }

        if (decision.Event is null)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("REVIEW_ITEM_HAS_NO_CANDIDATE", "The staged item cannot be approved without an event candidate."));
        }

        var approvedDecision = decision with
        {
            Status = ReviewStatus.Approved,
            Event = decision.Event with { ReviewStatus = ReviewStatus.Approved },
            DecidedAt = DateTimeOffset.UtcNow,
            DecidedBy = ActorRef.System
        };
        var audit = CalendarUiAudits.ForDecision(approvedDecision);

        try
        {
            await store.StoreReviewDecisionAsync(approvedDecision, audit, cancellationToken);
        }
        catch (StaleReviewDecisionException)
        {
            return CommandResult<ReviewActionResult>.Failed(CalendarUiErrors.StaleReviewItemError());
        }

        await notifier.PublishAsync(CalendarUiNotifications.For(approvedDecision), cancellationToken);

        return CommandResult<ReviewActionResult>.Succeeded(CalendarUiMapping.ToReviewActionResult(approvedDecision));
    }
}

public sealed class RejectReviewItemCommandHandler(
    IHearthCalendarStore store,
    ICalendarUpdateNotifier notifier)
    : ICommandHandler<RejectReviewItemCommand, ReviewActionResult>
{
    public async ValueTask<CommandResult<ReviewActionResult>> Handle(
        RejectReviewItemCommand request,
        CancellationToken cancellationToken)
    {
        var decision = await store.LoadReviewDecisionAsync(
            new ReviewDecisionId(request.ReviewDecisionId),
            cancellationToken);
        if (decision is null)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("REVIEW_ITEM_NOT_FOUND", "The staged item was not found."));
        }

        if (decision.Status != ReviewStatus.Staged)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("REVIEW_ITEM_NOT_STAGED", "Only staged review items can be changed."));
        }

        var rejectedDecision = decision with
        {
            Status = ReviewStatus.Rejected,
            Event = decision.Event is null ? null : decision.Event with { ReviewStatus = ReviewStatus.Rejected },
            DecidedAt = DateTimeOffset.UtcNow,
            DecidedBy = ActorRef.System
        };
        var audit = CalendarUiAudits.ForDecision(rejectedDecision);

        try
        {
            await store.StoreReviewDecisionAsync(rejectedDecision, audit, cancellationToken);
        }
        catch (StaleReviewDecisionException)
        {
            return CommandResult<ReviewActionResult>.Failed(CalendarUiErrors.StaleReviewItemError());
        }

        await notifier.PublishAsync(CalendarUiNotifications.For(rejectedDecision), cancellationToken);

        return CommandResult<ReviewActionResult>.Succeeded(CalendarUiMapping.ToReviewActionResult(rejectedDecision));
    }
}

public sealed class EditReviewItemCommandValidator : AbstractValidator<EditReviewItemCommand>
{
    public EditReviewItemCommandValidator()
    {
        RuleFor(command => command.ReviewDecisionId).NotEmpty();
        RuleFor(command => command.RawText)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class EditReviewItemCommandHandler(
    IHearthCalendarStore store,
    ICalendarUpdateNotifier notifier,
    IEnumerable<IValidator<EditReviewItemCommand>> validators,
    ILogger<EditReviewItemCommandHandler> logger)
    : CommandHandler<EditReviewItemCommand, ReviewActionResult>(validators, logger)
{
    protected override async Task<CommandResult<ReviewActionResult>> HandleInternal(
        EditReviewItemCommand request,
        CancellationToken cancellationToken)
    {
        var originalDecision = await store.LoadReviewDecisionAsync(
            new ReviewDecisionId(request.ReviewDecisionId),
            cancellationToken);
        if (originalDecision is null)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("REVIEW_ITEM_NOT_FOUND", "The staged item was not found."));
        }

        if (originalDecision.Status != ReviewStatus.Staged)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("REVIEW_ITEM_NOT_STAGED", "Only staged review items can be changed."));
        }

        var originalIntent = await store.LoadIntentAsync(originalDecision.IntentId, cancellationToken);
        if (originalIntent is null)
        {
            return CommandResult<ReviewActionResult>.Failed(
                new BluQubeErrorData("INTENT_NOT_FOUND", "The original intent was not found."));
        }

        var revisedIntent = originalIntent with
        {
            Id = EventIntentId.New(),
            RawText = request.RawText.Trim(),
            Payload = new EventIntentPayload(request.Date, request.StartTime, request.EndTime),
            SubmittedAt = DateTimeOffset.UtcNow
        };
        var existingEvents = request.Date is null
            ? []
            : await store.QueryApprovedEventsAsync(
                request.Date.Value,
                request.Date.Value,
                VirtualCalendar.Combined,
                cancellationToken);
        var outcome = await new DeterministicEventReviewPipeline(
                DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
                existingEvents)
            .ReviewWithAuditAsync(revisedIntent, cancellationToken);

        try
        {
            await store.StoreEditedReviewOutcomeAsync(originalDecision, revisedIntent, outcome, cancellationToken);
        }
        catch (StaleReviewDecisionException)
        {
            return CommandResult<ReviewActionResult>.Failed(CalendarUiErrors.StaleReviewItemError());
        }

        await notifier.PublishAsync(CalendarUiNotifications.For(outcome.Decision), cancellationToken);

        return CommandResult<ReviewActionResult>.Succeeded(CalendarUiMapping.ToReviewActionResult(outcome.Decision));
    }
}

public abstract class AdminBluQubeAuthorizer<TRequest>(IHttpContextAccessor accessor)
    : IBluQubeAuthorizer<TRequest>
{
    public Task<AuthorizationResult> Authorize(TRequest request, CancellationToken cancellationToken)
    {
        var user = accessor.HttpContext?.User;
        var allowed = user?.HasClaim(HearthCalendarAuth.ScopeClaim, HearthCalendarAuth.AdminWebScope) == true;

        return Task.FromResult(
            allowed
                ? AuthorizationResult.Succeed()
                : AuthorizationResult.Fail("Admin access is required."));
    }
}

public sealed class ReviewQueueQueryAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<GetReviewQueueQuery>(accessor);

public sealed class UpcomingEventsQueryAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<GetUpcomingEventsQuery>(accessor);

public sealed class SubmitWebEventIntentCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<SubmitWebEventIntentCommand>(accessor);

public sealed class ApproveReviewItemCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<ApproveReviewItemCommand>(accessor);

public sealed class RejectReviewItemCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<RejectReviewItemCommand>(accessor);

public sealed class EditReviewItemCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<EditReviewItemCommand>(accessor);

public static class CalendarUiMapping
{
    public static ReviewQueueItemDto ToReviewQueueItem(
        EventIntent intent,
        ReviewDecision decision,
        AiReviewSuggestion? suggestion) =>
        new(
            decision.Id.Value,
            intent.Id.Value,
            intent.RawText,
            decision.Status.ToString(),
            decision.Mode.ToString(),
            intent.Source.ToString(),
            intent.SubmittedBy.Id,
            intent.SubmittedAt,
            decision.Event is null ? null : ToCalendarEventSummary(decision.Event),
            decision.Reasons.Select(reason => new DecisionReasonDto(reason.Code.ToString(), reason.Message)).ToArray(),
            decision.Clashes
                .Select(clash => new ClashDto(
                    clash.Severity.ToString(),
                    clash.Summary,
                    clash.AffectedPeople.Select(person => person.Id.Value).ToArray()))
                .ToArray(),
            suggestion is null
                ? null
                : new AiSuggestionDto(
                    suggestion.Provider,
                    suggestion.Model,
                    suggestion.SuggestedTitle,
                    suggestion.SuggestedCalendar?.ToString(),
                    suggestion.Confidence,
                    suggestion.Reasons));

    public static CalendarEventSummaryDto ToCalendarEventSummary(CalendarEvent calendarEvent) =>
        new(
            calendarEvent.Id.Value,
            calendarEvent.Title,
            calendarEvent.Time.Date,
            calendarEvent.Time.StartTime,
            calendarEvent.Time.EndTime,
            calendarEvent.Time.IsAllDay,
            calendarEvent.PrimaryCalendar.ToString(),
            calendarEvent.Category.ToString(),
            calendarEvent.BusyStatus.ToString(),
            calendarEvent.Participants.Select(participant => participant.Person.Id.Value).ToArray());

    public static ReviewActionResult ToReviewActionResult(ReviewDecision decision) =>
        new(
            decision.Id.Value,
            decision.Status.ToString(),
            $"Review item {decision.Status}.",
            decision.Event?.Id.Value);
}

public static class CalendarUiErrors
{
    public static BluQubeErrorData StaleReviewItemError() =>
        new("REVIEW_ITEM_NOT_STAGED", "Only staged review items can be changed.");
}

public static class CalendarUiAudits
{
    public static AuditEntry ForDecision(ReviewDecision decision) =>
        new(
            AuditEntryId.New(),
            decision.Status switch
            {
                ReviewStatus.Approved => AuditAction.EventApproved,
                ReviewStatus.Staged => AuditAction.EventStaged,
                ReviewStatus.Rejected => AuditAction.EventRejected,
                _ => AuditAction.IntentReviewed
            },
            ActorRef.System,
            decision.DecidedAt,
            $"Review item {decision.Status}.",
            decision.IntentId,
            decision.Event?.Id,
            decision.Id,
            new Dictionary<string, string>
            {
                ["mode"] = decision.Mode.ToString(),
                ["status"] = decision.Status.ToString()
            });
}

public static class CalendarUiNotifications
{
    public const string ReviewQueueChanged = nameof(ReviewQueueChanged);
    public const string CalendarEventsChanged = nameof(CalendarEventsChanged);

    public static IReadOnlyList<CalendarUpdateNotification> For(ReviewDecision decision)
    {
        var notifications = new List<CalendarUpdateNotification>
        {
            new(ReviewQueueChanged, decision.Id.Value, DateTimeOffset.UtcNow)
        };

        if (decision.Status == ReviewStatus.Approved)
        {
            notifications.Add(new(CalendarEventsChanged, decision.Event?.Id.Value, DateTimeOffset.UtcNow));
        }

        return notifications;
    }
}

public interface ICalendarUpdateNotifier
{
    Task PublishAsync(
        IReadOnlyList<CalendarUpdateNotification> notifications,
        CancellationToken cancellationToken);
}

public sealed class SignalRCalendarUpdateNotifier(IHubContext<CalendarUpdatesHub> hubContext)
    : ICalendarUpdateNotifier
{
    public async Task PublishAsync(
        IReadOnlyList<CalendarUpdateNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await hubContext.Clients.All.SendAsync(
                "CalendarUpdated",
                notification,
                cancellationToken);
        }
    }
}
