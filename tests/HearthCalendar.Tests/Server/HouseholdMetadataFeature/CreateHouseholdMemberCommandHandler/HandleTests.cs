using HearthCalendar.Client.Contracts.Household;
using HearthCalendar.Server.Features.Household;

namespace HearthCalendar.Tests.Server;

public sealed class CreateHouseholdMemberCommandHandlerHandleTests : HouseholdMetadataFeatureTestBase
{
    [Fact]
    public async Task Creates_normalized_household_member_and_audit()
    {
        var store = new RecordingHouseholdMetadataStore();
        var handler = new CreateHouseholdMemberCommandHandler(store);

        var result = await handler.Handle(
            new CreateHouseholdMemberCommand(" Child-A ", " Child A ", "child"),
            CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            result.Data,
            Member = DescribeMember(store.Members.Single()),
            Audit = DescribeAudit(store.Audits.Single())
        });
    }

    [Fact]
    public async Task Rejects_duplicate_member_id()
    {
        var store = new RecordingHouseholdMetadataStore();
        store.Members.Add(new()
        {
            Id = "child-a",
            DisplayName = "Child A",
            Kind = "Child",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var handler = new CreateHouseholdMemberCommandHandler(store);

        var result = await handler.Handle(
            new CreateHouseholdMemberCommand("child-a", "Child A", "Child"),
            CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            result.ErrorData
        });
    }
}
