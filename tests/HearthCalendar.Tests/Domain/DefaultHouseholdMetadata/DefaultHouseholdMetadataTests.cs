using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class DefaultHouseholdMetadataTests
{
    [Fact]
    public Task Describes_default_generic_household_members_and_relationships()
    {
        var metadata = DefaultHouseholdMetadata.Instance;

        return Verifier.Verify(new
        {
            Members = metadata.Members.Select(member => new
            {
                Id = member.Id.Value,
                member.DisplayName,
                Kind = member.Kind.ToString(),
                member.IsActive
            }),
            Relationships = metadata.Relationships.Select(relationship => new
            {
                From = relationship.From.Value,
                To = relationship.To.Value,
                Kind = relationship.Kind.ToString(),
                relationship.IsActive
            }),
            AdultAReference = metadata.FindReferencedAdult("dentist for adult a")?.Id.Value,
            ChildReference = metadata.FindReferencedPerson("child swimming")?.Id.Value,
            ChildResponsibility = metadata.IsChildResponsibilitySet(
                new HashSet<PersonId>
                {
                    new("child"),
                    new("adult-b")
                },
                new PersonId("adult-b"))
        });
    }
}
