using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class EditReviewItemCommandHandlerHandleTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Edit_command_preserves_original_intent_and_creates_revised_intent()
    {
        var intent = Intent("Adult A dentist", new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var decision = StagedDecision(intent, CandidateEvent());
        var store = new RecordingStore();
        store.Intents.Add(intent);
        store.Decisions.Add(decision);
        var notifier = new RecordingNotifier(store);
        var handler = new EditReviewItemCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<EditReviewItemCommand>>(),
            NullLogger<EditReviewItemCommandHandler>.Instance);

        var result = await handler.Handle(
            new EditReviewItemCommand(decision.Id.Value, "Adult B dentist", Today, new TimeOnly(10, 0), new TimeOnly(10, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Contains(store.Intents, saved => saved.Id == intent.Id && saved.RawText == "Adult A dentist");
        Assert.Contains(store.Intents, saved => saved.Id != intent.Id && saved.RawText == "Adult B dentist");
        Assert.Contains(store.Intents, saved =>
            saved.Id != intent.Id &&
            saved.Payload?.Date == Today &&
            saved.Payload?.StartTime == new TimeOnly(10, 0) &&
            saved.Payload?.EndTime == new TimeOnly(10, 30));
    }
}
