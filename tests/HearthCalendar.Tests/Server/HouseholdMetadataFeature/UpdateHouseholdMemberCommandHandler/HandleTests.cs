using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Server.Features.Household;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class UpdateHouseholdMemberCommandHandlerHandleTests : HouseholdMetadataFeatureTestBase
{
    [Fact]
    public async Task Updates_member_label_kind_and_audit()
    {
        var store = new RecordingHouseholdMetadataStore();
        store.Members.Add(new HouseholdMemberDocument
        {
            Id = "child-a",
            DisplayName = "Child A",
            Kind = "Child",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var handler = new UpdateHouseholdMemberCommandHandler(store);

        var result = await handler.Handle(
            new UpdateHouseholdMemberCommand("child-a", "Child A Updated", "Adult"),
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
