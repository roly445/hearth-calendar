using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public abstract class DeterministicEventReviewPipelineTestBase
{
    protected static readonly DateOnly Today = new(2026, 7, 29);

    protected static ReviewDecision Review(string text) => Pipeline().Review(Intent(text));

    protected static DeterministicEventReviewPipeline Pipeline(params CalendarEvent[] existingEvents) =>
        new(Today, existingEvents);

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

    protected static object DescribeDecision(ReviewDecision decision) => new
    {
        Status = decision.Status.ToString(),
        Mode = decision.Mode.ToString(),
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
}
