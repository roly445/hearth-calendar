using System.Security.Claims;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.Domain;
using Microsoft.AspNetCore.Mvc;

namespace HearthCalendar.Server.Intake;

public static class IntakeEndpoints
{
    public static IEndpointRouteBuilder MapIntakeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/intake")
            .RequireAuthorization(HearthCalendarAuth.IntakeWritePolicy);

        group.MapPost("/event", SubmitGenericEventIntent);
        group.MapPost("/home-assistant/event", SubmitHomeAssistantEventIntent);

        return endpoints;
    }

    private static Task<IResult> SubmitGenericEventIntent(
        [FromBody] IntakeEventRequest request,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
        CancellationToken cancellationToken) =>
        SubmitIntentAsync(request, CalendarSource.Web, user, store, cancellationToken);

    private static Task<IResult> SubmitHomeAssistantEventIntent(
        [FromBody] IntakeEventRequest request,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
        CancellationToken cancellationToken) =>
        SubmitIntentAsync(request, CalendarSource.HomeAssistant, user, store, cancellationToken);

    private static async Task<IResult> SubmitIntentAsync(
        IntakeEventRequest request,
        CalendarSource source,
        ClaimsPrincipal user,
        IHearthCalendarStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RawText))
        {
            return Results.BadRequest(new IntakeErrorResponse("raw_text_required"));
        }

        if (!Enum.IsDefined(request.SourceMode))
        {
            return Results.BadRequest(new IntakeErrorResponse("source_mode_invalid"));
        }

        var submittedBy = new ActorRef(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-client");
        var intent = new EventIntent(
            EventIntentId.New(),
            source,
            request.SourceMode,
            request.RawText.Trim(),
            new EventIntentPayload(request.Date, request.StartTime, request.EndTime),
            DateTimeOffset.UtcNow,
            submittedBy);
        var audit = new AuditEntry(
            AuditEntryId.New(),
            AuditAction.IntakeIntentSubmitted,
            submittedBy,
            intent.SubmittedAt,
            "Intake intent submitted.",
            intent.Id,
            Metadata: new Dictionary<string, string>
            {
                ["source"] = source.ToString(),
                ["mode"] = request.SourceMode.ToString(),
                ["tokenKind"] = user.FindFirstValue(HearthCalendarAuth.TokenKindClaim) ?? "unknown"
            });

        await store.StoreIntentWithAuditAsync(intent, audit, cancellationToken);

        return Results.Accepted(
            $"/api/intake/event/{intent.Id.Value}",
            new IntakeEventResponse(intent.Id.Value, "accepted"));
    }
}

public sealed record IntakeEventRequest(
    string RawText,
    ReviewSourceMode SourceMode = ReviewSourceMode.Passive,
    DateOnly? Date = null,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null);

public sealed record IntakeEventResponse(Guid IntentId, string Status);

public sealed record IntakeErrorResponse(string Code);
