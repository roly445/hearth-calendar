using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Server.Features.Household;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class ArchiveHouseholdRelationshipCommandHandlerHandleTests : HouseholdMetadataFeatureTestBase
{
    [Fact]
    public async Task Archives_relationship_and_audits()
    {
        var relationshipId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var store = new RecordingHouseholdMetadataStore();
        store.Relationships.Add(new HouseholdRelationshipDocument
        {
            Id = relationshipId,
            FromMemberId = "adult-a",
            ToMemberId = "child-a",
            Kind = "ParentOrGuardianOf",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var handler = new ArchiveHouseholdRelationshipCommandHandler(store);

        var result = await handler.Handle(
            new ArchiveHouseholdRelationshipCommand(relationshipId),
            CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            Result = new
            {
                result.Data.Id,
                result.Data.FromMemberId,
                result.Data.ToMemberId,
                result.Data.Kind,
                result.Data.IsActive,
                result.Data.Message
            },
            Relationship = DescribeRelationship(store.Relationships.Single()),
            Audit = DescribeAudit(store.Audits.Single())
        });
    }
}
