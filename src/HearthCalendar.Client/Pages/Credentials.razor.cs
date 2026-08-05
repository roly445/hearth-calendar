using BluQube.Commands;
using BluQube.Constants;
using BluQube.Queries;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Client.Features.CredentialManagement;
using HearthCalendar.Client.Services;
using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Pages;

public partial class Credentials
{
    private static readonly IReadOnlyList<string> FeedCalendarOptions =
        ["AdultA", "AdultB", "Child", "Family", "Events", "Combined"];

    private readonly CredentialManagementModel model = new();
    private IReadOnlyList<ClientCredentialMetadataDto> clientCredentials = [];
    private IReadOnlyList<FeedTokenMetadataDto> feedTokens = [];
    private IReadOnlyList<CalDavCredentialMetadataDto> calDavCredentials = [];
    private IGeneratedSecretResult? generatedSecret;
    private string? message;
    private bool isLoading;
    private bool isSubmitting;

    [Inject]
    private IQueryRunner QueryRunner { get; set; } = null!;

    [Inject]
    private ICommandRunner CommandRunner { get; set; } = null!;

    [Inject]
    private AdminSessionClient SessionClient { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var session = await SessionClient.GetSessionAsync();
        if (!session.IsAuthenticated)
        {
            Navigation.NavigateTo("/login");
            return;
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        isLoading = true;
        var result = await QueryRunner.Send(new GetCredentialInventoryQuery());
        if (result.Status == QueryResultStatus.Succeeded)
        {
            clientCredentials = result.Data.ClientCredentials;
            feedTokens = result.Data.FeedTokens;
            calDavCredentials = result.Data.CalDavCredentials;
            message = null;
        }
        else if (result.Status == QueryResultStatus.Empty)
        {
            clientCredentials = [];
            feedTokens = [];
            calDavCredentials = [];
            message = null;
        }
        else
        {
            message = result.Status == QueryResultStatus.Unauthorized
                ? "Sign in with admin access to manage credentials."
                : "Credential metadata is unavailable.";
        }

        isLoading = false;
    }

    private async Task CreateClientCredentialAsync() =>
        await RunSecretCommandAsync(new CreateClientCredentialCommand(model.ClientName, SplitCsv(model.ClientScopes)));

    private async Task CreateFeedTokenAsync() =>
        await RunSecretCommandAsync(new CreateFeedTokenCommand(model.FeedName, model.FeedCalendars));

    private async Task CreateCalDavCredentialAsync() =>
        await RunSecretCommandAsync(new CreateCalDavCredentialCommand(
            model.CalDavName,
            SplitCsv(model.CalDavReadableCalendars),
            SplitCsv(model.CalDavWritableCalendars)));

    private async Task RotateClientCredentialAsync(Guid id) =>
        await RunSecretCommandAsync(new RotateClientCredentialCommand(id));

    private async Task RotateFeedTokenAsync(Guid id) =>
        await RunSecretCommandAsync(new RotateFeedTokenCommand(id));

    private async Task RotateCalDavCredentialAsync(Guid id) =>
        await RunSecretCommandAsync(new RotateCalDavCredentialCommand(id));

    private async Task RevokeClientCredentialAsync(Guid id) =>
        await RunMutationCommandAsync(new RevokeClientCredentialCommand(id));

    private async Task RevokeFeedTokenAsync(Guid id) =>
        await RunMutationCommandAsync(new RevokeFeedTokenCommand(id));

    private async Task RevokeCalDavCredentialAsync(Guid id) =>
        await RunMutationCommandAsync(new RevokeCalDavCredentialCommand(id));

    private async Task RunSecretCommandAsync<TResult>(ICommand<TResult> command)
        where TResult : IGeneratedSecretResult
    {
        isSubmitting = true;
        generatedSecret = null;
        var result = await CommandRunner.Send(command);
        if (result.Status == CommandResultStatus.Succeeded)
        {
            generatedSecret = result.Data;
            message = result.Data.Message;
            await RefreshAsync();
        }
        else
        {
            message = CommandMessage(result.Status);
        }

        isSubmitting = false;
    }

    private async Task RunMutationCommandAsync<TResult>(ICommand<TResult> command)
        where TResult : ICredentialMutationResult
    {
        isSubmitting = true;
        generatedSecret = null;
        var result = await CommandRunner.Send(command);
        message = result.Status == CommandResultStatus.Succeeded
            ? result.Data.Message
            : CommandMessage(result.Status);
        isSubmitting = false;
        await RefreshAsync();
    }

    private async Task SignOutAsync()
    {
        generatedSecret = null;
        await SessionClient.LogoutAsync();
        Navigation.NavigateTo("/login");
    }

    private void DismissSecret() =>
        generatedSecret = null;

    private bool IsFeedCalendarSelected(string calendar) =>
        model.FeedCalendars.Contains(calendar, StringComparer.Ordinal);

    private void ToggleFeedCalendar(string calendar, ChangeEventArgs args)
    {
        var checkedValue = args.Value is bool value && value;
        model.FeedCalendars = checkedValue
            ? model.FeedCalendars.Append(calendar).Distinct(StringComparer.Ordinal).ToArray()
            : model.FeedCalendars.Where(selected => selected != calendar).ToArray();
    }

    private void SetClientName(ChangeEventArgs args) =>
        model.ClientName = args.Value?.ToString() ?? "";

    private void SetClientScopes(ChangeEventArgs args) =>
        model.ClientScopes = args.Value?.ToString() ?? "";

    private void SetFeedName(ChangeEventArgs args) =>
        model.FeedName = args.Value?.ToString() ?? "";

    private void SetCalDavName(ChangeEventArgs args) =>
        model.CalDavName = args.Value?.ToString() ?? "";

    private void SetCalDavReadableCalendars(ChangeEventArgs args) =>
        model.CalDavReadableCalendars = args.Value?.ToString() ?? "";

    private void SetCalDavWritableCalendars(ChangeEventArgs args) =>
        model.CalDavWritableCalendars = args.Value?.ToString() ?? "";

    private static IReadOnlyList<string> SplitCsv(string value) =>
        value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string CommandMessage(CommandResultStatus status) =>
        status switch
        {
            CommandResultStatus.Invalid => "Check the credential details and try again.",
            _ => "The credential action could not be completed."
        };
}
