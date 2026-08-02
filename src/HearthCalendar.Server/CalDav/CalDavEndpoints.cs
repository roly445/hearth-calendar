using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Feeds;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Domain;

namespace HearthCalendar.Server.CalDav;

public static class CalDavEndpoints
{
    private const int MaxIcsContentBytes = 64 * 1024;
    private const string SmartInboxCalendar = "smart-inbox";
    private const string DavNamespace = "DAV:";
    private const string CalDavNamespace = "urn:ietf:params:xml:ns:caldav";
    private static readonly IReadOnlyList<CalDavCalendarDescriptor> Calendars =
    [
        new(SmartInboxCalendar, "Smart Inbox", IsWritable: true),
        new("combined", "Combined", IsWritable: false),
        new("adult-a", "Adult A", IsWritable: false),
        new("adult-b", "Adult B", IsWritable: false),
        new("child", "Child", IsWritable: false),
        new("family", "Family", IsWritable: false),
        new("events", "Events", IsWritable: false)
    ];
    private static readonly IReadOnlyDictionary<string, VirtualCalendar> ReadOnlyVirtualCalendars =
        new Dictionary<string, VirtualCalendar>(StringComparer.OrdinalIgnoreCase)
        {
            ["combined"] = VirtualCalendar.Combined,
            ["adult-a"] = VirtualCalendar.AdultA,
            ["adult-b"] = VirtualCalendar.AdultB,
            ["child"] = VirtualCalendar.Child,
            ["family"] = VirtualCalendar.Family,
            ["events"] = VirtualCalendar.Events
        };

    public static IEndpointRouteBuilder MapCalDavEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/caldav");

        group.MapMethods("/", [HttpMethods.Options], OptionsAsync)
            .RequireAuthorization(HearthCalendarAuth.CalDavReadPolicy);
        group.MapMethods("/{**path}", [HttpMethods.Options], OptionsAsync)
            .RequireAuthorization(HearthCalendarAuth.CalDavReadPolicy);
        group.MapMethods("/", ["PROPFIND"], PropFindAsync)
            .RequireAuthorization(HearthCalendarAuth.CalDavReadPolicy);
        group.MapMethods("/{**path}", ["PROPFIND"], PropFindAsync)
            .RequireAuthorization(HearthCalendarAuth.CalDavReadPolicy);
        group.MapMethods(
            "/calendars/{calendarId}/{itemId}.ics",
            [HttpMethods.Get],
            GetCalendarObjectAsync)
            .RequireAuthorization(HearthCalendarAuth.CalDavReadPolicy);
        group.MapMethods(
            "/calendars/{calendarId}/",
            ["REPORT"],
            ReportCalendarQueryAsync)
            .RequireAuthorization(HearthCalendarAuth.CalDavReadPolicy);
        group.MapMethods(
            "/calendars/{calendarId}/{itemId}.ics",
            [HttpMethods.Put],
            PutCalendarObjectAsync)
            .RequireAuthorization(HearthCalendarAuth.CalDavWritePolicy);

