using System.Text.RegularExpressions;
using BluQube.Authorization;
using BluQube.Commands;
using BluQube.Constants;
using BluQube.Queries;
using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Server.Features.Household;

public sealed class GetHouseholdMetadataQueryProcessor(IHearthCalendarHouseholdMetadataStore store)
    : IQueryProcessor<GetHouseholdMetadataQuery, HouseholdMetadataResult>
{
    public async ValueTask<QueryResult<HouseholdMetadataResult>> Handle(
        GetHouseholdMetadataQuery request,
        CancellationToken cancellationToken)
    {
        var inventory = await store.QueryAsync(cancellationToken);

        return QueryResult<HouseholdMetadataResult>.Succeeded(new HouseholdMetadataResult(
            inventory.Members.Select(HouseholdMetadataMapping.ToDto).ToArray(),
            inventory.Relationships.Select(HouseholdMetadataMapping.ToDto).ToArray()));
    }
}

public sealed class CreateHouseholdMemberCommandHandler(IHearthCalendarHouseholdMetadataStore store)
    : ICommandHandler<CreateHouseholdMemberCommand, CreateHouseholdMemberResult>
{
    public async ValueTask<CommandResult<CreateHouseholdMemberResult>> Handle(
        CreateHouseholdMemberCommand request,
        CancellationToken cancellationToken)
    {
        var normalized = HouseholdMetadataSupport.NormalizeMember(request.MemberId, request.DisplayName, request.Kind);
        if (normalized is null)
        {
            return HouseholdMetadataSupport.InvalidMember<CreateHouseholdMemberResult>();
        }

        if (await store.LoadMemberAsync(normalized.Value.Id, cancellationToken) is not null)
        {
            return HouseholdMetadataSupport.FailedMember<CreateHouseholdMemberResult>("DUPLICATE_HOUSEHOLD_MEMBER", "A household member with that id already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var member = new HouseholdMemberDocument
        {
            Id = normalized.Value.Id,
            DisplayName = normalized.Value.DisplayName,
            Kind = normalized.Value.Kind,
            CreatedAt = now
        };

        await store.StoreMemberAsync(
            member,
            HouseholdMetadataAudits.ForMember(AuditAction.HouseholdMemberCreated, member, now),
            cancellationToken);

        return CommandResult<CreateHouseholdMemberResult>.Succeeded(
            HouseholdMetadataMapping.ToCreateResult(member, "Household member created."));
    }
}

public sealed class UpdateHouseholdMemberCommandHandler(IHearthCalendarHouseholdMetadataStore store)
    : ICommandHandler<UpdateHouseholdMemberCommand, UpdateHouseholdMemberResult>
{
    public async ValueTask<CommandResult<UpdateHouseholdMemberResult>> Handle(
        UpdateHouseholdMemberCommand request,
        CancellationToken cancellationToken)
    {
        var normalized = HouseholdMetadataSupport.NormalizeMember(request.MemberId, request.DisplayName, request.Kind);
        if (normalized is null)
        {
            return HouseholdMetadataSupport.InvalidMember<UpdateHouseholdMemberResult>();
        }

        var existing = await store.LoadMemberAsync(normalized.Value.Id, cancellationToken);
        if (existing is null)
        {
            return HouseholdMetadataSupport.MemberNotFound<UpdateHouseholdMemberResult>();
        }

        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            DisplayName = normalized.Value.DisplayName,
            Kind = normalized.Value.Kind,
            UpdatedAt = now
        };

        await store.StoreMemberAsync(
            updated,
            HouseholdMetadataAudits.ForMember(AuditAction.HouseholdMemberUpdated, updated, now),
            cancellationToken);

        return CommandResult<UpdateHouseholdMemberResult>.Succeeded(
            HouseholdMetadataMapping.ToUpdateResult(updated, "Household member updated."));
    }
}

public sealed class ArchiveHouseholdMemberCommandHandler(IHearthCalendarHouseholdMetadataStore store)
    : ICommandHandler<ArchiveHouseholdMemberCommand, ArchiveHouseholdMemberResult>
{
    public async ValueTask<CommandResult<ArchiveHouseholdMemberResult>> Handle(
        ArchiveHouseholdMemberCommand request,
        CancellationToken cancellationToken)
    {
        var id = HouseholdMetadataSupport.NormalizeMemberId(request.MemberId);
        if (id is null)
        {
            return HouseholdMetadataSupport.InvalidMember<ArchiveHouseholdMemberResult>();
        }

        var existing = await store.LoadMemberAsync(id, cancellationToken);
        if (existing is null)
        {
            return HouseholdMetadataSupport.MemberNotFound<ArchiveHouseholdMemberResult>();
        }

        var now = DateTimeOffset.UtcNow;
        var archived = existing with { ArchivedAt = existing.ArchivedAt ?? now, UpdatedAt = now };
        await store.StoreMemberAsync(
            archived,
            HouseholdMetadataAudits.ForMember(AuditAction.HouseholdMemberArchived, archived, now),
            cancellationToken);

        return CommandResult<ArchiveHouseholdMemberResult>.Succeeded(
            HouseholdMetadataMapping.ToArchiveResult(archived, "Household member archived."));
    }
}

public sealed class CreateHouseholdRelationshipCommandHandler(IHearthCalendarHouseholdMetadataStore store)
    : ICommandHandler<CreateHouseholdRelationshipCommand, CreateHouseholdRelationshipResult>
{
    public async ValueTask<CommandResult<CreateHouseholdRelationshipResult>> Handle(
        CreateHouseholdRelationshipCommand request,
        CancellationToken cancellationToken)
    {
        var normalized = HouseholdMetadataSupport.NormalizeRelationship(request.FromMemberId, request.ToMemberId, request.Kind);
        if (normalized is null)
        {
            return HouseholdMetadataSupport.InvalidRelationship<CreateHouseholdRelationshipResult>();
        }

        if (string.Equals(normalized.Value.FromMemberId, normalized.Value.ToMemberId, StringComparison.OrdinalIgnoreCase))
        {
            return HouseholdMetadataSupport.FailedRelationship<CreateHouseholdRelationshipResult>("INVALID_HOUSEHOLD_RELATIONSHIP", "A household member cannot relate to itself.");
        }

        var inventory = await store.QueryAsync(cancellationToken);
        var from = inventory.Members.SingleOrDefault(member =>
            string.Equals(member.Id, normalized.Value.FromMemberId, StringComparison.OrdinalIgnoreCase));
        var to = inventory.Members.SingleOrDefault(member =>
            string.Equals(member.Id, normalized.Value.ToMemberId, StringComparison.OrdinalIgnoreCase));
        if (from is null || to is null || from.ArchivedAt is not null || to.ArchivedAt is not null)
        {
            return HouseholdMetadataSupport.FailedRelationship<CreateHouseholdRelationshipResult>("HOUSEHOLD_MEMBER_NOT_FOUND", "Relationships require active household members.");
        }

        var duplicate = inventory.Relationships.Any(relationship =>
            relationship.ArchivedAt is null &&
            string.Equals(relationship.FromMemberId, normalized.Value.FromMemberId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(relationship.ToMemberId, normalized.Value.ToMemberId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(relationship.Kind, normalized.Value.Kind, StringComparison.Ordinal));
        if (duplicate)
        {
            return HouseholdMetadataSupport.FailedRelationship<CreateHouseholdRelationshipResult>(
                "DUPLICATE_HOUSEHOLD_RELATIONSHIP",
                "That active household relationship already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var relationship = new HouseholdRelationshipDocument
        {
            Id = Guid.NewGuid(),
            FromMemberId = normalized.Value.FromMemberId,
            ToMemberId = normalized.Value.ToMemberId,
            Kind = normalized.Value.Kind,
            CreatedAt = now
        };

        await store.StoreRelationshipAsync(
            relationship,
            HouseholdMetadataAudits.ForRelationship(AuditAction.HouseholdRelationshipCreated, relationship, now),
            cancellationToken);

        return CommandResult<CreateHouseholdRelationshipResult>.Succeeded(
            HouseholdMetadataMapping.ToCreateResult(relationship, "Household relationship created."));
    }
}

public sealed class ArchiveHouseholdRelationshipCommandHandler(IHearthCalendarHouseholdMetadataStore store)
    : ICommandHandler<ArchiveHouseholdRelationshipCommand, ArchiveHouseholdRelationshipResult>
{
    public async ValueTask<CommandResult<ArchiveHouseholdRelationshipResult>> Handle(
        ArchiveHouseholdRelationshipCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await store.LoadRelationshipAsync(request.RelationshipId, cancellationToken);
        if (existing is null)
        {
            return HouseholdMetadataSupport.RelationshipNotFound<ArchiveHouseholdRelationshipResult>();
        }

        var now = DateTimeOffset.UtcNow;
        var archived = existing with { ArchivedAt = existing.ArchivedAt ?? now };
        await store.StoreRelationshipAsync(
            archived,
            HouseholdMetadataAudits.ForRelationship(AuditAction.HouseholdRelationshipArchived, archived, now),
            cancellationToken);

        return CommandResult<ArchiveHouseholdRelationshipResult>.Succeeded(
            HouseholdMetadataMapping.ToArchiveResult(archived, "Household relationship archived."));
    }
}

public static partial class HouseholdMetadataSupport
{
    public static string? NormalizeMemberId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var id = value.Trim().ToLowerInvariant();

        return MemberIdRegex().IsMatch(id) ? id : null;
    }

    public static NormalizedMember? NormalizeMember(string memberId, string displayName, string kind)
    {
        var id = NormalizeMemberId(memberId);
        var name = NormalizeDisplayName(displayName);
        var normalizedKind = NormalizeMemberKind(kind);

        return id is null || name is null || normalizedKind is null
            ? null
            : new NormalizedMember(id, name, normalizedKind);
    }

    public static NormalizedRelationship? NormalizeRelationship(string fromMemberId, string toMemberId, string kind)
    {
        var from = NormalizeMemberId(fromMemberId);
        var to = NormalizeMemberId(toMemberId);
        var normalizedKind = NormalizeRelationshipKind(kind);

        return from is null || to is null || normalizedKind is null
            ? null
            : new NormalizedRelationship(from, to, normalizedKind);
    }

    public static CommandResult<TResult> InvalidMember<TResult>()
        where TResult : ICommandResult =>
        FailedMember<TResult>("INVALID_HOUSEHOLD_MEMBER", "Household members need an id, display label, and supported kind.");

    public static CommandResult<TResult> MemberNotFound<TResult>()
        where TResult : ICommandResult =>
        FailedMember<TResult>("HOUSEHOLD_MEMBER_NOT_FOUND", "The household member was not found.");

    public static CommandResult<TResult> FailedMember<TResult>(string code, string message)
        where TResult : ICommandResult =>
        CommandResult<TResult>.Failed(new BluQubeErrorData(code, message));

    public static CommandResult<TResult> InvalidRelationship<TResult>()
        where TResult : ICommandResult =>
        FailedRelationship<TResult>("INVALID_HOUSEHOLD_RELATIONSHIP", "Household relationships need valid members and a supported kind.");

    public static CommandResult<TResult> RelationshipNotFound<TResult>()
        where TResult : ICommandResult =>
        FailedRelationship<TResult>("HOUSEHOLD_RELATIONSHIP_NOT_FOUND", "The household relationship was not found.");

    public static CommandResult<TResult> FailedRelationship<TResult>(string code, string message)
        where TResult : ICommandResult =>
        CommandResult<TResult>.Failed(new BluQubeErrorData(code, message));

    private static string? NormalizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var name = value.Trim();

        return name.Length is > 0 and <= 80 ? name : null;
    }

    private static string? NormalizeMemberKind(string value) =>
        Enum.TryParse<HouseholdMemberKind>(value.Trim(), ignoreCase: true, out var kind)
            ? kind.ToString()
            : null;

    private static string? NormalizeRelationshipKind(string value) =>
        Enum.TryParse<HouseholdRelationshipKind>(value.Trim(), ignoreCase: true, out var kind)
            ? kind.ToString()
            : null;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex MemberIdRegex();

    public readonly record struct NormalizedMember(string Id, string DisplayName, string Kind);

    public readonly record struct NormalizedRelationship(string FromMemberId, string ToMemberId, string Kind);
}

public static class HouseholdMetadataMapping
{
    public static HouseholdMemberDto ToDto(HouseholdMemberDocument member) =>
        new(
            member.Id,
            member.DisplayName,
            member.Kind,
            member.ArchivedAt is null,
            member.CreatedAt,
            member.UpdatedAt,
            member.ArchivedAt);

    public static HouseholdRelationshipDto ToDto(HouseholdRelationshipDocument relationship) =>
        new(
            relationship.Id,
            relationship.FromMemberId,
            relationship.ToMemberId,
            relationship.Kind,
            relationship.ArchivedAt is null,
            relationship.CreatedAt,
            relationship.ArchivedAt);

    public static CreateHouseholdMemberResult ToCreateResult(HouseholdMemberDocument member, string message) =>
        new(member.Id, member.DisplayName, member.Kind, member.ArchivedAt is null, message);

    public static UpdateHouseholdMemberResult ToUpdateResult(HouseholdMemberDocument member, string message) =>
        new(member.Id, member.DisplayName, member.Kind, member.ArchivedAt is null, message);

    public static ArchiveHouseholdMemberResult ToArchiveResult(HouseholdMemberDocument member, string message) =>
        new(member.Id, member.DisplayName, member.Kind, member.ArchivedAt is null, message);

    public static CreateHouseholdRelationshipResult ToCreateResult(
        HouseholdRelationshipDocument relationship,
        string message) =>
        new(
            relationship.Id,
            relationship.FromMemberId,
            relationship.ToMemberId,
            relationship.Kind,
            relationship.ArchivedAt is null,
            message);

    public static ArchiveHouseholdRelationshipResult ToArchiveResult(
        HouseholdRelationshipDocument relationship,
        string message) =>
        new(
            relationship.Id,
            relationship.FromMemberId,
            relationship.ToMemberId,
            relationship.Kind,
            relationship.ArchivedAt is null,
            message);
}

public static class HouseholdMetadataAudits
{
    public static AuditEntry ForMember(AuditAction action, HouseholdMemberDocument member, DateTimeOffset now) =>
        new(
            AuditEntryId.New(),
            action,
            ActorRef.System,
            now,
            $"Household metadata action {action}.",
            Metadata: new Dictionary<string, string>
            {
                ["memberId"] = member.Id,
                ["kind"] = member.Kind,
                ["active"] = (member.ArchivedAt is null).ToString()
            });

    public static AuditEntry ForRelationship(
        AuditAction action,
        HouseholdRelationshipDocument relationship,
        DateTimeOffset now) =>
        new(
            AuditEntryId.New(),
            action,
            ActorRef.System,
            now,
            $"Household metadata action {action}.",
            Metadata: new Dictionary<string, string>
            {
                ["relationshipId"] = relationship.Id.ToString(),
                ["fromMemberId"] = relationship.FromMemberId,
                ["toMemberId"] = relationship.ToMemberId,
                ["kind"] = relationship.Kind,
                ["active"] = (relationship.ArchivedAt is null).ToString()
            });
}

public sealed class GetHouseholdMetadataQueryAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<GetHouseholdMetadataQuery>(accessor);

public sealed class CreateHouseholdMemberCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<CreateHouseholdMemberCommand>(accessor);

public sealed class UpdateHouseholdMemberCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<UpdateHouseholdMemberCommand>(accessor);

public sealed class ArchiveHouseholdMemberCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<ArchiveHouseholdMemberCommand>(accessor);

public sealed class CreateHouseholdRelationshipCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<CreateHouseholdRelationshipCommand>(accessor);

public sealed class ArchiveHouseholdRelationshipCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<ArchiveHouseholdRelationshipCommand>(accessor);
