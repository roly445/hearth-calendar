using BluQube.Attributes;
using BluQube.Commands;
using BluQube.Queries;
using HearthCalendar.Shared.Domain;

namespace HearthCalendar.Shared.Contracts.Ui;

[BluQubeQuery(Path = "queries/review/queue")]
public sealed record GetReviewQueueQuery : IQuery<ReviewQueueResult>;

public sealed record ReviewQueueResult(IReadOnlyList<ReviewQueueItemDto> Items) : IQueryResult;

[BluQubeQuery(Path = "queries/events/upcoming")]
public sealed record GetUpcomingEventsQuery(
    DateOnly From,
    DateOnly To,
    VirtualCalendar Calendar = VirtualCalendar.Combined) : IQuery<UpcomingEventsResult>;

public sealed record UpcomingEventsResult(IReadOnlyList<CalendarEventSummaryDto> Items) : IQueryResult;

[BluQubeCommand(Path = "commands/events/submit")]
public sealed record SubmitWebEventIntentCommand(
    string RawText,
    DateOnly? Date = null,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null) : ICommand<ReviewActionResult>;

[BluQubeCommand(Path = "commands/review/approve")]
public sealed record ApproveReviewItemCommand(Guid ReviewDecisionId) : ICommand<ReviewActionResult>;

[BluQubeCommand(Path = "commands/review/reject")]
public sealed record RejectReviewItemCommand(Guid ReviewDecisionId) : ICommand<ReviewActionResult>;

[BluQubeCommand(Path = "commands/review/edit")]
public sealed record EditReviewItemCommand(
    Guid ReviewDecisionId,
    string RawText,
    DateOnly? Date = null,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null) : ICommand<ReviewActionResult>;

[BluQubeCommand(Path = "commands/events/delete")]
public sealed record DeleteEventCommand(
    string RawText,
    DateOnly Date,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    ReviewSourceMode SourceMode = ReviewSourceMode.Interactive) : ICommand<ReviewActionResult>;

[BluQubeCommand(Path = "commands/events/reschedule")]
public sealed record RescheduleEventCommand(
    string RawText,
    DateOnly CurrentDate,
    DateOnly NewDate,
    TimeOnly? CurrentStartTime = null,
    TimeOnly? CurrentEndTime = null,
    TimeOnly? NewStartTime = null,
    TimeOnly? NewEndTime = null,
    ReviewSourceMode SourceMode = ReviewSourceMode.Interactive) : ICommand<ReviewActionResult>;

public sealed record ReviewActionResult(
    Guid ReviewDecisionId,
    string Status,
    string Message,
    Guid? CalendarEventId = null) : ICommandResult;

public sealed record ReviewQueueItemDto(
    Guid ReviewDecisionId,
    Guid IntentId,
    string RawText,
    string Status,
    string Mode,
    string Source,
    string SubmittedBy,
    DateTimeOffset SubmittedAt,
    CalendarEventSummaryDto? Candidate,
    IReadOnlyList<DecisionReasonDto> Reasons,
    IReadOnlyList<ClashDto> Clashes,
    AiSuggestionDto? AiSuggestion);

public sealed record CalendarEventSummaryDto(
    Guid EventId,
    string Title,
    DateOnly Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    bool IsAllDay,
    string Calendar,
    string Category,
    string BusyStatus,
    IReadOnlyList<string> Participants);

public sealed record DecisionReasonDto(string Code, string Message);

public sealed record ClashDto(string Severity, string Summary, IReadOnlyList<string> AffectedPeople);

public sealed record AiSuggestionDto(
    string Provider,
    string Model,
    string? SuggestedTitle,
    string? SuggestedCalendar,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public sealed record CalendarUpdateNotification(string Type, Guid? EntityId, DateTimeOffset OccurredAt);
