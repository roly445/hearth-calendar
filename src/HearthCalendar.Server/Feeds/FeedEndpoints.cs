using System.Security.Claims;
using System.Text;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Domain;
using Microsoft.AspNetCore.Mvc;

namespace HearthCalendar.Server.Feeds;

public static class FeedEndpoints
{
    private static readonly IReadOnlyDictionary<string, VirtualCalendar> FeedCalendars =
        new Dictionary<string, VirtualCalendar>(StringComparer.OrdinalIgnoreCase)
        {
            ["combined"] = VirtualCalendar.Combined,
            ["adult-a"] = VirtualCalendar.AdultA,
            ["adult-b"] = VirtualCalendar.AdultB,
            ["child"] = VirtualCalendar.Child,
            ["family"] = VirtualCalendar.Family,
            ["events"] = VirtualCalendar.Events
        };

    public static IEndpointRouteBuilder MapFeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("/feeds/{calendar}.ics", GetFeedAsync)
            .RequireAuthorization(HearthCalendarAuth.FeedReadPolicy);

        return endpoints;
    }

    private static async Task<IResult> GetFeedAsync(
        [FromRoute] string calendar,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
        CancellationToken cancellationToken)
    {
        if (!FeedCalendars.TryGetValue(calendar, out var virtualCalendar))
        {
            return Results.NotFound(new FeedErrorResponse("feed_not_found"));
        }

        if (!CanReadCalendar(user, virtualCalendar))
        {
            return Results.Forbid();
        }

        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);
        var to = from.AddYears(3);
        var events = await store.QueryApprovedEventsAsync(
            from,
            to,
            virtualCalendar,
            cancellationToken);
        var feed = IcsFeedWriter.Write(virtualCalendar, events);

        return Results.Text(feed, "text/calendar; charset=utf-8", Encoding.UTF8);
    }

    private static bool CanReadCalendar(ClaimsPrincipal user, VirtualCalendar calendar)
    {
        var allowedCalendars = user
            .FindAll(HearthCalendarAuth.AllowedCalendarClaim)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allowedCalendars.Contains(calendar.ToString());
    }
}

public sealed record FeedErrorResponse(string Code);

public static class IcsFeedWriter
{
    public static string Write(VirtualCalendar calendar, IReadOnlyList<CalendarEvent> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BEGIN:VCALENDAR");
        builder.AppendLine("VERSION:2.0");
        builder.AppendLine("PRODID:-//Hearth Calendar//EN");
        builder.AppendLine("CALSCALE:GREGORIAN");
        builder.AppendLine($"X-WR-CALNAME:{EscapeText($"Hearth {calendar}")}");

        foreach (var calendarEvent in events.OrderBy(item => item.Time.Date).ThenBy(item => item.Title))
        {
            AppendEvent(builder, calendarEvent);
        }

        builder.AppendLine("END:VCALENDAR");

        return builder.ToString().ReplaceLineEndings("\r\n");
    }

    private static void AppendEvent(StringBuilder builder, CalendarEvent calendarEvent)
    {
        builder.AppendLine("BEGIN:VEVENT");
        builder.AppendLine($"UID:{calendarEvent.Id.Value}@hearth-calendar");
        builder.AppendLine("DTSTAMP:20260730T000000Z");
        AppendDateTime(builder, "DTSTART", calendarEvent.Time.Date, calendarEvent.Time.StartTime, calendarEvent.Time.IsAllDay);
        AppendDateTime(
            builder,
            "DTEND",
            calendarEvent.Time.IsAllDay ? calendarEvent.Time.Date.AddDays(1) : calendarEvent.Time.Date,
            calendarEvent.Time.EndTime,
            calendarEvent.Time.IsAllDay);

        if (calendarEvent.Recurrence?.Frequency == RecurrenceFrequency.Yearly)
        {
            builder.AppendLine("RRULE:FREQ=YEARLY");
        }

        builder.AppendLine($"SUMMARY:{EscapeText(calendarEvent.Title)}");
        builder.AppendLine($"CATEGORIES:{EscapeText(calendarEvent.Category.ToString())}");
        builder.AppendLine(calendarEvent.BusyStatus == BusyStatus.Free ? "TRANSP:TRANSPARENT" : "TRANSP:OPAQUE");
        builder.AppendLine(calendarEvent.BusyStatus == BusyStatus.Free
            ? "X-MICROSOFT-CDO-BUSYSTATUS:FREE"
            : "X-MICROSOFT-CDO-BUSYSTATUS:BUSY");
        builder.AppendLine("END:VEVENT");
    }

    private static void AppendDateTime(
        StringBuilder builder,
        string property,
        DateOnly date,
        TimeOnly? time,
        bool isAllDay)
    {
        if (isAllDay)
        {
            builder.AppendLine($"{property};VALUE=DATE:{date:yyyyMMdd}");
            return;
        }

        var value = date.ToDateTime(time ?? TimeOnly.MinValue);
        builder.AppendLine($"{property}:{value:yyyyMMdd'T'HHmmss}");
    }

    private static string EscapeText(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace(";", @"\;", StringComparison.Ordinal)
            .Replace(",", @"\,", StringComparison.Ordinal)
            .ReplaceLineEndings(@"\n");
}
