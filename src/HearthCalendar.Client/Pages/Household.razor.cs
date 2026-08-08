using BluQube.Commands;
using BluQube.Constants;
using BluQube.Queries;
using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Client.Features.HouseholdMetadata;
using HearthCalendar.Client.Services;
using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Pages;

public partial class Household
{
    private static readonly IReadOnlyList<string> MemberKindOptions = ["Adult", "Child"];

    private static readonly IReadOnlyList<string> RelationshipKindOptions =
        ["PartnerOf", "ParentOrGuardianOf", "HouseholdMemberOf", "ResponsibleFor"];

    private readonly HouseholdMetadataModel model = new();
    private IReadOnlyList<HouseholdMemberDto> members = [];
    private IReadOnlyList<HouseholdRelationshipDto> relationships = [];
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

    private IReadOnlyList<HouseholdMemberDto> ActiveMembers =>
        members.Where(member => member.IsActive).ToArray();

    private bool IsMemberActionDisabled =>
        isSubmitting ||
        string.IsNullOrWhiteSpace(model.MemberId) ||
        string.IsNullOrWhiteSpace(model.DisplayName);

    private bool IsRelationshipActionDisabled =>
        isSubmitting ||
        string.IsNullOrWhiteSpace(model.FromMemberId) ||
        string.IsNullOrWhiteSpace(model.ToMemberId) ||
        string.Equals(model.FromMemberId, model.ToMemberId, StringComparison.OrdinalIgnoreCase);

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
        var result = await QueryRunner.Send(new GetHouseholdMetadataQuery());
        if (result.Status == QueryResultStatus.Succeeded)
        {
            members = result.Data.Members;
            relationships = result.Data.Relationships;
            EnsureRelationshipSelections();
            message = null;
        }
        else if (result.Status == QueryResultStatus.Empty)
        {
            members = [];
            relationships = [];
            message = null;
        }
        else
        {
            message = result.Status == QueryResultStatus.Unauthorized
                ? "Sign in with admin access to manage household metadata."
                : "Household metadata is unavailable.";
        }

        isLoading = false;
    }

    private async Task CreateMemberAsync() =>
        await RunCommandAsync(new CreateHouseholdMemberCommand(model.MemberId, model.DisplayName, model.MemberKind));

    private async Task UpdateMemberAsync() =>
        await RunCommandAsync(new UpdateHouseholdMemberCommand(model.MemberId, model.DisplayName, model.MemberKind));

    private async Task ArchiveMemberAsync(string memberId) =>
        await RunCommandAsync(new ArchiveHouseholdMemberCommand(memberId));

    private async Task CreateRelationshipAsync() =>
        await RunCommandAsync(new CreateHouseholdRelationshipCommand(
            model.FromMemberId,
            model.ToMemberId,
            model.RelationshipKind));

    private async Task ArchiveRelationshipAsync(Guid relationshipId) =>
        await RunCommandAsync(new ArchiveHouseholdRelationshipCommand(relationshipId));

    private async Task RunCommandAsync<TResult>(ICommand<TResult> command)
        where TResult : ICommandResult
    {
        isSubmitting = true;
        var result = await CommandRunner.Send(command);
        var nextMessage = result.Status == CommandResultStatus.Succeeded
            ? SuccessMessage(result.Data)
            : CommandMessage(result.Status);
        isSubmitting = false;
        await RefreshAsync();
        message = nextMessage;
    }

    private async Task SignOutAsync()
    {
        await SessionClient.LogoutAsync();
        Navigation.NavigateTo("/login");
    }

    private void SelectMemberForEdit(HouseholdMemberDto member)
    {
        model.MemberId = member.Id;
        model.DisplayName = member.DisplayName;
        model.MemberKind = member.Kind;
    }

    private void SetMemberId(ChangeEventArgs args) =>
        model.MemberId = args.Value?.ToString() ?? "";

    private void SetDisplayName(ChangeEventArgs args) =>
        model.DisplayName = args.Value?.ToString() ?? "";

    private void SetMemberKind(ChangeEventArgs args) =>
        model.MemberKind = args.Value?.ToString() ?? MemberKindOptions[0];

    private void SetFromMemberId(ChangeEventArgs args) =>
        model.FromMemberId = args.Value?.ToString() ?? "";

    private void SetToMemberId(ChangeEventArgs args) =>
        model.ToMemberId = args.Value?.ToString() ?? "";

    private void SetRelationshipKind(ChangeEventArgs args) =>
        model.RelationshipKind = args.Value?.ToString() ?? RelationshipKindOptions[0];

    private bool IsArchiveMemberDisabled(HouseholdMemberDto member) =>
        isSubmitting || !member.IsActive;

    private bool IsArchiveRelationshipDisabled(HouseholdRelationshipDto relationship) =>
        isSubmitting || !relationship.IsActive;

    private void EnsureRelationshipSelections()
    {
        var active = ActiveMembers;
        if (active.All(member => member.Id != model.FromMemberId))
        {
            model.FromMemberId = active.FirstOrDefault()?.Id ?? "";
        }

        if (active.All(member => member.Id != model.ToMemberId))
        {
            model.ToMemberId = active.FirstOrDefault(member => member.Id != model.FromMemberId)?.Id ?? "";
        }
    }

    private string MemberLabel(string memberId) =>
        members.FirstOrDefault(member => string.Equals(member.Id, memberId, StringComparison.OrdinalIgnoreCase))
            is { } member
                ? member.DisplayName
                : memberId;

    private static string StatusLabel(bool isActive) =>
        isActive ? "Active" : "Archived";

    private static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static string UpdatedLabel(HouseholdMemberDto member) =>
        member.ArchivedAt is not null
            ? $" - archived {FormatDate(member.ArchivedAt.Value)}"
            : member.UpdatedAt is not null
                ? $" - updated {FormatDate(member.UpdatedAt.Value)}"
                : "";

    private static string ArchivedLabel(DateTimeOffset? archivedAt) =>
        archivedAt is null ? "" : $" - archived {FormatDate(archivedAt.Value)}";

    private static string SuccessMessage<TResult>(TResult result) =>
        result switch
        {
            CreateHouseholdMemberResult value => value.Message,
            UpdateHouseholdMemberResult value => value.Message,
            ArchiveHouseholdMemberResult value => value.Message,
            CreateHouseholdRelationshipResult value => value.Message,
            ArchiveHouseholdRelationshipResult value => value.Message,
            _ => "Household metadata updated."
        };

    private static string CommandMessage(CommandResultStatus status) =>
        status switch
        {
            CommandResultStatus.Invalid => "Check the household metadata details and try again.",
            _ => "The household metadata action could not be completed."
        };
}
