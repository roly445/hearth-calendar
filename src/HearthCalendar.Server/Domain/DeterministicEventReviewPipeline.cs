using System.Globalization;
using System.Text.RegularExpressions;

namespace HearthCalendar.Server.Domain;

public sealed partial class DeterministicEventReviewPipeline
{
    private readonly DateOnly today;
    private readonly IReadOnlyList<CalendarEvent> existingEvents;
    private readonly IAiReviewProvider aiReviewProvider;

    public DeterministicEventReviewPipeline(
        DateOnly today,
        IReadOnlyList<CalendarEvent>? existingEvents = null,
        IAiReviewProvider? aiReviewProvider = null)
    {
        this.today = today;
        this.existingEvents = existingEvents ?? [];
        this.aiReviewProvider = aiReviewProvider ?? NoOpAiReviewProvider.Instance;
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

    public async ValueTask<ReviewOutcome> ReviewWithAuditAsync(
        EventIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.RawText);

        var deterministicOutcome = ReviewWithAudit(intent);
        if (!CanAskAiForHelp(deterministicOutcome.Decision))
        {
            return deterministicOutcome;
        }

        AiReviewSuggestion? suggestion = null;
        var providerFailed = false;

        try
        {
            suggestion = await aiReviewProvider
                .ReviewAsync(BuildAiReviewRequest(intent), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            providerFailed = true;
        }

        var reasons = deterministicOutcome.Decision.Reasons.ToList();

        if (providerFailed)
        {
            reasons.Add(Reason(DecisionReasonCode.AiProviderUnavailable, "AI review provider failed; deterministic review was used."));

            var degradedDecision = deterministicOutcome.Decision with { Reasons = reasons };

            return new ReviewOutcome(degradedDecision, BuildAuditEntry(intent, degradedDecision), suggestion);
        }

        if (suggestion is null)
        {
            return deterministicOutcome;
        }

        if (deterministicOutcome.Decision.Clashes.Count > 0 ||
            deterministicOutcome.Decision.Status == ReviewStatus.Rejected ||
            HasDeterministicSafetyReason(deterministicOutcome.Decision))
        {
            reasons.Add(Reason(DecisionReasonCode.AiSuggestionIgnored, "AI suggestion did not override deterministic safety rules."));

            var safetyDecision = LinkSuggestion(deterministicOutcome.Decision with { Reasons = reasons }, suggestion);

            return new ReviewOutcome(safetyDecision, BuildAuditEntry(intent, safetyDecision), suggestion);
        }

        if (deterministicOutcome.Decision.Status == ReviewStatus.Staged &&
            deterministicOutcome.Decision.Event is null &&
            CanApplySuggestion(suggestion))
        {
            var candidate = BuildCandidateFromSuggestion(intent, suggestion);
            if (candidate.Time.Date < today)
            {
                var pastDecision = Decide(
                    intent,
                    intent.SourceMode == ReviewSourceMode.Interactive ? ReviewStatus.Rejected : ReviewStatus.Staged,
                    candidate,
                    [
                        Reason(DecisionReasonCode.PastEvent, "Past non-reference events need confirmation."),
                        Reason(DecisionReasonCode.AiSuggestionIgnored, "AI suggestion did not override deterministic safety rules.")
                    ],
                    []);

                pastDecision = LinkSuggestion(pastDecision, suggestion);

                return new ReviewOutcome(pastDecision, BuildAuditEntry(intent, pastDecision), suggestion);
            }

            var clashes = ClashDetector.FindClashes(candidate, existingEvents);
            if (clashes.Count == 0)
            {
                var appliedDecision = Decide(
                    intent,
                    ReviewStatus.Approved,
                    candidate,
                    [Reason(DecisionReasonCode.AiSuggestionApplied, "AI suggestion resolved an allowed ambiguous intent.")],
                    []);

                appliedDecision = LinkSuggestion(appliedDecision, suggestion);

                return new ReviewOutcome(appliedDecision, BuildAuditEntry(intent, appliedDecision), suggestion);
            }

            var clashingDecision = Decide(
                intent,
                ReviewStatus.Staged,
                candidate,
                [
                    Reason(DecisionReasonCode.ClashDetected, "The event overlaps an existing busy event."),
                    Reason(DecisionReasonCode.AiSuggestionIgnored, "AI suggestion did not override deterministic safety rules.")
                ],
                clashes);

            clashingDecision = LinkSuggestion(clashingDecision, suggestion);

            return new ReviewOutcome(clashingDecision, BuildAuditEntry(intent, clashingDecision), suggestion);
        }

        reasons.Add(Reason(DecisionReasonCode.AiSuggestionIgnored, "AI suggestion confidence or shape was not safe to apply automatically."));

        var ignoredDecision = LinkSuggestion(deterministicOutcome.Decision with { Reasons = reasons }, suggestion);

        return new ReviewOutcome(ignoredDecision, BuildAuditEntry(intent, ignoredDecision), suggestion);
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

    private static ReviewDecision LinkSuggestion(ReviewDecision decision, AiReviewSuggestion suggestion) =>
        decision with
        {
            Mode = DecisionMode.AssistedByAi,
            AiSuggestionId = suggestion.Id
        };

    private static bool CanAskAiForHelp(ReviewDecision decision) =>
        decision.Status == ReviewStatus.Staged &&
        decision.Event is null &&
        decision.Reasons.Any(reason => reason.Code == DecisionReasonCode.AmbiguousIntent);

    private static AiReviewRequest BuildAiReviewRequest(EventIntent intent) =>
        new(
            intent.Id,
            intent.Source,
            intent.SourceMode,
            intent.RawText,
            intent.Payload,
            intent.SubmittedAt);

    private static bool HasDeterministicSafetyReason(ReviewDecision decision) =>
        decision.Reasons.Any(reason => reason.Code is
            DecisionReasonCode.MissingDate or
            DecisionReasonCode.PastEvent or
            DecisionReasonCode.ClashDetected);

    private static bool CanApplySuggestion(AiReviewSuggestion suggestion) =>
        suggestion.Confidence >= 0.8m &&
        !string.IsNullOrWhiteSpace(suggestion.SuggestedTitle) &&
        (suggestion.SuggestedCalendar is VirtualCalendar.AdultA or
            VirtualCalendar.AdultB or
            VirtualCalendar.Child or
            VirtualCalendar.Family) &&
        suggestion.SuggestedParticipants.Count > 0 &&
        suggestion.SuggestedParticipants.All(personId => ResolvePerson(personId) is not null) &&
        ResponsibleAdultIsValid(suggestion) &&
        SuggestedParticipantsMatchCalendar(suggestion) &&
        suggestion.SuggestedRecurrence is null;

    private CalendarEvent BuildCandidateFromSuggestion(EventIntent intent, AiReviewSuggestion suggestion)
    {
        var participants = suggestion.SuggestedParticipants
            .Select(ResolvePerson)
            .Where(person => person is not null)
            .Cast<Person>()
            .Select(person => new Participant(person, ParticipantRoleFor(person, suggestion), BusyStatus.Busy))
            .ToArray();
        var primaryCalendar = suggestion.SuggestedCalendar ?? VirtualCalendar.Review;
        var category = primaryCalendar switch
        {
            VirtualCalendar.Family => EventCategory.Family,
            VirtualCalendar.Child => EventCategory.Responsibility,
            _ => EventCategory.Personal
        };
        var responsibleAdult = suggestion.SuggestedResponsibleAdult is null
            ? null
            : ResolvePerson(suggestion.SuggestedResponsibleAdult.Value);
        var time = new EventTime(
            intent.Payload?.Date ?? today,
            intent.Payload?.StartTime,
            intent.Payload?.EndTime,
            intent.Payload?.StartTime is null && intent.Payload?.EndTime is null);

        return CalendarEvent.Approved(
            CalendarEventId.New(),
            suggestion.SuggestedTitle ?? intent.RawText.Trim(),
            time,
            primaryCalendar,
            category,
            BusyStatus.Busy,
            participants,
            intent.Source,
            recurrence: null,
            responsibleAdult is null
                ? null
                : new ResponsibleAdult(responsibleAdult, ResponsibilityKind.Attending, ResponsibilitySource.Inferred));
    }

    private static ParticipationRole ParticipantRoleFor(Person person, AiReviewSuggestion suggestion)
    {
        if (person.Id == KnownPeople.Child.Id)
        {
            return ParticipationRole.Child;
        }

        return suggestion.SuggestedResponsibleAdult == person.Id
            ? ParticipationRole.ResponsibleAdult
            : ParticipationRole.Attendee;
    }

    private static Person? ResolvePerson(PersonId personId) =>
        KnownPeople.All.FirstOrDefault(person => person.Id == personId);

    private static bool ResponsibleAdultIsValid(AiReviewSuggestion suggestion)
    {
        if (suggestion.SuggestedResponsibleAdult is null)
        {
            return true;
        }

        var responsibleAdult = ResolvePerson(suggestion.SuggestedResponsibleAdult.Value);

        return responsibleAdult?.Kind == PersonKind.Adult;
    }

    private static bool SuggestedParticipantsMatchCalendar(AiReviewSuggestion suggestion)
    {
        var participantIds = suggestion.SuggestedParticipants.ToHashSet();

        return suggestion.SuggestedCalendar switch
        {
            VirtualCalendar.AdultA => participantIds.Contains(KnownPeople.AdultA.Id),
            VirtualCalendar.AdultB => participantIds.Contains(KnownPeople.AdultB.Id),
            VirtualCalendar.Family => KnownPeople.All.All(person => participantIds.Contains(person.Id)),
            VirtualCalendar.Child => participantIds.Contains(KnownPeople.Child.Id) &&
                suggestion.SuggestedResponsibleAdult is not null &&
                participantIds.Contains(suggestion.SuggestedResponsibleAdult.Value),
            _ => false
        };
    }

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
