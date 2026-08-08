using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public abstract class HouseholdMetadataFeatureTestBase
{
    protected sealed class RecordingHouseholdMetadataStore : IHearthCalendarHouseholdMetadataStore
    {
        public List<HouseholdMemberDocument> Members { get; } = [];

        public List<HouseholdRelationshipDocument> Relationships { get; } = [];

        public List<AuditEntry> Audits { get; } = [];

        public Task<HouseholdMetadataInventory> QueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HouseholdMetadataInventory(Members, Relationships));

        public Task<HouseholdMemberDocument?> LoadMemberAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Members.SingleOrDefault(member => string.Equals(member.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task StoreMemberAsync(
            HouseholdMemberDocument member,
            AuditEntry audit,
            CancellationToken cancellationToken)
        {
            Members.RemoveAll(candidate => string.Equals(candidate.Id, member.Id, StringComparison.OrdinalIgnoreCase));
            Members.Add(member);
            Audits.Add(audit);

            return Task.CompletedTask;
        }

        public Task<HouseholdRelationshipDocument?> LoadRelationshipAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Relationships.SingleOrDefault(relationship => relationship.Id == id));

        public Task StoreRelationshipAsync(
            HouseholdRelationshipDocument relationship,
            AuditEntry audit,
            CancellationToken cancellationToken)
        {
            Relationships.RemoveAll(candidate => candidate.Id == relationship.Id);
            Relationships.Add(relationship);
            Audits.Add(audit);

            return Task.CompletedTask;
        }

        public Task<EnsureDefaultHouseholdMetadataResultDocument> EnsureDefaultsAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var createdMembers = 0;
            foreach (var member in DefaultHouseholdMetadata.Instance.Members)
            {
                if (Members.Any(candidate => string.Equals(candidate.Id, member.Id.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Members.Add(new HouseholdMemberDocument
                {
                    Id = member.Id.Value,
                    DisplayName = member.DisplayName,
                    Kind = member.Kind.ToString(),
                    CreatedAt = now
                });
                createdMembers++;
            }

            var createdRelationships = 0;
            foreach (var relationship in DefaultHouseholdMetadata.Instance.Relationships)
            {
                if (Relationships.Any(candidate =>
                    candidate.ArchivedAt is null &&
                    string.Equals(candidate.FromMemberId, relationship.From.Value, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.ToMemberId, relationship.To.Value, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Kind, relationship.Kind.ToString(), StringComparison.Ordinal)))
                {
                    continue;
                }

                Relationships.Add(new HouseholdRelationshipDocument
                {
                    Id = Guid.NewGuid(),
                    FromMemberId = relationship.From.Value,
                    ToMemberId = relationship.To.Value,
                    Kind = relationship.Kind.ToString(),
                    CreatedAt = now
                });
                createdRelationships++;
            }

            return Task.FromResult(new EnsureDefaultHouseholdMetadataResultDocument(createdMembers, createdRelationships));
        }
    }

    protected static object DescribeMember(HouseholdMemberDocument member) => new
    {
        member.Id,
        member.DisplayName,
        member.Kind,
        IsActive = member.ArchivedAt is null,
        HasCreatedAt = member.CreatedAt != default,
        HasUpdatedAt = member.UpdatedAt is not null,
        HasArchivedAt = member.ArchivedAt is not null
    };

    protected static object DescribeRelationship(HouseholdRelationshipDocument relationship) => new
    {
        HasId = relationship.Id != Guid.Empty,
        relationship.FromMemberId,
        relationship.ToMemberId,
        relationship.Kind,
        IsActive = relationship.ArchivedAt is null,
        HasCreatedAt = relationship.CreatedAt != default,
        HasArchivedAt = relationship.ArchivedAt is not null
    };

    protected static object DescribeAudit(AuditEntry audit) => new
    {
        Action = audit.Action.ToString(),
        Actor = audit.Actor.Id,
        HasOccurredAt = audit.OccurredAt != default,
        audit.Summary,
        audit.Metadata
    };
}
