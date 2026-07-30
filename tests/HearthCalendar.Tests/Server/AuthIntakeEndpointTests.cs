using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HearthCalendar.Tests.Server;

public sealed class AuthIntakeEndpointTests
{
    private const string WriteToken = "test-write-token";
    private const string FeedToken = "test-feed-token";

    [Fact]
    public async Task Valid_intake_token_can_submit_generic_intent_and_audit_without_raw_secret()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new IntakeEventRequest(
                "Family calendar planning",
                ReviewSourceMode.Passive,
                new DateOnly(2026, 8, 1),
                new TimeOnly(10, 0),
                new TimeOnly(11, 0)));

        await Verifier.Verify(new
        {
            response.StatusCode,
            Body = await response.Content.ReadFromJsonAsync<IntakeEventResponse>(),
            StoredIntents = store.Intents.Select(DescribeIntent),
            Audits = store.Audits.Select(DescribeAudit)
        });
    }

    [Fact]
    public async Task Valid_home_assistant_token_can_submit_home_assistant_intent()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/home-assistant/event",
            new IntakeEventRequest("Adult A appointment", Date: new DateOnly(2026, 8, 2)));

        await Verifier.Verify(new
        {
            response.StatusCode,
            StoredIntents = store.Intents.Select(DescribeIntent),
            Audits = store.Audits.Select(DescribeAudit)
        });
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-token", HttpStatusCode.Unauthorized)]
    public async Task Missing_or_invalid_token_is_rejected_without_storing_intent(
        string? token,
        HttpStatusCode expectedStatusCode)
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new IntakeEventRequest("Family calendar planning"));

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Feed_token_cannot_submit_intent()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FeedToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new IntakeEventRequest("Family calendar planning"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Invalid_source_mode_is_rejected_without_storing_intent()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new
            {
                RawText = "Family calendar planning",
                SourceMode = 999
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Write_token_cannot_access_admin_endpoint()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Admin_policy_requires_explicit_admin_scope()
    {
        var store = new RecordingHearthCalendarStore();
        using var factory = CreateFactory(store);

        var authorizationOptions = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>()
            .Value;
        var adminPolicy = authorizationOptions.GetPolicy(HearthCalendarAuth.AdminPolicy);
        var claimRequirement = Assert.IsType<ClaimsAuthorizationRequirement>(
            Assert.Single(adminPolicy!.Requirements.OfType<ClaimsAuthorizationRequirement>()));

        Assert.Equal(HearthCalendarAuth.ScopeClaim, claimRequirement.ClaimType);
        Assert.Equal([HearthCalendarAuth.AdminWebScope], claimRequirement.AllowedValues);
    }

    [Fact]
    public void Feed_token_principal_carries_allowed_calendar_claims()
    {
        var principal = HearthCalendarTokenPrincipalFactory.Create(
            "adult-a-feed",
            HearthCalendarAuth.FeedTokenKind,
            [HearthCalendarAuth.FeedReadScope],
            [VirtualCalendar.AdultA.ToString()]);

        Assert.Equal(HearthCalendarAuth.FeedTokenKind, principal.FindFirst(HearthCalendarAuth.TokenKindClaim)?.Value);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == HearthCalendarAuth.ScopeClaim &&
            claim.Value == HearthCalendarAuth.FeedReadScope);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == HearthCalendarAuth.AllowedCalendarClaim &&
            claim.Value == VirtualCalendar.AdultA.ToString());
    }

    [Fact]
    public async Task Admin_endpoint_requires_authentication()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory(
        RecordingHearthCalendarStore store) =>
        new WebApplicationFactory<HearthCalendar.Server.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = "Host=localhost;Database=hearth_calendar_test",
                        ["Database:SchemaName"] = "hearth_calendar_test",
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
                    services.RemoveAll<IHearthCalendarStore>();
                    services.AddSingleton<IHearthCalendarStore>(store);
                });
            });

    private static object DescribeIntent(EventIntent intent) => new
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

    private static object DescribeAudit(AuditEntry audit) => new
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

    private static bool ContainsValue(IReadOnlyDictionary<string, string>? metadata, string value) =>
        metadata?.Values.Any(candidate => string.Equals(candidate, value, StringComparison.Ordinal)) == true;

    private sealed class RecordingHearthCalendarStore : IHearthCalendarStore
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

        public Task<IReadOnlyList<CalendarEvent>> QueryApprovedEventsAsync(
            DateOnly from,
            DateOnly to,
            VirtualCalendar calendar,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Audits);
    }
}
