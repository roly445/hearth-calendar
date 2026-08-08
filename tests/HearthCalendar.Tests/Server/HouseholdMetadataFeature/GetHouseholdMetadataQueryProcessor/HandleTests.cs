using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Server.Features.Household;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class GetHouseholdMetadataQueryProcessorHandleTests : HouseholdMetadataFeatureTestBase
{
    [Fact]
    public async Task Lists_household_metadata_without_personal_details()
    {
        var store = new RecordingHouseholdMetadataStore();
        store.Members.Add(new HouseholdMemberDocument
        {
            Id = "adult-a",
            DisplayName = "Adult A",
            Kind = "Adult",
            CreatedAt = new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero)
        });
        store.Members.Add(new HouseholdMemberDocument
        {
            Id = "child-a",
            DisplayName = "Child A",
            Kind = "Child",
            CreatedAt = new DateTimeOffset(2026, 8, 8, 9, 1, 0, TimeSpan.Zero),
            ArchivedAt = new DateTimeOffset(2026, 8, 8, 9, 2, 0, TimeSpan.Zero)
        });
        store.Relationships.Add(new HouseholdRelationshipDocument
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
            FromMemberId = "adult-a",
            ToMemberId = "child-a",
            Kind = "ParentOrGuardianOf",
            CreatedAt = new DateTimeOffset(2026, 8, 8, 9, 3, 0, TimeSpan.Zero)
        });
        var processor = new GetHouseholdMetadataQueryProcessor(store);

        var result = await processor.Handle(new GetHouseholdMetadataQuery(), CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            result.Data
        });
    }
}
