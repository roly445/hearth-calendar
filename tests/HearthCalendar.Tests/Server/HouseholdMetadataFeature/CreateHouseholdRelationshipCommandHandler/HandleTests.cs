using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Server.Features.Household;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class CreateHouseholdRelationshipCommandHandlerHandleTests : HouseholdMetadataFeatureTestBase
{
    [Fact]
    public async Task Creates_relationship_between_active_members_and_audit()
    {
        var store = StoreWithMembers();
        var handler = new CreateHouseholdRelationshipCommandHandler(store);

        var result = await handler.Handle(
            new CreateHouseholdRelationshipCommand("adult-a", "child-a", "ParentOrGuardianOf"),
            CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            Result = new
            {
                HasId = result.Data.Id != Guid.Empty,
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

    [Fact]
    public async Task Rejects_duplicate_active_relationship()
    {
        var store = StoreWithMembers();
        store.Relationships.Add(new HouseholdRelationshipDocument
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
            FromMemberId = "adult-a",
            ToMemberId = "child-a",
            Kind = "ParentOrGuardianOf",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var handler = new CreateHouseholdRelationshipCommandHandler(store);

        var result = await handler.Handle(
            new CreateHouseholdRelationshipCommand("adult-a", "child-a", "ParentOrGuardianOf"),
            CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            result.ErrorData
        });
    }

    private static RecordingHouseholdMetadataStore StoreWithMembers()
    {
        var store = new RecordingHouseholdMetadataStore();
        store.Members.Add(new HouseholdMemberDocument
        {
            Id = "adult-a",
            DisplayName = "Adult A",
            Kind = "Adult",
            CreatedAt = DateTimeOffset.UtcNow
        });
        store.Members.Add(new HouseholdMemberDocument
        {
            Id = "child-a",
            DisplayName = "Child A",
            Kind = "Child",
            CreatedAt = DateTimeOffset.UtcNow
        });

        return store;
    }
}