        return endpoints;
    }

    private static Task<IResult> OptionsAsync(HttpRequest request)
    {
        var path = NormalizeCalDavPath(request.Path.Value);
        var allow = IsSmartInboxPath(path)
            ? "OPTIONS, PROPFIND, PUT"
            : "OPTIONS, PROPFIND";

        return Task.FromResult<IResult>(new CalDavOptionsResult(allow));
    }

    private static Task<IResult> PropFindAsync(HttpRequest request, ClaimsPrincipal user)
    {
        var path = NormalizeCalDavPath(request.Path.Value);
        if (path.Length == 0)
        {
            return Task.FromResult<IResult>(new CalDavXmlResult(MultiStatus(
                Response(
                    "/caldav/",
                    PropStatOk(
                        DisplayName("Hearth Calendar CalDAV"),
                        ResourceType(Collection(), Principal()),
                        CurrentUserPrincipal(),
                        CalendarHomeSet())))));
        }

        if (string.Equals(path, "calendars", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IResult>(new CalDavXmlResult(MultiStatus(
                Calendars.Select(calendar => Response(
                    $"/caldav/calendars/{calendar.Id}/",
                    PropStatOk(CalendarProperties(calendar, user)))))));
        }

        const string calendarPrefix = "calendars/";
        if (!path.StartsWith(calendarPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IResult>(Results.NotFound());
        }

        var calendarId = path[calendarPrefix.Length..];
        var calendar = Calendars.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, calendarId, StringComparison.OrdinalIgnoreCase));

        return calendar is null
            ? Task.FromResult<IResult>(Results.NotFound())
            : Task.FromResult<IResult>(new CalDavXmlResult(MultiStatus(
                Response(
                    $"/caldav/calendars/{calendar.Id}/",
                    PropStatOk(CalendarProperties(calendar, user))))));
    }

    private static string NormalizeCalDavPath(string? path)
    {
        const string calDavPrefix = "/caldav";
        var normalized = path ?? string.Empty;
        if (normalized.StartsWith(calDavPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[calDavPrefix.Length..];
        }

        return normalized.Trim('/');
    }

    private static bool IsSmartInboxPath(string path) =>
        string.Equals(path, $"calendars/{SmartInboxCalendar}", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith($"calendars/{SmartInboxCalendar}/", StringComparison.OrdinalIgnoreCase);

    private static async Task<IResult> PutCalendarObjectAsync(
        string calendarId,
        string itemId,
        HttpRequest request,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
        ICalendarUpdateNotifier notifier,
        IAiReviewProvider aiReviewProvider,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(calendarId, SmartInboxCalendar, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (!CanWriteCalendar(user, SmartInboxCalendar))
        {
            return Results.Forbid();
        }

        var content = await ReadRequestBodyAsync(request, cancellationToken);
        if (content is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var parseResult = CalDavEventParser.Parse(content);
        if (parseResult is null)
        {
            return Results.BadRequest(new IntakeErrorResponse("caldav_event_invalid"));
        }

        var submittedBy = new ActorRef(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-caldav-client");
        var now = DateTimeOffset.UtcNow;
        var contentHash = CalDavContentHasher.Hash(content);
        var eTag = CalDavContentHasher.ToETag(contentHash);
        var intent = new EventIntent(
            EventIntentId.New(),
            CalendarSource.CalDav,
            ReviewSourceMode.Passive,
            parseResult.Summary,
            new EventIntentPayload(parseResult.Date, parseResult.StartTime, parseResult.EndTime),
            now,
            submittedBy);
        var audit = new AuditEntry(
            AuditEntryId.New(),
            AuditAction.IntakeIntentSubmitted,
            submittedBy,
            now,
            "CalDAV Smart Inbox intent submitted.",
            intent.Id,
            Metadata: new Dictionary<string, string>
            {
                ["source"] = CalendarSource.CalDav.ToString(),
                ["mode"] = ReviewSourceMode.Passive.ToString(),
                ["tokenKind"] = user.FindFirstValue(HearthCalendarAuth.TokenKindClaim) ?? "unknown",
                ["calendar"] = SmartInboxCalendar,
                ["itemId"] = itemId,
                ["etag"] = eTag
            });
        var existingEvents = await store.QueryApprovedEventsAsync(
            parseResult.Date,
            parseResult.Date,
            VirtualCalendar.Combined,
            cancellationToken);
        ValueTask<ReviewOutcome> ReviewAsync(CancellationToken token) =>
            new DeterministicEventReviewPipeline(
                DateOnly.FromDateTime(now.UtcDateTime),
                existingEvents,
                aiReviewProvider)
            .ReviewWithAuditAsync(intent, token);

        var result = await store.UpsertCalDavObjectAsync(
            new CalDavObjectUpsert(
                SmartInboxCalendar,
                itemId,
                contentHash,
                eTag,
                intent,
                audit,
                ReviewAsync,
                now,
                ReadIfMatchETags(request),
                ReadIfMatchAny(request),
                ReadIfNoneMatchETags(request),
                ReadIfNoneMatchAny(request)),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.ETag))
        {
            request.HttpContext.Response.Headers.ETag = result.ETag;
        }

        if (result.Status is CalDavObjectUpsertStatus.Created or CalDavObjectUpsertStatus.Replaced)
        {
            await notifier.PublishAsync(
                CalendarUiNotifications.For(result.ReviewDecision!),
                cancellationToken);
        }

        return result.Status switch
        {
            CalDavObjectUpsertStatus.Created => Results.Created(
                $"/caldav/calendars/{SmartInboxCalendar}/{itemId}.ics",
                new IntakeEventResponse(result.IntentId!.Value.Value, "accepted")),
            CalDavObjectUpsertStatus.Replaced => Results.Ok(
                new IntakeEventResponse(result.IntentId!.Value.Value, "accepted")),
            CalDavObjectUpsertStatus.Unchanged => Results.NoContent(),
            CalDavObjectUpsertStatus.PreconditionFailed => Results.StatusCode(StatusCodes.Status412PreconditionFailed),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static bool CanWriteCalendar(ClaimsPrincipal user, string calendarId) =>
        user.Claims.Any(claim =>
            claim.Type == HearthCalendarAuth.CalDavWritableCalendarClaim &&
            string.Equals(claim.Value, calendarId, StringComparison.OrdinalIgnoreCase));

    private static async Task<IResult> GetCalendarObjectAsync(
        string calendarId,
        string itemId,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
        CancellationToken cancellationToken)
    {
        if (!TryGetReadOnlyVirtualCalendar(calendarId, out var virtualCalendar))
        {
            return Results.NotFound();
        }

        if (!CanReadCalendar(user, calendarId))
        {
            return Results.Forbid();
        }

        if (!Guid.TryParse(itemId, out var parsedItemId))
        {
            return Results.NotFound();
        }

        var calendarEvent = await store.LoadApprovedEventAsync(
            new CalendarEventId(parsedItemId),
            virtualCalendar,
            cancellationToken);
        if (calendarEvent is null)
        {
            return Results.NotFound();
        }

        var calendarData = IcsFeedWriter.Write(virtualCalendar, [calendarEvent]);
        var eTag = CalDavContentHasher.ToETag(CalDavContentHasher.Hash(calendarData));

        return new CalDavIcsResult(calendarData, eTag);
    }

    private static async Task<IResult> ReportCalendarQueryAsync(
        string calendarId,
        HttpRequest request,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
        CancellationToken cancellationToken)
    {
        if (!TryGetReadOnlyVirtualCalendar(calendarId, out var virtualCalendar))
        {
            return Results.NotFound();
        }

        if (!CanReadCalendar(user, calendarId))
        {
            return Results.Forbid();
        }

        var (from, to) = await ReadCalendarQueryRangeAsync(request, cancellationToken);
        var events = await store.QueryApprovedEventsAsync(
            from,
            to,
            virtualCalendar,
            cancellationToken);

        return new CalDavXmlResult(MultiStatus(events.Select(calendarEvent =>
        {
            var calendarData = IcsFeedWriter.Write(virtualCalendar, [calendarEvent]);

            return Response(
                $"/caldav/calendars/{calendarId.ToLowerInvariant()}/{calendarEvent.Id.Value}.ics",
                PropStatOk(
                    GetETag(CalDavContentHasher.ToETag(CalDavContentHasher.Hash(calendarData))),
                    CalendarData(calendarData)));
        })));
    }

    private static bool TryGetReadOnlyVirtualCalendar(
        string calendarId,
        out VirtualCalendar virtualCalendar) =>
        ReadOnlyVirtualCalendars.TryGetValue(calendarId, out virtualCalendar);

    private static bool CanReadCalendar(ClaimsPrincipal user, string calendarId) =>
        user.Claims.Any(claim =>
            claim.Type == HearthCalendarAuth.CalDavReadableCalendarClaim &&
            string.Equals(claim.Value, calendarId, StringComparison.OrdinalIgnoreCase));

    private static async Task<(DateOnly From, DateOnly To)> ReadCalendarQueryRangeAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fallback = (From: today.AddYears(-1), To: today.AddYears(3));
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            var document = XDocument.Parse(body);
            var timeRange = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "time-range", StringComparison.OrdinalIgnoreCase));
            if (timeRange is null)
            {
                return fallback;
            }

            var from = ParseCalDavRangeBoundary(timeRange.Attribute("start")?.Value)?.Date ?? fallback.From;
            var end = ParseCalDavRangeBoundary(timeRange.Attribute("end")?.Value);
            var to = end is null
                ? fallback.To
                : end.IsDateOnly || end.Time == TimeOnly.MinValue
                    ? end.Date.AddDays(-1)
                    : end.Date;

            return from <= to ? (from, to) : fallback;
        }
        catch (System.Xml.XmlException)
        {
            return fallback;
        }
    }

    private static CalDavRangeBoundary? ParseCalDavRangeBoundary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.TrimEnd('Z');
        if (DateTime.TryParseExact(
            normalized,
            "yyyyMMdd'T'HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dateTime))
        {
            return new CalDavRangeBoundary(
                DateOnly.FromDateTime(dateTime),
                TimeOnly.FromDateTime(dateTime),
                IsDateOnly: false);
        }

        return DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new CalDavRangeBoundary(date, null, IsDateOnly: true)
            : null;
    }

    private static IReadOnlyList<string> ReadIfMatchETags(HttpRequest request) =>
        request.Headers.IfMatch
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Where(value => value != "*")
            .ToArray();

    private static bool ReadIfMatchAny(HttpRequest request) =>
        request.Headers.IfMatch
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Any(value => value == "*");

    private static IReadOnlyList<string> ReadIfNoneMatchETags(HttpRequest request) =>
        request.Headers.IfNoneMatch
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Where(value => value != "*")
            .ToArray();

    private static bool ReadIfNoneMatchAny(HttpRequest request) =>
        request.Headers.IfNoneMatch
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Any(value => value == "*");

    private static XElement[] CalendarProperties(CalDavCalendarDescriptor calendar, ClaimsPrincipal user)
    {
        var privileges = CurrentUserPrivileges(calendar, user);

        return
        [
            DisplayName(calendar.DisplayName),
            ResourceType(Collection(), Calendar()),
            SupportedCalendarComponentSet(),
            CurrentUserPrivilegeSet(privileges)
        ];
    }

    private static XElement[] CurrentUserPrivileges(CalDavCalendarDescriptor calendar, ClaimsPrincipal user)
    {
        if (calendar.IsWritable && CanWriteCalendar(user, calendar.Id))
        {
            return [WritePrivilege()];
        }

        if (!calendar.IsWritable && CanReadCalendar(user, calendar.Id))
        {
            return [ReadPrivilege()];
        }

        return [];
    }

    private static XDocument MultiStatus(params XElement[] responses) =>
        MultiStatus((IEnumerable<XElement>)responses);

    private static XDocument MultiStatus(IEnumerable<XElement> responses)
    {
        XNamespace dav = DavNamespace;

        return new XDocument(new XElement(dav + "multistatus", responses));
    }

    private static XElement Response(string href, XElement propStat)
    {
        XNamespace dav = DavNamespace;

        return new XElement(
            dav + "response",
            new XElement(dav + "href", href),
            propStat);
    }

    private static XElement PropStatOk(params XElement[] properties)
    {
        XNamespace dav = DavNamespace;

        return new XElement(
            dav + "propstat",
            new XElement(dav + "prop", properties),
            new XElement(dav + "status", "HTTP/1.1 200 OK"));
    }

    private static XElement DisplayName(string value)
    {
        XNamespace dav = DavNamespace;

        return new XElement(dav + "displayname", value);
    }

    private static XElement GetETag(string value)
    {
        XNamespace dav = DavNamespace;

        return new XElement(dav + "getetag", value);
    }

    private static XElement CalendarData(string value)
    {
        XNamespace calDav = CalDavNamespace;

        return new XElement(calDav + "calendar-data", value);
    }

    private static XElement ResourceType(params XElement[] resourceTypes)
    {
        XNamespace dav = DavNamespace;

        return new XElement(dav + "resourcetype", resourceTypes);
    }

    private static XElement Collection()
    {
        XNamespace dav = DavNamespace;

        return new XElement(dav + "collection");
    }

    private static XElement Principal()
    {
        XNamespace dav = DavNamespace;

        return new XElement(dav + "principal");
    }

    private static XElement CurrentUserPrincipal()
    {
        XNamespace dav = DavNamespace;

        return new XElement(
            dav + "current-user-principal",
            new XElement(dav + "href", "/caldav/principals/current/"));
    }

    private static XElement CalendarHomeSet()
    {
        XNamespace calDav = CalDavNamespace;
        XNamespace dav = DavNamespace;

        return new XElement(
            calDav + "calendar-home-set",
            new XElement(dav + "href", "/caldav/calendars/"));
    }

    private static XElement CurrentUserPrivilegeSet(params XElement[] privileges)
    {
        XNamespace dav = DavNamespace;

        return new XElement(dav + "current-user-privilege-set", privileges);
    }

    private static XElement ReadPrivilege() => Privilege("read");

    private static XElement WritePrivilege() => Privilege("write");

    private static XElement Privilege(string privilegeName)
    {
        XNamespace dav = DavNamespace;

        return new XElement(
            dav + "privilege",
            new XElement(dav + privilegeName));
    }

    private static XElement Calendar()
    {
        XNamespace calDav = CalDavNamespace;

        return new XElement(calDav + "calendar");
    }

    private static XElement SupportedCalendarComponentSet()
    {
        XNamespace calDav = CalDavNamespace;

        return new XElement(
            calDav + "supported-calendar-component-set",
            new XElement(calDav + "comp", new XAttribute("name", "VEVENT")));
    }

    private static async Task<string?> ReadRequestBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxIcsContentBytes)
        {
            return null;
        }

        using var content = new MemoryStream();
        var buffer = new byte[8192];
        var totalBytesRead = 0;
        while (true)
        {
            var bytesRead = await request.Body.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
            if (totalBytesRead > MaxIcsContentBytes)
            {
                return null;
            }

            content.Write(buffer, 0, bytesRead);
        }

        return Encoding.UTF8.GetString(content.ToArray());
    }
}

