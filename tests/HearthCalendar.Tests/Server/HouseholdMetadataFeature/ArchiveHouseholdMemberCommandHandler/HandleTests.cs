using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Server.Features.Household;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class ArchiveHouseholdMemberCommandHandlerHandleTests : HouseholdMetadataFeatureTestBase
{
    [Fact]
    public async Task Archives_member_and_audits()
    {
        var store = new RecordingHouseholdMetadataStore();
        store.Members.Add(new HouseholdMemberDocument
        {
            Id = "child-a",
            DisplayName = "Child A",
            Kind = "Child",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var handler = new ArchiveHouseholdMemberCommandHandler(store);

        var result = await handler.Handle(
            new ArchiveHouseholdMemberCommand("child-a"),
            CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            result.Data,
            Member = DescribeMember(store.Members.Single()),
            Audit = DescribeAudit(store.Audits.Single())
        });
    }
}
