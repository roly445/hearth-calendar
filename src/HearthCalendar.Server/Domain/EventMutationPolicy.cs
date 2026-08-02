namespace HearthCalendar.Server.Domain;

public sealed record DeleteEventPlan(
    MutationPlanStatus Status,
    CalendarEvent? MatchedEvent,
    IReadOnlyList<DecisionReason> Reasons);

public sealed record RescheduleEventPlan(
    MutationPlanStatus Status,
    CalendarEvent? MatchedEvent,
    CalendarEvent? RescheduledEvent,
    IReadOnlyList<DecisionReason> Reasons,
    IReadOnlyList<Clash> Clashes);

public enum MutationPlanStatus
{
    Approved,
    Rejected,
    Staged
}

public static class EventMutationPolicy
{
    public static DeleteEventPlan PlanDelete(
        string rawText,
        DateOnly date,
        TimeOnly? startTime,
        TimeOnly? endTime,
        IReadOnlyList<CalendarEvent> candidates)
    {
        var matches = candidates
            .Where(calendarEvent => IsExactMatch(calendarEvent, rawText, date, startTime, endTime))
            .ToArray();

        return matches.Length switch
        {
            1 => new DeleteEventPlan(
                MutationPlanStatus.Approved,
                matches[0],
                [Reason(DecisionReasonCode.ExactEventMatch, "The delete request exactly matched one approved event.")]),
            0 => new DeleteEventPlan(
                MutationPlanStatus.Rejected,
                null,
                [Reason(DecisionReasonCode.AmbiguousEventMatch, "The delete request did not exactly match an approved event.")]),
            _ => new DeleteEventPlan(
                MutationPlanStatus.Staged,
                null,
                [Reason(DecisionReasonCode.AmbiguousEventMatch, "The delete request matched more than one approved event.")])
        };
    }

    public static RescheduleEventPlan PlanReschedule(
        string rawText,
        DateOnly currentDate,
        TimeOnly? currentStartTime,
        TimeOnly? currentEndTime,
        DateOnly newDate,
        TimeOnly? newStartTime,
        TimeOnly? newEndTime,
        IReadOnlyList<CalendarEvent> candidates,
        IReadOnlyList<CalendarEvent> existingEvents)
    {
        var matches = candidates
            .Where(calendarEvent => IsExactMatch(calendarEvent, rawText, currentDate, currentStartTime, currentEndTime))
            .ToArray();

        if (matches.Length == 0)
        {
            return new RescheduleEventPlan(
                MutationPlanStatus.Rejected,
                null,
                null,
                [Reason(DecisionReasonCode.AmbiguousEventMatch, "The reschedule request did not exactly match an approved event.")],
                []);
        }

        if (matches.Length > 1)
        {
            return new RescheduleEventPlan(
                MutationPlanStatus.Staged,
                null,
                null,
                [Reason(DecisionReasonCode.AmbiguousEventMatch, "The reschedule request matched more than one approved event.")],
                []);
        }

        var matched = matches[0];
        var rescheduled = matched with
        {
            Time = new EventTime(
                newDate,
                newStartTime,
                newEndTime,
                newStartTime is null && newEndTime is null)
        };

        if (IsDuplicateAdjacentEvent(rescheduled, existingEvents))
        {
            return new RescheduleEventPlan(
                MutationPlanStatus.Rejected,
                matched,
                null,
                [Reason(DecisionReasonCode.DuplicateEventMatch, "The reschedule would duplicate an existing approved event.")],
                []);
        }

        var clashes = ClashDetector.FindClashes(rescheduled, existingEvents);
        if (clashes.Count > 0)
        {
            return new RescheduleEventPlan(
                MutationPlanStatus.Staged,
                matched,
                rescheduled,
                [Reason(DecisionReasonCode.ClashDetected, "The rescheduled event overlaps an existing busy event.")],
                clashes);
        }

        return new RescheduleEventPlan(
            MutationPlanStatus.Approved,
            matched,
            rescheduled,
            [Reason(DecisionReasonCode.ExactEventMatch, "The reschedule request exactly matched one approved event.")],
            []);
    }

    private static bool IsExactMatch(
        CalendarEvent calendarEvent,
        string rawText,
        DateOnly date,
        TimeOnly? startTime,
        TimeOnly? endTime) =>
        calendarEvent.ReviewStatus == ReviewStatus.Approved &&
        string.Equals(Normalize(calendarEvent.Title), Normalize(rawText), StringComparison.Ordinal) &&
        calendarEvent.Time.Date == date &&
        calendarEvent.Time.StartTime == startTime &&
        calendarEvent.Time.EndTime == endTime;

    private static bool IsDuplicateAdjacentEvent(
        CalendarEvent rescheduled,
        IReadOnlyList<CalendarEvent> existingEvents) =>
        existingEvents.Any(existing =>
            existing.Id != rescheduled.Id &&
            existing.ReviewStatus == ReviewStatus.Approved &&
            string.Equals(Normalize(existing.Title), Normalize(rescheduled.Title), StringComparison.Ordinal) &&
            existing.Time == rescheduled.Time &&
            existing.PrimaryCalendar == rescheduled.PrimaryCalendar);

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static DecisionReason Reason(DecisionReasonCode code, string message) => new(code, message);
}