public sealed record CalDavCalendarDescriptor(string Id, string DisplayName, bool IsWritable);

public sealed record CalDavRangeBoundary(DateOnly Date, TimeOnly? Time, bool IsDateOnly);

public static class CalDavContentHasher
{
    public static string Hash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ToETag(string contentHash) => $"\"{contentHash}\"";
}

public sealed class CalDavOptionsResult(string allow) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        httpContext.Response.Headers.Allow = allow;
        httpContext.Response.Headers.Append("DAV", "1, 3, calendar-access");

        return Task.CompletedTask;
    }
}

public sealed class CalDavXmlResult(XDocument document) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status207MultiStatus;
        httpContext.Response.ContentType = "application/xml; charset=utf-8";
        httpContext.Response.Headers.Append("DAV", "1, 3, calendar-access");

        return httpContext.Response.WriteAsync(
            document.ToString(SaveOptions.DisableFormatting),
            Encoding.UTF8,
            httpContext.RequestAborted);
    }
}

public sealed class CalDavIcsResult(string content, string eTag) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/calendar; charset=utf-8";
        httpContext.Response.Headers.ETag = eTag;

        return httpContext.Response.WriteAsync(
            content,
            Encoding.UTF8,
            httpContext.RequestAborted);
    }
}

public static partial class CalDavEventParser
{
    private static readonly Regex LineBreakPattern = CreateLineBreakPattern();

