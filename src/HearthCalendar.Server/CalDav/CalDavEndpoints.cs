using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Domain;

namespace HearthCalendar.Server.CalDav;

public static class CalDavEndpoints
{
    private const int MaxIcsContentBytes = 64 * 1024;
    private const string SmartInboxCalendar = "smart-inbox";

    public static IEndpointRouteBuilder MapCalDavEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/caldav")
            .RequireAuthorization(HearthCalendarAuth.CalDavWritePolicy);

        group.MapMethods(
            "/calendars/{calendarId}/{itemId}.ics",
            [HttpMethods.Put],
            PutCalendarObjectAsync);

        return endpoints;
    }

    private static async Task<IResult> PutCalendarObjectAsync(
        string calendarId,
        string itemId,
        HttpRequest request,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
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
                ["itemId"] = itemId
            });

        await store.StoreIntentWithAuditAsync(intent, audit, cancellationToken);

        return Results.Created(
            $"/caldav/calendars/{SmartInboxCalendar}/{itemId}.ics",
            new IntakeEventResponse(intent.Id.Value, "accepted"));
    }

    private static bool CanWriteCalendar(ClaimsPrincipal user, string calendarId) =>
        user.Claims.Any(claim =>
            claim.Type == HearthCalendarAuth.AllowedCalendarClaim &&
            string.Equals(claim.Value, calendarId, StringComparison.OrdinalIgnoreCase));

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
