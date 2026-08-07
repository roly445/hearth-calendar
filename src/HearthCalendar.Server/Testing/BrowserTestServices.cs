using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace HearthCalendar.Server.Testing;

internal static partial class BrowserTestServices
{
    private const string SectionName = "BrowserTests";

    public static IServiceCollection AddBrowserTestServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!IsEnabled(configuration, environment))
        {
            return services;
        }

        services.AddSingleton<BrowserTestHearthCalendarStore>();
        services.AddSingleton<IHearthCalendarStore>(provider =>
            provider.GetRequiredService<BrowserTestHearthCalendarStore>());

        return services;
    }

    public static IApplicationBuilder UseBrowserTestAuthentication(this IApplicationBuilder app) =>
        app;

    public static IApplicationBuilder UseBrowserTestStaticFiles(this IApplicationBuilder app)
    {
        if (!app.IsBrowserTestSeedDataEnabled())
        {
            return app;
        }

        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        var providers = BrowserTestStaticFileRoots(environment)
            .Select(root => new PhysicalFileProvider(root))
            .Cast<IFileProvider>()
            .ToArray();
        if (providers.Length == 0)
        {
            return app;
        }

        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".dat"] = "application/octet-stream";
        contentTypes.Mappings[".pdb"] = "application/octet-stream";

        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new CompositeFileProvider(providers),
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                if (context.Context.Response.ContentType is null &&
                    contentTypes.TryGetContentType(context.File.Name, out var contentType))
                {
                    context.Context.Response.ContentType = contentType;
                }
            }
        });
    }

    public static bool IsBrowserTestSeedDataEnabled(this IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

        return IsEnabled(configuration, environment);
    }

    public static IEndpointRouteBuilder MapBrowserTestAppShell(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!IsEnabled(configuration, environment))
        {
            return endpoints;
        }

        endpoints.MapGet("/", BrowserTestIndexAsync)
            .RequireAuthorization(HearthCalendarAuth.AdminPolicy);
        endpoints.MapGet("/index.html", BrowserTestIndexAsync)
            .AllowAnonymous();
        return endpoints;
    }

    private static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsEnvironment("Test") &&
        configuration.GetValue<bool>($"{SectionName}:UseSeedData");

    private static IResult BrowserTestIndexAsync(IHostEnvironment environment)
    {
        var clientRoot = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "HearthCalendar.Client"));
        var transformedIndex = Directory
            .EnumerateFiles(Path.Combine(clientRoot, "obj"), "*.html", SearchOption.AllDirectories)
            .Where(path => path.Contains(
                Path.Combine("staticwebassets", "htmlassetplaceholders", "build"),
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => path.Contains(
                Path.Combine("obj", CurrentBuildConfiguration(), "net10.0"),
                StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return transformedIndex is null
            ? Results.NotFound()
            : Results.File(transformedIndex, "text/html; charset=utf-8");
    }

    private static IEnumerable<string> BrowserTestStaticFileRoots(IHostEnvironment environment)
    {
        var clientRoot = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "HearthCalendar.Client"));
        var sourceWebRoot = Path.Combine(clientRoot, "wwwroot");
        if (Directory.Exists(sourceWebRoot))
        {
            yield return sourceWebRoot;
        }

        var binRoot = Path.Combine(clientRoot, "bin");
        if (Directory.Exists(binRoot))
        {
            foreach (var buildWebRoot in Directory
                .EnumerateDirectories(binRoot, "wwwroot", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Contains(
                    Path.Combine("bin", CurrentBuildConfiguration(), "net10.0"),
                    StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(Directory.GetLastWriteTimeUtc))
            {
                yield return buildWebRoot;
            }
        }

        var objRoot = Path.Combine(clientRoot, "obj");
        if (Directory.Exists(objRoot))
        {
            foreach (var scopedCssRoot in Directory
                .EnumerateDirectories(objRoot, "bundle", SearchOption.AllDirectories)
                .Where(path => path.Contains(
                    Path.Combine("scopedcss", "bundle"),
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => path.Contains(
                    Path.Combine("obj", CurrentBuildConfiguration(), "net10.0"),
                    StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(Directory.GetLastWriteTimeUtc))
            {
                yield return scopedCssRoot;
            }
        }
    }

    private static string CurrentBuildConfiguration() =>
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

}

internal sealed class BrowserTestHearthCalendarStore : IHearthCalendarStore
{
    private static readonly DateOnly Today = new(2026, 8, 12);
    private readonly List<EventIntent> intents = [];
    private readonly List<ReviewDecision> decisions = [];
    private readonly List<CalendarEvent> approvedEvents = [];
    private readonly List<AuditEntry> audits = [];

    public BrowserTestHearthCalendarStore()
    {
        var stagedIntent = new EventIntent(
            EventIntentId.New(),
            CalendarSource.Web,
            ReviewSourceMode.Interactive,
            "Adult A dentist",
            new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)),
            SubmittedAt(),
            ActorRef.System);
        var stagedDecision = new ReviewDecision(
            ReviewDecisionId.New(),
            stagedIntent.Id,
            ReviewStatus.Staged,
            DecisionMode.Automatic,
            [new DecisionReason(DecisionReasonCode.PastEvent, "Past non-reference events need confirmation.")],
            [],
            CalendarEvent.Approved(
                CalendarEventId.New(),
                "Adult A dentist",
                new EventTime(Today, new TimeOnly(9, 0), new TimeOnly(9, 30), false),
                VirtualCalendar.AdultA,
                EventCategory.Personal,
                BusyStatus.Busy,
                [new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy)],
                CalendarSource.Web) with
            {
                ReviewStatus = ReviewStatus.Staged
            },
            SubmittedAt(),
            ActorRef.System);

        intents.Add(stagedIntent);
        decisions.Add(stagedDecision);
        approvedEvents.Add(CalendarEvent.Approved(
            CalendarEventId.New(),
            "Family planning",
            new EventTime(Today.AddDays(2), new TimeOnly(18, 0), new TimeOnly(19, 0), false),
            VirtualCalendar.Family,
            EventCategory.Family,
            BusyStatus.Busy,
            [
                new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy),
                new Participant(KnownPeople.AdultB, ParticipationRole.Attendee, BusyStatus.Busy)
            ],
            CalendarSource.Web));
    }

    public Task StoreIntentAsync(EventIntent intent, CancellationToken cancellationToken)
    {
        intents.Add(intent);

        return Task.CompletedTask;
    }

    public Task StoreIntentWithAuditAsync(
        EventIntent intent,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        intents.Add(intent);
        audits.Add(auditEntry);

        return Task.CompletedTask;
    }

    public Task<CalDavObjectUpsertResult> UpsertCalDavObjectAsync(
        CalDavObjectUpsert upsert,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("CalDAV writes are outside browser smoke test scope.");

    public Task<EventIntent?> LoadIntentAsync(EventIntentId id, CancellationToken cancellationToken) =>
        Task.FromResult(intents.SingleOrDefault(intent => intent.Id == id));

    public Task StoreReviewOutcomeAsync(
        EventIntent intent,
        ReviewOutcome outcome,
        CancellationToken cancellationToken)
    {
        intents.Add(intent);
        decisions.Add(outcome.Decision);
        audits.Add(outcome.AuditEntry);
        if (outcome.Decision.Event?.ReviewStatus == ReviewStatus.Approved)
        {
            approvedEvents.Add(outcome.Decision.Event);
        }

        return Task.CompletedTask;
    }

    public Task StoreAuditEntryAsync(AuditEntry auditEntry, CancellationToken cancellationToken)
    {
        audits.Add(auditEntry);

        return Task.CompletedTask;
    }

    public Task<ReviewOutcome?> LoadReviewOutcomeAsync(
        ReviewDecisionId id,
        CancellationToken cancellationToken)
    {
        var decision = decisions.SingleOrDefault(candidate => candidate.Id == id);
        if (decision is null)
        {
            return Task.FromResult<ReviewOutcome?>(null);
        }

        var audit = audits.FirstOrDefault(candidate => candidate.ReviewDecisionId == id) ??
            CalendarUiAudits.ForDecision(decision);

        return Task.FromResult<ReviewOutcome?>(new ReviewOutcome(decision, audit));
    }

    public Task<ReviewDecision?> LoadReviewDecisionAsync(
        ReviewDecisionId id,
        CancellationToken cancellationToken) =>
        Task.FromResult(decisions.SingleOrDefault(candidate => candidate.Id == id));

    public Task StoreReviewDecisionAsync(
        ReviewDecision decision,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        decisions.RemoveAll(candidate => candidate.Id == decision.Id);
        decisions.Add(decision);
        audits.Add(auditEntry);
        if (decision.Event?.ReviewStatus == ReviewStatus.Approved)
        {
            approvedEvents.Add(decision.Event);
        }

        return Task.CompletedTask;
    }

    public Task StoreEditedReviewOutcomeAsync(
        ReviewDecision originalDecision,
        EventIntent revisedIntent,
        ReviewOutcome revisedOutcome,
        CancellationToken cancellationToken)
    {
        decisions.RemoveAll(candidate => candidate.Id == originalDecision.Id);
        decisions.Add(originalDecision with { Status = ReviewStatus.Rejected });
        intents.Add(revisedIntent);
        decisions.Add(revisedOutcome.Decision);
        audits.Add(revisedOutcome.AuditEntry);

        return Task.CompletedTask;
    }

    public Task DeleteApprovedEventAsync(
        CalendarEvent calendarEvent,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        approvedEvents.RemoveAll(candidate => candidate.Id == calendarEvent.Id);
        audits.Add(auditEntry);

        return Task.CompletedTask;
    }

    public Task RescheduleApprovedEventAsync(
        CalendarEvent originalEvent,
        CalendarEvent rescheduledEvent,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        approvedEvents.RemoveAll(candidate => candidate.Id == originalEvent.Id);
        approvedEvents.Add(rescheduledEvent);
        audits.Add(auditEntry);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CalendarEvent>> QueryApprovedEventsAsync(
        DateOnly from,
        DateOnly to,
        VirtualCalendar calendar,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendarEvent>>(VirtualCalendarViews
            .ForCalendar(
                calendar,
                approvedEvents
                    .Where(calendarEvent => calendarEvent.Time.Date >= from && calendarEvent.Time.Date <= to)
                    .ToArray())
            .ToArray());

    public Task<CalendarEvent?> LoadApprovedEventAsync(
        CalendarEventId id,
        VirtualCalendar calendar,
        CancellationToken cancellationToken) =>
        Task.FromResult(VirtualCalendarViews
            .ForCalendar(calendar, approvedEvents)
            .SingleOrDefault(calendarEvent => calendarEvent.Id == id));

    public Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReviewDecision>>(decisions
            .Where(decision => decision.Status == ReviewStatus.Staged)
            .ToArray());

    public Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(audits.ToArray());

    private static DateTimeOffset SubmittedAt() =>
        new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
}