    public static CalDavParsedEvent? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var lines = UnfoldLines(content);
        if (!lines.Contains("BEGIN:VEVENT", StringComparer.OrdinalIgnoreCase) ||
            !lines.Contains("END:VEVENT", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var summary = ReadProperty(lines, "SUMMARY");
        var dtStart = ReadProperty(lines, "DTSTART");
        if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(dtStart))
        {
            return null;
        }

        var start = ParseIcsDateTime(dtStart);
        if (start is null)
        {
            return null;
        }

        var dtEnd = ReadProperty(lines, "DTEND");
        var end = string.IsNullOrWhiteSpace(dtEnd) ? null : ParseIcsDateTime(dtEnd);
        if (!string.IsNullOrWhiteSpace(dtEnd) && end is null)
        {
            return null;
        }

        return new CalDavParsedEvent(
            summary.Trim(),
            start.Date,
            start.IsAllDay ? null : start.Time,
            end is null || end.IsAllDay ? null : end.Time);
    }

    private static IReadOnlyList<string> UnfoldLines(string content)
    {
        var rawLines = LineBreakPattern.Split(content.Trim());
        var lines = new List<string>();
        foreach (var rawLine in rawLines)
        {
            if (rawLine.StartsWith(' ') || rawLine.StartsWith('\t'))
            {
                if (lines.Count > 0)
                {
                    lines[^1] += rawLine.TrimStart();
                }

                continue;
            }

            lines.Add(rawLine);
        }

        return lines;
    }

    private static string? ReadProperty(IReadOnlyList<string> lines, string propertyName)
    {
        var prefix = propertyName + ":";
        var parameterPrefix = propertyName + ";";
        var line = lines.FirstOrDefault(candidate =>
            candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(parameterPrefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return null;
        }

        var separator = line.IndexOf(':', StringComparison.Ordinal);

        return separator < 0 ? null : line[(separator + 1)..];
    }

    private static ParsedIcsDateTime? ParseIcsDateTime(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new ParsedIcsDateTime(date, null, true);
        }

        var normalized = value.TrimEnd('Z');
        if (!DateTime.TryParseExact(
            normalized,
            "yyyyMMdd'T'HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dateTime))
        {
            return null;
        }

        return new ParsedIcsDateTime(
            DateOnly.FromDateTime(dateTime),
            TimeOnly.FromDateTime(dateTime),
            false);
    }

    [GeneratedRegex("\r\n|\n|\r")]
    private static partial Regex CreateLineBreakPattern();

    private sealed record ParsedIcsDateTime(DateOnly Date, TimeOnly? Time, bool IsAllDay);
}

public sealed record CalDavParsedEvent(
    string Summary,
    DateOnly Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime);
