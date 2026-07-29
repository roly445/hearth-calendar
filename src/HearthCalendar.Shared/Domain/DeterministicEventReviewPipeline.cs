using System.Globalization;
using System.Text.RegularExpressions;

namespace HearthCalendar.Shared.Domain;

public sealed partial class DeterministicEventReviewPipeline
{
    private readonly DateOnly today;
    private readonly IReadOnlyList<CalendarEvent> existingEvents;

    public DeterministicEventReviewPipeline(DateOnly today, IReadOnlyList<CalendarEvent>? existingEvents = null)
    {
        this.today = today;
        this.existingEvents = existingEvents ?? [];
    }

    public ReviewDecision Review(EventIntent intent)
    {
        return ReviewWithAudit(intent).Decision;
    }

    public ReviewOutcome ReviewWithAudit(EventIntent intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.RawText);

        if (IsReferenceIntent(intent.RawText) && ResolveDate(intent.Payload?.Date, intent.RawText) is null)
        {
            var stagedDecision = Decide(
                intent,
                ReviewStatus.Staged,
                null,
                [Reason(DecisionReasonCode.MissingDate, "Reference events need a known date before yearly recurrence can be created.")],
                []);

            return new ReviewOutcome(stagedDecision, BuildAuditEntry(intent, stagedDecision));
        }

        var candidate = BuildCandidate(intent);
        if (candidate is null)
        {
            var stagedDecision = Decide(intent, ReviewStatus.Staged, null, [Reason(DecisionReasonCode.AmbiguousIntent, "The intent does not identify enough calendar details.")], []);

            return new ReviewOutcome(stagedDecision, BuildAuditEntry(intent, stagedDecision));
        }

        if (candidate.Time.Date < today && candidate.Category is not EventCategory.Birthday and not EventCategory.Anniversary)
        {
            var status = intent.SourceMode == ReviewSourceMode.Interactive ? ReviewStatus.Rejected : ReviewStatus.Staged;
            var decision = Decide(intent, status, candidate, [Reason(DecisionReasonCode.PastEvent, "Past non-reference events need confirmation.")], []);

            return new ReviewOutcome(decision, BuildAuditEntry(intent, decision));
        }

        var clashes = ClashDetector.FindClashes(candidate, existingEvents);
        if (clashes.Count > 0)
        {
            var decision = Decide(intent, ReviewStatus.Staged, candidate, [Reason(DecisionReasonCode.ClashDetected, "The event overlaps an existing busy event.")], clashes);

            return new ReviewOutcome(decision, BuildAuditEntry(intent, decision));
        }

        var approvedDecision = Decide(intent, ReviewStatus.Approved, candidate, [Reason(DecisionReasonCode.DeterministicMatch, "The deterministic review rules matched the intent.")], []);

