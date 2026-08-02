using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.CalDav;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HearthCalendar.Tests.Server;

public sealed class CalDavEndpointTests
{
    private const string CalDavUser = "caldav-app";
    private const string CalDavPassword = "test-caldav-app-password";
    private const string CalDavReadUser = "caldav-read-app";
    private const string CalDavReadPassword = "test-caldav-read-app-password";
    private const string WriteToken = "test-write-token";
    private const string FeedToken = "test-feed-token";

    [Fact]
    public async Task Discovery_requires_caldav_basic_authentication_challenge()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(PropFind("/caldav/"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"Hearth Calendar CalDAV\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Options_advertises_caldav_discovery_methods()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var root = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/caldav/"));
        var smartInbox = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Options,
            "/caldav/calendars/smart-inbox/"));
        var smartInboxArchive = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Options,
            "/caldav/calendars/smart-inbox-archive/"));

        Assert.Equal(HttpStatusCode.NoContent, root.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND", root.Content.Headers.Allow.ToString());
        Assert.Equal("1, 3, calendar-access", root.Headers.GetValues("DAV").Single());
        Assert.Equal(HttpStatusCode.NoContent, smartInbox.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND, PUT", smartInbox.Content.Headers.Allow.ToString());
        Assert.Equal("1, 3, calendar-access", smartInbox.Headers.GetValues("DAV").Single());
        Assert.Equal(HttpStatusCode.NoContent, smartInboxArchive.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND", smartInboxArchive.Content.Headers.Allow.ToString());
    }

    [Theory]
    [InlineData(WriteToken)]
    [InlineData(FeedToken)]
    public async Task Bearer_tokens_cannot_use_caldav_discovery(string token)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(PropFind("/caldav/calendars/"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_caldav_credential_can_discover_but_not_write()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var discovery = await client.SendAsync(PropFind("/caldav/calendars/"));
        var write = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal((HttpStatusCode)207, discovery.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Propfind_root_returns_service_discovery_multistatus()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1, 3, calendar-access", response.Headers.GetValues("DAV").Single());
        await Verifier.Verify(NormalizeDiscoveryXml(document));
    }

    [Fact]
    public async Task Propfind_calendars_returns_smart_inbox_and_virtual_calendar_metadata()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/", depth: "1"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        await Verifier.Verify(NormalizeDiscoveryXml(document));
    }

    [Fact]
    public async Task Propfind_smart_inbox_marks_calendar_writable()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/smart-inbox/", depth: "0"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var discovery = NormalizeDiscoveryXml(document);

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/smart-inbox/" &&
            item.Privileges.SequenceEqual(["write"]));
    }

    [Fact]
    public async Task Smart_inbox_put_creates_caldav_event_intent_and_audit()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                VERSION:2.0
                BEGIN:VEVENT
                UID:family-planning@example.invalid
                SUMMARY:Family planning
                DTSTART:20260801T100000Z
                DTEND:20260801T110000Z
                END:VEVENT
                END:VCALENDAR
                """));

        await Verifier.Verify(new
        {
            response.StatusCode,
            Location = response.Headers.Location?.ToString(),
            ETag = response.Headers.ETag?.ToString(),
            Body = await response.Content.ReadFromJsonAsync<IntakeEventResponse>(),
            StoredIntents = store.Intents.Select(DescribeIntent),
            Audits = store.Audits.Select(DescribeAudit),
            Objects = store.Objects.Values.Select(DescribeObject)
        });
    }

    [Fact]
    public async Task Repeated_identical_smart_inbox_put_reuses_existing_intent_and_etag()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var retry = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal(first.Headers.ETag, retry.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Single(store.Audits);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task Changed_smart_inbox_put_replaces_object_metadata_and_creates_new_intent()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var changed = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Updated family planning
                DTSTART:20260801T120000Z
                DTEND:20260801T130000Z
                END:VEVENT
                END:VCALENDAR
                """));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(first.Headers.ETag, changed.Headers.ETag);
        Assert.Equal(2, store.Intents.Count);
        Assert.Equal(2, store.Audits.Count);
        var storedObject = Assert.Single(store.Objects.Values);
        Assert.Equal(store.Intents[1].Id, storedObject.IntentId);
        Assert.Equal(changed.Headers.ETag?.ToString(), storedObject.ETag);
    }

    [Fact]
    public async Task Stale_if_match_is_rejected_without_creating_intent_or_audit()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var changed = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Updated family planning
                DTSTART:20260801T120000Z
                DTEND:20260801T130000Z
                END:VEVENT
                END:VCALENDAR
                """)
        };
        changed.Headers.TryAddWithoutValidation("If-Match", "\"stale-etag\"");

        var response = await client.SendAsync(changed);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(first.Headers.ETag, response.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Single(store.Audits);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task If_none_match_star_prevents_overwriting_existing_object()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var retry = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent(BasicIcs())
        };
        retry.Headers.TryAddWithoutValidation("If-None-Match", "*");

        var response = await client.SendAsync(retry);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(first.Headers.ETag, response.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Single(store.Audits);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task If_match_star_requires_existing_object()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var write = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent(BasicIcs())
        };
        write.Headers.TryAddWithoutValidation("If-Match", "*");

        var response = await client.SendAsync(write);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task If_none_match_matching_etag_prevents_overwrite()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var changed = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Updated family planning
                DTSTART:20260801T120000Z
                DTEND:20260801T130000Z
                END:VEVENT
                END:VCALENDAR
                """)
        };
        changed.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag?.ToString());

        var response = await client.SendAsync(changed);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(first.Headers.ETag, response.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Single(store.Audits);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task Smart_inbox_item_ids_remain_case_sensitive()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var upper = await client.PutAsync(
            "/caldav/calendars/smart-inbox/Family-Planning.ics",
            IcsContent(BasicIcs()));
        var lower = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.Created, upper.StatusCode);
        Assert.Equal(HttpStatusCode.Created, lower.StatusCode);
        Assert.Equal(2, store.Intents.Count);
        Assert.Equal(2, store.Audits.Count);
        Assert.Equal(2, store.Objects.Count);
        Assert.Contains("smart-inbox/Family-Planning", store.Objects.Keys);
        Assert.Contains("smart-inbox/family-planning", store.Objects.Keys);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-caldav-password", HttpStatusCode.Unauthorized)]
    public async Task Smart_inbox_put_requires_valid_caldav_app_password(
        string? password,
        HttpStatusCode expectedStatusCode)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        if (password is not null)
        {
            client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, password);
        }

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Theory]
    [InlineData(WriteToken)]
    [InlineData(FeedToken)]
    public async Task Bearer_tokens_cannot_write_caldav_calendar_objects(string token)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Unsupported_caldav_calendar_is_not_writable()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/adult-a/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Supplied_invalid_dtend_is_rejected_without_storing_intent()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Family planning
                DTSTART:20260801T100000Z
                DTEND:not-a-date
                END:VEVENT
                END:VCALENDAR
                """));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Oversized_caldav_body_is_rejected_without_storing_intent()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var oversizedBody = BasicIcs() + new string('X', 70 * 1024);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(oversizedBody));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public void Parser_accepts_all_day_vevent_without_faking_midnight_times()
    {
        var parsed = CalDavEventParser.Parse("""
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            SUMMARY:Adult B birthday
            DTSTART;VALUE=DATE:20260725
            END:VEVENT
            END:VCALENDAR
            """);

        Assert.NotNull(parsed);
        Assert.Equal("Adult B birthday", parsed.Summary);
        Assert.Equal(new DateOnly(2026, 7, 25), parsed.Date);
        Assert.Null(parsed.StartTime);
        Assert.Null(parsed.EndTime);
    }

    private static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory(
        RecordingCalDavStore store) =>
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
                        ["Auth:FeedTokens:0:Scopes:0"] = HearthCalendarAuth.FeedReadScope,
                        ["Auth:CalDavCredentials:0:Name"] = CalDavUser,
                        ["Auth:CalDavCredentials:0:SecretHash"] = HearthCalendarSecretHasher.Hash(CalDavPassword),
                        ["Auth:CalDavCredentials:0:WritableCalendars:0"] = "smart-inbox",
                        ["Auth:CalDavCredentials:0:Scopes:0"] = HearthCalendarAuth.CalDavWriteScope,
                        ["Auth:CalDavCredentials:1:Name"] = CalDavReadUser,
                        ["Auth:CalDavCredentials:1:SecretHash"] = HearthCalendarSecretHasher.Hash(CalDavReadPassword),
                        ["Auth:CalDavCredentials:1:Scopes:0"] = HearthCalendarAuth.CalDavReadScope
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHearthCalendarStore>();
                    services.AddSingleton<IHearthCalendarStore>(store);
                });
            });

    private static AuthenticationHeaderValue Basic(string user, string password)
    {
        var bytes = Encoding.UTF8.GetBytes($"{user}:{password}");

        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    private static StringContent IcsContent(string content) =>
        new(content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal), Encoding.UTF8, "text/calendar");

    private static HttpRequestMessage PropFind(string uri, string depth = "0")
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), uri);
        request.Headers.TryAddWithoutValidation("Depth", depth);

        return request;
    }

    private static IReadOnlyList<CalDavDiscoveryResponse> NormalizeDiscoveryXml(XDocument document)
    {
        XNamespace dav = "DAV:";
        XNamespace calDav = "urn:ietf:params:xml:ns:caldav";

        return document
            .Descendants(dav + "response")
            .Select(response => new CalDavDiscoveryResponse(
                response.Element(dav + "href")?.Value ?? string.Empty,
                response.Descendants(dav + "status").SingleOrDefault()?.Value ?? string.Empty,
                response.Descendants(dav + "displayname").SingleOrDefault()?.Value ?? string.Empty,
                response.Descendants(dav + "current-user-principal").Elements(dav + "href").SingleOrDefault()?.Value ?? string.Empty,
                response.Descendants(calDav + "calendar-home-set").Elements(dav + "href").SingleOrDefault()?.Value ?? string.Empty,
                response
                    .Descendants(dav + "resourcetype")
                    .Elements()
                    .Select(element => element.Name.LocalName)
                    .Order()
                    .ToArray(),
                response
                    .Descendants(calDav + "comp")
                    .Select(element => element.Attribute("name")?.Value)
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .Order()
                    .ToArray(),
                response
                    .Descendants(dav + "privilege")
                    .Elements()
                    .Select(element => element.Name.LocalName)
                    .Order()
                    .ToArray()))
            .OrderBy(response => response.Href)
            .ToArray();
    }

    private sealed record CalDavDiscoveryResponse(
        string Href,
        string Status,
        string DisplayName,
        string PrincipalHref,
        string CalendarHomeSet,
        IReadOnlyList<string> ResourceTypes,
        IReadOnlyList<string> Components,
        IReadOnlyList<string> Privileges);

    private static string BasicIcs() =>
        """
        BEGIN:VCALENDAR
        BEGIN:VEVENT
        SUMMARY:Family planning
        DTSTART:20260801T100000Z
        DTEND:20260801T110000Z
        END:VEVENT
        END:VCALENDAR
        """;

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
        ContainsRawCalDavPassword = ContainsValue(audit.Metadata, CalDavPassword),
        ContainsRawWriteToken = ContainsValue(audit.Metadata, WriteToken),
        ContainsRawFeedToken = ContainsValue(audit.Metadata, FeedToken)
    };

    private static bool ContainsValue(IReadOnlyDictionary<string, string>? metadata, string value) =>
        metadata?.Values.Any(candidate => string.Equals(candidate, value, StringComparison.Ordinal)) == true;

    private sealed class RecordingCalDavStore : IHearthCalendarStore
    {
        public List<EventIntent> Intents { get; } = [];

        public List<AuditEntry> Audits { get; } = [];

        public Dictionary<string, RecordingCalDavObject> Objects { get; } = new(StringComparer.Ordinal);

        public Task StoreIntentAsync(EventIntent intent, CancellationToken cancellationToken)
        {
            Intents.Add(intent);

            return Task.CompletedTask;
        }

        public Task<CalDavObjectUpsertResult> UpsertCalDavObjectAsync(
            CalDavObjectUpsert upsert,
            CancellationToken cancellationToken)
        {
            var id = CalDavObjectDocumentId.Create(upsert.CalendarId, upsert.ItemId);
            Objects.TryGetValue(id, out var current);
            if (!PreconditionsAllowWrite(upsert, current))
            {
                return Task.FromResult(new CalDavObjectUpsertResult(
                    CalDavObjectUpsertStatus.PreconditionFailed,
                    current?.IntentId,
                    current?.ETag));
            }

            if (current is not null &&
                string.Equals(current.ContentHash, upsert.ContentHash, StringComparison.Ordinal))
            {
                return Task.FromResult(new CalDavObjectUpsertResult(
                    CalDavObjectUpsertStatus.Unchanged,
                    current.IntentId,
                    current.ETag));
            }

            Intents.Add(upsert.Intent);
            Audits.Add(upsert.AuditEntry);
            Objects[id] = new RecordingCalDavObject(
                id,
                upsert.CalendarId,
                upsert.ItemId,
                upsert.Intent.Id,
                upsert.ContentHash,
                upsert.ETag,
                current?.CreatedAt ?? upsert.ObservedAt,
                upsert.ObservedAt);

            return Task.FromResult(new CalDavObjectUpsertResult(
                current is null ? CalDavObjectUpsertStatus.Created : CalDavObjectUpsertStatus.Replaced,
                upsert.Intent.Id,
                upsert.ETag));
        }

        private static bool PreconditionsAllowWrite(
            CalDavObjectUpsert upsert,
            RecordingCalDavObject? current)
        {
            if (upsert.IfNoneMatchAny && current is not null)
            {
                return false;
            }

            if (current is not null && upsert.IfNoneMatchETags.Contains(current.ETag, StringComparer.Ordinal))
            {
                return false;
            }

            if (upsert.IfMatchAny && current is null)
            {
                return false;
            }

            if (upsert.IfMatchAny)
            {
                return true;
            }

            if (upsert.IfMatchETags.Count == 0)
            {
                return true;
            }

            return current is not null && upsert.IfMatchETags.Contains(current.ETag, StringComparer.Ordinal);
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

        public Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Audits);
    }

    private static object DescribeObject(RecordingCalDavObject calDavObject) => new
    {
        calDavObject.Id,
        calDavObject.CalendarId,
        calDavObject.ItemId,
        HasIntentLink = calDavObject.IntentId.Value != Guid.Empty,
        HasContentHash = !string.IsNullOrWhiteSpace(calDavObject.ContentHash),
        calDavObject.ETag,
        HasCreatedAt = calDavObject.CreatedAt != default,
        HasUpdatedAt = calDavObject.UpdatedAt != default,
        ContainsRawCalDavPassword = calDavObject.ContentHash.Contains(CalDavPassword, StringComparison.Ordinal),
        ContainsRawWriteToken = calDavObject.ContentHash.Contains(WriteToken, StringComparison.Ordinal),
        ContainsRawFeedToken = calDavObject.ContentHash.Contains(FeedToken, StringComparison.Ordinal)
    };

    private sealed record RecordingCalDavObject(
        string Id,
        string CalendarId,
        string ItemId,
        EventIntentId IntentId,
        string ContentHash,
        string ETag,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
