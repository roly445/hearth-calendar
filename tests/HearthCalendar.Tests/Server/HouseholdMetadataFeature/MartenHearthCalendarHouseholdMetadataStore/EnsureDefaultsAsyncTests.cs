using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class MartenHearthCalendarHouseholdMetadataStoreEnsureDefaultsAsyncTests : HouseholdMetadataFeatureTestBase
{
    [Fact]
    public async Task Creates_generic_default_metadata_once()
    {
        var store = new RecordingHouseholdMetadataStore();
        var now = new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);

        var first = await store.EnsureDefaultsAsync(now, CancellationToken.None);
        var second = await store.EnsureDefaultsAsync(now.AddMinutes(1), CancellationToken.None);

        await Verifier.Verify(new
        {
            First = first,
            Second = second,
            Members = store.Members.Select(DescribeMember),
            Relationships = store.Relationships.Select(DescribeRelationship)
        });
    }
}