        return new ReviewOutcome(approvedDecision, BuildAuditEntry(intent, approvedDecision));
    }

    private CalendarEvent? BuildCandidate(EventIntent intent)
    {
        var normalized = intent.RawText.Trim();
        var lower = normalized.ToLowerInvariant();
        var date = ResolveDate(intent.Payload?.Date, normalized);
        var time = new EventTime(
            date ?? today,
            intent.Payload?.StartTime,
            intent.Payload?.EndTime,
            intent.Payload?.StartTime is null && intent.Payload?.EndTime is null);

        if (lower.Contains("birthday", StringComparison.Ordinal) || lower.Contains("anniversary", StringComparison.Ordinal))
        {
            var person = FindPerson(lower);
            var category = lower.Contains("birthday", StringComparison.Ordinal)
                ? EventCategory.Birthday
                : EventCategory.Anniversary;

            return CalendarEvent.Approved(
                CalendarEventId.New(),
                normalized,
                time,
                VirtualCalendar.Events,
                category,
                BusyStatus.Free,
                person is null ? [] : [new Participant(person, ParticipationRole.Attendee, BusyStatus.Free)],
                intent.Source,
                new RecurrenceRule(RecurrenceFrequency.Yearly));
        }

        if (lower.Contains("child", StringComparison.Ordinal))
        {
            var responsibleAdult = FindAdult(lower);
            if (responsibleAdult is null)
            {
                return null;
            }

            return CalendarEvent.Approved(
                CalendarEventId.New(),
                normalized,
                time,
                VirtualCalendar.Child,
                EventCategory.Responsibility,
                BusyStatus.Busy,
                [
                    new Participant(KnownPeople.Child, ParticipationRole.Child, BusyStatus.Busy),
                    new Participant(responsibleAdult, ParticipationRole.ResponsibleAdult, BusyStatus.Busy)
                ],
                intent.Source,
                responsibleAdult: new ResponsibleAdult(responsibleAdult, ResponsibilityKind.Attending, ResponsibilitySource.Inferred));
        }

        if (lower.Contains("family", StringComparison.Ordinal))
        {
            return CalendarEvent.Approved(
                CalendarEventId.New(),
                normalized,
                time,
                VirtualCalendar.Family,
                EventCategory.Family,
                BusyStatus.Busy,
                KnownPeople.All.Select(person => new Participant(person, ParticipationRole.Attendee, BusyStatus.Busy)).ToArray(),
                intent.Source);
        }

        var adult = FindAdult(lower);
        if (adult is not null)
        {
            return CalendarEvent.Approved(
                CalendarEventId.New(),
                normalized,
                time,
                adult.Id == KnownPeople.AdultA.Id ? VirtualCalendar.AdultA : VirtualCalendar.AdultB,
                EventCategory.Personal,
                BusyStatus.Busy,
                [new Participant(adult, ParticipationRole.Attendee, BusyStatus.Busy)],
                intent.Source);
        }

        return null;
    }

    private DateOnly? ResolveDate(DateOnly? payloadDate, string rawText) =>
        payloadDate ?? ParseDayMonth(rawText);

    private DateOnly? ParseDayMonth(string rawText)
    {
        var match = DayMonthRegex().Match(rawText);
        if (!match.Success)
        {
            return null;
        }

        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        var month = DateTime.ParseExact(match.Groups["month"].Value, "MMMM", CultureInfo.InvariantCulture).Month;

        return new DateOnly(today.Year, month, day);
    }

    private static Person? FindPerson(string lower) => FindAdult(lower) ?? (lower.Contains("child", StringComparison.Ordinal) ? KnownPeople.Child : null);

    private static Person? FindAdult(string lower)
    {
        if (lower.Contains("adult a", StringComparison.Ordinal))
        {
            return KnownPeople.AdultA;
        }

        return lower.Contains("adult b", StringComparison.Ordinal) ? KnownPeople.AdultB : null;
    }

    private static ReviewDecision Decide(
        EventIntent intent,
        ReviewStatus status,
        CalendarEvent? candidate,
        IReadOnlyList<DecisionReason> reasons,
        IReadOnlyList<Clash> clashes) =>
        new(
            ReviewDecisionId.New(),
            intent.Id,
            status,
            DecisionMode.Automatic,
            reasons,
            clashes,
            candidate is null ? null : candidate with { ReviewStatus = status },
            intent.SubmittedAt,
            ActorRef.System);

    private static DecisionReason Reason(DecisionReasonCode code, string message) => new(code, message);

    private static bool IsReferenceIntent(string rawText)
    {
        var lower = rawText.ToLowerInvariant();

        return lower.Contains("birthday", StringComparison.Ordinal) ||
            lower.Contains("anniversary", StringComparison.Ordinal);
    }

    private static AuditEntry BuildAuditEntry(EventIntent intent, ReviewDecision decision) =>
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
            $"Intent {decision.Status}.",
            intent.Id,
            decision.Event?.Id,
            decision.Id,
            new Dictionary<string, string>
            {
                ["source"] = intent.Source.ToString(),
                ["mode"] = intent.SourceMode.ToString()
            });

    [GeneratedRegex(@"\bon\s+(?<day>\d{1,2})\s+(?<month>january|february|march|april|may|june|july|august|september|october|november|december)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthRegex();
}
