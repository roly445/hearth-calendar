using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
[Trait("Category", "Docker")]
public sealed class HouseholdMetadataDocumentPersistenceTests(MartenPostgreSqlFixture fixture)
    : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task Household_metadata_documents_store_generic_members_and_relationships()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        session.Store(new HouseholdMemberDocument
        {
            Id = "adult-a",
            DisplayName = "Adult A",
            Kind = "Adult",
            CreatedAt = SubmittedAt()
        });
        session.Store(new HouseholdMemberDocument
        {
            Id = "child-a",
            DisplayName = "Child A",
            Kind = "Child",
            CreatedAt = SubmittedAt().AddMinutes(1),
            ArchivedAt = SubmittedAt().AddMinutes(2)
        });
        session.Store(new HouseholdRelationshipDocument
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
            FromMemberId = "adult-a",
            ToMemberId = "child-a",
            Kind = "ParentOrGuardianOf",
            CreatedAt = SubmittedAt().AddMinutes(3)
        });
        await session.SaveChangesAsync(CancellationToken.None);

        var members = await session.Query<HouseholdMemberDocument>()
            .OrderBy(member => member.Id)
            .ToListAsync(CancellationToken.None);
        var relationships = await session.Query<HouseholdRelationshipDocument>()
            .OrderBy(relationship => relationship.FromMemberId)
            .ToListAsync(CancellationToken.None);

        await Verifier.Verify(new
        {
            Members = members.Select(member => new
            {
                member.Id,
                member.DisplayName,
                member.Kind,
                IsActive = member.ArchivedAt is null,
                CreatedAt = member.CreatedAt.ToString("O"),
                ArchivedAt = member.ArchivedAt?.ToString("O")
            }),
            Relationships = relationships.Select(relationship => new
            {
                relationship.FromMemberId,
                relationship.ToMemberId,
                relationship.Kind,
                IsActive = relationship.ArchivedAt is null,
                CreatedAt = relationship.CreatedAt.ToString("O"),
                ArchivedAt = relationship.ArchivedAt?.ToString("O")
            })
        });
    }
}
