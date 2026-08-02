using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class ReviewWithAuditTests : DeterministicEventReviewPipelineTestBase
{
    [Fact]
    public Task Review_with_audit_returns_audit_entry_for_decision()
    {
        var intent = Intent("Family BBQ");
        var outcome = Pipeline().ReviewWithAudit(intent);

        return Verifier.Verify(new
        {
            Decision = DescribeDecision(outcome.Decision),
            AuditEntry = new
            {
                outcome.AuditEntry.Action,
                Actor = outcome.AuditEntry.Actor.Id,
                outcome.AuditEntry.OccurredAt,
                outcome.AuditEntry.Summary,
                HasIntentLink = outcome.AuditEntry.IntentId is not null,
                HasCalendarEventLink = outcome.AuditEntry.CalendarEventId is not null,
                HasReviewDecisionLink = outcome.AuditEntry.ReviewDecisionId is not null,
                outcome.AuditEntry.Metadata
            }
        });
    }
}
