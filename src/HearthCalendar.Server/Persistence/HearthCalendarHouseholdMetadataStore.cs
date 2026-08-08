using HearthCalendar.Server.Domain;
using Marten;

namespace HearthCalendar.Server.Persistence;

public interface IHearthCalendarHouseholdMetadataStore
{
    Task<HouseholdMetadataInventory> QueryAsync(CancellationToken cancellationToken);

    Task<HouseholdMemberDocument?> LoadMemberAsync(string id, CancellationToken cancellationToken);

    Task StoreMemberAsync(HouseholdMemberDocument member, AuditEntry audit, CancellationToken cancellationToken);

    Task<HouseholdRelationshipDocument?> LoadRelationshipAsync(Guid id, CancellationToken cancellationToken);

    Task StoreRelationshipAsync(HouseholdRelationshipDocument relationship, AuditEntry audit, CancellationToken cancellationToken);

    Task<EnsureDefaultHouseholdMetadataResultDocument> EnsureDefaultsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record HouseholdMetadataInventory(
    IReadOnlyList<HouseholdMemberDocument> Members,
    IReadOnlyList<HouseholdRelationshipDocument> Relationships);

public sealed record EnsureDefaultHouseholdMetadataResultDocument(
    int MembersCreated,
    int RelationshipsCreated);

public sealed class MartenHearthCalendarHouseholdMetadataStore(IDocumentSession session)
    : IHearthCalendarHouseholdMetadataStore
{
    public async Task<HouseholdMetadataInventory> QueryAsync(CancellationToken cancellationToken)
    {
        var members = await session.Query<HouseholdMemberDocument>()
            .OrderBy(member => member.Id)
            .ToListAsync(cancellationToken);
        var relationships = await session.Query<HouseholdRelationshipDocument>()
            .OrderBy(relationship => relationship.FromMemberId)
            .ThenBy(relationship => relationship.ToMemberId)
            .ThenBy(relationship => relationship.Kind)
            .ToListAsync(cancellationToken);

        return new HouseholdMetadataInventory(members, relationships);
    }

    public Task<HouseholdMemberDocument?> LoadMemberAsync(string id, CancellationToken cancellationToken) =>
        session.LoadAsync<HouseholdMemberDocument>(id, cancellationToken);

    public Task StoreMemberAsync(
        HouseholdMemberDocument member,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        StoreWithAuditAsync(member, audit, cancellationToken);

    public Task<HouseholdRelationshipDocument?> LoadRelationshipAsync(Guid id, CancellationToken cancellationToken) =>
        session.LoadAsync<HouseholdRelationshipDocument>(id, cancellationToken);

    public Task StoreRelationshipAsync(
        HouseholdRelationshipDocument relationship,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        StoreWithAuditAsync(relationship, audit, cancellationToken);

    public async Task<EnsureDefaultHouseholdMetadataResultDocument> EnsureDefaultsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var createdMembers = 0;
        foreach (var member in DefaultHouseholdMetadata.Instance.Members)
        {
            if (await LoadMemberAsync(member.Id.Value, cancellationToken) is not null)
            {
                continue;
            }

            session.Store(new HouseholdMemberDocument
            {
                Id = member.Id.Value,
                DisplayName = member.DisplayName,
                Kind = member.Kind.ToString(),
                CreatedAt = now
            });
            createdMembers++;
        }

        var existingRelationships = await session.Query<HouseholdRelationshipDocument>()
            .Where(relationship => relationship.ArchivedAt == null)
            .ToListAsync(cancellationToken);
        var createdRelationships = 0;
        foreach (var relationship in DefaultHouseholdMetadata.Instance.Relationships)
        {
            var exists = existingRelationships.Any(candidate =>
                string.Equals(candidate.FromMemberId, relationship.From.Value, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.ToMemberId, relationship.To.Value, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Kind, relationship.Kind.ToString(), StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            session.Store(new HouseholdRelationshipDocument
            {
                Id = Guid.NewGuid(),
                FromMemberId = relationship.From.Value,
                ToMemberId = relationship.To.Value,
                Kind = relationship.Kind.ToString(),
                CreatedAt = now
            });
            createdRelationships++;
        }

        if (createdMembers > 0 || createdRelationships > 0)
        {
            session.Store(new AuditEntry(
                AuditEntryId.New(),
                AuditAction.HouseholdDefaultsBootstrapped,
                ActorRef.System,
                now,
                "Household metadata defaults bootstrapped.",
                Metadata: new Dictionary<string, string>
                {
                    ["membersCreated"] = createdMembers.ToString(),
                    ["relationshipsCreated"] = createdRelationships.ToString()
                }).ToDocument());
        }

        await session.SaveChangesAsync(cancellationToken);

        return new EnsureDefaultHouseholdMetadataResultDocument(createdMembers, createdRelationships);
    }

    private async Task StoreWithAuditAsync<TDocument>(
        TDocument document,
        AuditEntry audit,
        CancellationToken cancellationToken)
        where TDocument : notnull
    {
        session.Store(document);
        session.Store(audit.ToDocument());

        await session.SaveChangesAsync(cancellationToken);
    }
}
