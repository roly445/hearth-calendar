using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HearthCalendar.Tests.Server;

public abstract class AuthIntakeEndpointTestBase
{
    protected const string WriteToken = "test-write-token";
    protected const string FeedToken = "test-feed-token";
    protected const string AdminUsername = "admin";
    protected const string AdminPassword = "test-admin-password";

    protected static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory(
        RecordingHearthCalendarStore store) =>
        new WebApplicationFactory<HearthCalendar.Server.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = "Host=localhost;Database=hearth_calendar_test",
                        ["Database:SchemaName"] = "hearth_calendar_test",
                        ["Auth:AdminUsers:0:Username"] = AdminUsername,
                        ["Auth:AdminUsers:0:DisplayName"] = "Calendar Admin",
                        ["Auth:AdminUsers:0:Password"] = AdminPassword,
                        ["Auth:AdminUsers:0:Scopes:0"] = HearthCalendarAuth.AdminWebScope,
                        ["Auth:ClientTokens:0:Name"] = "home-assistant",
                        ["Auth:ClientTokens:0:SecretHash"] = HearthCalendarSecretHasher.Hash(WriteToken),
                        ["Auth:ClientTokens:0:Scopes:0"] = HearthCalendarAuth.IntakeWriteScope,
                        ["Auth:FeedTokens:0:Name"] = "adult-a-feed",
                        ["Auth:FeedTokens:0:TokenHash"] = HearthCalendarSecretHasher.Hash(FeedToken),
                        ["Auth:FeedTokens:0:AllowedCalendars:0"] = VirtualCalendar.AdultA.ToString(),
                        ["Auth:FeedTokens:0:Scopes:0"] = HearthCalendarAuth.FeedReadScope
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    var identityDatabaseName = $"hearth-calendar-auth-{Guid.NewGuid():N}";
                    services.RemoveAll<DbContextOptions<HearthCalendarIdentityDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<HearthCalendarIdentityDbContext>>();
                    services.AddDbContext<HearthCalendarIdentityDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(identityDatabaseName);
                    });
                    services.RemoveAll<IHearthCalendarStore>();
                    services.RemoveAll<IHearthCalendarCredentialStore>();
                    services.AddSingleton<IHearthCalendarStore>(store);
                    services.AddSingleton<IHearthCalendarCredentialStore, NoOpCredentialStore>();
                });
            });

    protected static object DescribeIntent(EventIntent intent) => new
    {
        HasId = intent.Id.Value != Guid.Empty,
        Source = intent.Source.ToString(),
        SourceMode = intent.SourceMode.ToString(),
        intent.RawText,
        Payload = intent.Payload is null
            ? null
            : new
            {
                Date = intent.Payload.Date?.ToString("O"),
                StartTime = intent.Payload.StartTime?.ToString("HH:mm:ss"),
                EndTime = intent.Payload.EndTime?.ToString("HH:mm:ss")
            },
        HasSubmittedAt = intent.SubmittedAt != default,
        SubmittedBy = intent.SubmittedBy.Id
    };

    protected static object DescribeAudit(AuditEntry audit) => new
    {
        Action = audit.Action.ToString(),
        Actor = audit.Actor.Id,
        HasOccurredAt = audit.OccurredAt != default,
        audit.Summary,
        HasIntentLink = audit.IntentId is not null,
        audit.Metadata,
        ContainsRawWriteToken = ContainsValue(audit.Metadata, WriteToken),
        ContainsRawFeedToken = ContainsValue(audit.Metadata, FeedToken)
    };

    protected static bool ContainsValue(IReadOnlyDictionary<string, string>? metadata, string value) =>
        metadata?.Values.Any(candidate => string.Equals(candidate, value, StringComparison.Ordinal)) == true;

    protected sealed class RecordingHearthCalendarStore : IHearthCalendarStore
    {
        public List<EventIntent> Intents { get; } = [];

        public List<AuditEntry> Audits { get; } = [];

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
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreAuditEntryAsync(AuditEntry auditEntry, CancellationToken cancellationToken)
        {
            Audits.Add(auditEntry);

            return Task.CompletedTask;
        }

        public Task<ReviewOutcome?> LoadReviewOutcomeAsync(
            ReviewDecisionId id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReviewDecision?> LoadReviewDecisionAsync(
            ReviewDecisionId id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreReviewDecisionAsync(
            ReviewDecision decision,
            AuditEntry auditEntry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreEditedReviewOutcomeAsync(
            ReviewDecision originalDecision,
            EventIntent revisedIntent,
            ReviewOutcome revisedOutcome,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteApprovedEventAsync(
            CalendarEvent calendarEvent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RescheduleApprovedEventAsync(
            CalendarEvent originalEvent,
            CalendarEvent rescheduledEvent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> QueryApprovedEventsAsync(
            DateOnly from,
            DateOnly to,
            VirtualCalendar calendar,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CalendarEvent?> LoadApprovedEventAsync(
            CalendarEventId id,
            VirtualCalendar calendar,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Audits);
    }
}
