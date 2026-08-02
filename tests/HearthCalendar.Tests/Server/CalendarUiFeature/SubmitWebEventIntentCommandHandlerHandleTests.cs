using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HearthCalendar.Tests.Server;

public sealed class SubmitWebEventIntentCommandHandlerHandleTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Submit_command_persists_review_outcome_before_publishing_notifications()
    {
        var store = new RecordingStore();
        var notifier = new RecordingNotifier(store);
        var handler = new SubmitWebEventIntentCommandHandler(
            store,
            notifier,
            Array.Empty<IValidator<SubmitWebEventIntentCommand>>(),
            NullLogger<SubmitWebEventIntentCommandHandler>.Instance);

        var result = await handler.Handle(
            new SubmitWebEventIntentCommand("Adult A dentist", Today, new TimeOnly(9, 0), new TimeOnly(9, 30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.True(notifier.StoreHadPersistedDecisionWhenPublished);
        Assert.NotEmpty(notifier.Published);
    }
}
