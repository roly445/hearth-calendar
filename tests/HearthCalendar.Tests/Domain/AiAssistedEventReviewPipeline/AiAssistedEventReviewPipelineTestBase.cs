using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public abstract class AiAssistedEventReviewPipelineTestBase
{
    protected static readonly DateOnly Today = new(2026, 7, 29);

    protected static DeterministicEventReviewPipeline Pipeline(
        IAiReviewProvider provider,
        params CalendarEvent[] existingEvents) =>
        new(Today, existingEvents, provider);

    protected static EventIntent Intent(
        string text,
        EventIntentPayload? payload = null,
        ReviewSourceMode sourceMode = ReviewSourceMode.Passive) =>
        new(
            EventIntentId.New(),
            CalendarSource.Test,
            sourceMode,
            text,
            payload,
            new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            ActorRef.System);

    protected static AiReviewSuggestion Suggestion(
        string title,
        VirtualCalendar calendar,
        IReadOnlyList<PersonId> participants,
        decimal confidence,
        PersonId? responsibleAdult = null,
        RecurrenceRule? recurrence = null) =>
        new(
            AiReviewSuggestionId.New(),
            "stub",
            "stub-model",
            title,
            calendar,
            participants,
            responsibleAdult,
            recurrence,
            confidence,
            ["Matched placeholder people and a simple calendar type."],
            new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 1)), TimeSpan.Zero));

    protected static object DescribeOutcome(ReviewOutcome outcome) => new
    {
        Decision = DescribeDecision(outcome.Decision),
        AiSuggestion = DescribeSuggestion(outcome.AiSuggestion),
        AuditEntry = new
        {
            Action = outcome.AuditEntry.Action.ToString(),
            Actor = outcome.AuditEntry.Actor.Id,
            OccurredAt = outcome.AuditEntry.OccurredAt.ToString("O"),
            outcome.AuditEntry.Summary,
            HasIntentLink = outcome.AuditEntry.IntentId is not null,
            HasCalendarEventLink = outcome.AuditEntry.CalendarEventId is not null,
            HasReviewDecisionLink = outcome.AuditEntry.ReviewDecisionId is not null,
            outcome.AuditEntry.Metadata
        }
    };

    protected static object DescribeDecision(ReviewDecision decision) => new
    {
        Status = decision.Status.ToString(),
        Mode = decision.Mode.ToString(),
        HasAiSuggestionLink = decision.AiSuggestionId is not null,
        Event = DescribeEvent(decision.Event),
        Reasons = decision.Reasons.Select(reason => new
        {
            Code = reason.Code.ToString(),
            reason.Message
        }).ToArray(),
        Clashes = decision.Clashes.Select(DescribeClash).ToArray(),
        DecidedAt = decision.DecidedAt.ToString("O"),
        DecidedBy = decision.DecidedBy.Id
    };

    protected static object? DescribeEvent(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
        {
            return null;
        }

        return new
        {
            calendarEvent.Title,
            PrimaryCalendar = calendarEvent.PrimaryCalendar.ToString(),
            Category = calendarEvent.Category.ToString(),
            BusyStatus = calendarEvent.BusyStatus.ToString(),
            ReviewStatus = calendarEvent.ReviewStatus.ToString(),
            Time = new
            {
                Date = calendarEvent.Time.Date.ToString("O"),
                StartTime = calendarEvent.Time.StartTime?.ToString("HH:mm:ss"),
                EndTime = calendarEvent.Time.EndTime?.ToString("HH:mm:ss"),
                calendarEvent.Time.IsAllDay
            },
            Recurrence = calendarEvent.Recurrence?.Frequency.ToString(),
            Participants = calendarEvent.Participants.Select(participant => new
            {
                Person = participant.Person.DisplayName,
                PersonId = participant.Person.Id.Value,
                Role = participant.Role.ToString(),
                BusyStatus = participant.BusyStatus.ToString()
            }).ToArray(),
            ResponsibleAdult = calendarEvent.ResponsibleAdult is null
                ? null
                : new
                {
                    Adult = calendarEvent.ResponsibleAdult.Adult.DisplayName,
                    AdultId = calendarEvent.ResponsibleAdult.Adult.Id.Value,
                    Kind = calendarEvent.ResponsibleAdult.Kind.ToString(),
                    Source = calendarEvent.ResponsibleAdult.Source.ToString()
                },
            HasParentEvent = calendarEvent.ParentEventId is not null
        };
    }

    protected static object? DescribeSuggestion(AiReviewSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return null;
        }

        return new
        {
            suggestion.Provider,
            suggestion.Model,
            suggestion.SuggestedTitle,
            SuggestedCalendar = suggestion.SuggestedCalendar?.ToString(),
            SuggestedParticipants = suggestion.SuggestedParticipants.Select(personId => personId.Value).ToArray(),
            SuggestedResponsibleAdult = suggestion.SuggestedResponsibleAdult?.Value,
            Recurrence = suggestion.SuggestedRecurrence?.Frequency.ToString(),
            suggestion.Confidence,
            suggestion.Reasons,
            CreatedAt = suggestion.CreatedAt.ToString("O")
        };
    }

    protected static object DescribeClash(Clash clash) => new
    {
        Severity = clash.Severity.ToString(),
        clash.Summary,
        AffectedPeople = clash.AffectedPeople.Select(person => new
        {
            person.DisplayName,
            PersonId = person.Id.Value
        }).ToArray()
    };

    protected static object DescribeRequest(AiReviewRequest request) => new
    {
        HasIntentId = request.IntentId.Value != Guid.Empty,
        Source = request.Source.ToString(),
        SourceMode = request.SourceMode.ToString(),
        request.RawText,
        Payload = request.Payload is null
            ? null
            : new
            {
                Date = request.Payload.Date?.ToString("O"),
                StartTime = request.Payload.StartTime?.ToString("HH:mm:ss"),
                EndTime = request.Payload.EndTime?.ToString("HH:mm:ss")
            },
        SubmittedAt = request.SubmittedAt.ToString("O")
    };

    protected sealed class StubAiReviewProvider(AiReviewSuggestion? suggestion) : IAiReviewProvider
    {
        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(suggestion);
    }

    protected sealed class ThrowingAiReviewProvider : IAiReviewProvider
    {
        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Provider unavailable.");
        }
    }

    protected sealed class CapturingAiReviewProvider : IAiReviewProvider
    {
        public AiReviewRequest? Request { get; private set; }

        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;

            return ValueTask.FromResult<AiReviewSuggestion?>(null);
        }
    }
}
