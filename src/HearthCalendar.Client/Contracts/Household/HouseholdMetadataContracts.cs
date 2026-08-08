using BluQube.Attributes;
using BluQube.Commands;
using BluQube.Queries;

namespace HearthCalendar.Client.Contracts.Household;

[BluQubeQuery(Path = "queries/admin/household")]
public sealed record GetHouseholdMetadataQuery : IQuery<HouseholdMetadataResult>;

public sealed record HouseholdMetadataResult(
    IReadOnlyList<HouseholdMemberDto> Members,
    IReadOnlyList<HouseholdRelationshipDto> Relationships) : IQueryResult;

[BluQubeCommand(Path = "commands/admin/household/members/create")]
public sealed record CreateHouseholdMemberCommand(
    string MemberId,
    string DisplayName,
    string Kind) : ICommand<CreateHouseholdMemberResult>;

[BluQubeCommand(Path = "commands/admin/household/members/update")]
public sealed record UpdateHouseholdMemberCommand(
    string MemberId,
    string DisplayName,
    string Kind) : ICommand<UpdateHouseholdMemberResult>;

[BluQubeCommand(Path = "commands/admin/household/members/archive")]
public sealed record ArchiveHouseholdMemberCommand(string MemberId) : ICommand<ArchiveHouseholdMemberResult>;

[BluQubeCommand(Path = "commands/admin/household/relationships/create")]
public sealed record CreateHouseholdRelationshipCommand(
    string FromMemberId,
    string ToMemberId,
    string Kind) : ICommand<CreateHouseholdRelationshipResult>;

[BluQubeCommand(Path = "commands/admin/household/relationships/archive")]
public sealed record ArchiveHouseholdRelationshipCommand(Guid RelationshipId) : ICommand<ArchiveHouseholdRelationshipResult>;

public sealed record HouseholdMemberDto(
    string Id,
    string DisplayName,
    string Kind,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record HouseholdRelationshipDto(
    Guid Id,
    string FromMemberId,
    string ToMemberId,
    string Kind,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record CreateHouseholdMemberResult(
    string Id,
    string DisplayName,
    string Kind,
    bool IsActive,
    string Message) : ICommandResult;

public sealed record UpdateHouseholdMemberResult(
    string Id,
    string DisplayName,
    string Kind,
    bool IsActive,
    string Message) : ICommandResult;

public sealed record ArchiveHouseholdMemberResult(
    string Id,
    string DisplayName,
    string Kind,
    bool IsActive,
    string Message) : ICommandResult;

public sealed record CreateHouseholdRelationshipResult(
    Guid Id,
    string FromMemberId,
    string ToMemberId,
    string Kind,
    bool IsActive,
    string Message) : ICommandResult;

public sealed record ArchiveHouseholdRelationshipResult(
    Guid Id,
    string FromMemberId,
    string ToMemberId,
    string Kind,
    bool IsActive,
    string Message) : ICommandResult;
