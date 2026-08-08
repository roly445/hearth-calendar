namespace HearthCalendar.Server.Domain;

public readonly record struct HouseholdMemberId(string Value);

public sealed record HouseholdMember(
    HouseholdMemberId Id,
    string DisplayName,
    HouseholdMemberKind Kind,
    bool IsActive = true)
{
    public Person ToPerson() => new(new PersonId(Id.Value), DisplayName, Kind.ToPersonKind());
}

public sealed record HouseholdRelationship(
    HouseholdMemberId From,
    HouseholdMemberId To,
    HouseholdRelationshipKind Kind,
    bool IsActive = true);

public enum HouseholdMemberKind
{
    Adult,
    Child
}

public enum HouseholdRelationshipKind
{
    PartnerOf,
    ParentOrGuardianOf,
    HouseholdMemberOf,
    ResponsibleFor
}

public interface IHouseholdMetadata
{
    IReadOnlyList<HouseholdMember> Members { get; }

    IReadOnlyList<HouseholdRelationship> Relationships { get; }

    IReadOnlyList<Person> People { get; }

    Person? FindPerson(PersonId personId);

    Person? FindReferencedPerson(string normalizedText);

    Person? FindReferencedAdult(string normalizedText);

    Person? FindDefaultChild();

    Person? FindDefaultPersonForCalendar(VirtualCalendar calendar);

    VirtualCalendar? FindPrimaryCalendarFor(Person person);

    bool IsFamilyParticipantSet(IReadOnlySet<PersonId> participantIds);

    bool IsChildResponsibilitySet(IReadOnlySet<PersonId> participantIds, PersonId? responsibleAdultId);

    bool IsChild(Person person);
}

public sealed class DefaultHouseholdMetadata : IHouseholdMetadata
{
    public static DefaultHouseholdMetadata Instance { get; } = new();

    public IReadOnlyList<HouseholdMember> Members { get; } =
    [
        new(new HouseholdMemberId("adult-a"), "Adult A", HouseholdMemberKind.Adult),
        new(new HouseholdMemberId("adult-b"), "Adult B", HouseholdMemberKind.Adult),
        new(new HouseholdMemberId("child"), "Child", HouseholdMemberKind.Child)
    ];

    public IReadOnlyList<HouseholdRelationship> Relationships { get; } =
    [
        new(new HouseholdMemberId("adult-a"), new HouseholdMemberId("adult-b"), HouseholdRelationshipKind.PartnerOf),
        new(new HouseholdMemberId("adult-b"), new HouseholdMemberId("adult-a"), HouseholdRelationshipKind.PartnerOf),
        new(new HouseholdMemberId("adult-a"), new HouseholdMemberId("child"), HouseholdRelationshipKind.ParentOrGuardianOf),
        new(new HouseholdMemberId("adult-b"), new HouseholdMemberId("child"), HouseholdRelationshipKind.ParentOrGuardianOf),
        new(new HouseholdMemberId("adult-a"), new HouseholdMemberId("child"), HouseholdRelationshipKind.ResponsibleFor),
        new(new HouseholdMemberId("adult-b"), new HouseholdMemberId("child"), HouseholdRelationshipKind.ResponsibleFor)
    ];

    public IReadOnlyList<Person> People { get; }

    private DefaultHouseholdMetadata()
    {
        People = Members.Where(member => member.IsActive).Select(member => member.ToPerson()).ToArray();
    }

    public Person? FindPerson(PersonId personId) =>
        People.FirstOrDefault(person => person.Id == personId);

    public Person? FindReferencedPerson(string normalizedText) =>
        FindReferencedAdult(normalizedText) ??
        (normalizedText.Contains("child", StringComparison.Ordinal) ? FindDefaultChild() : null);

    public Person? FindReferencedAdult(string normalizedText) =>
        People
            .Where(person => person.Kind == PersonKind.Adult)
            .FirstOrDefault(person => IsReferenced(normalizedText, person));

    public Person? FindDefaultChild() =>
        People.FirstOrDefault(person => person.Kind == PersonKind.Child);

    public Person? FindDefaultPersonForCalendar(VirtualCalendar calendar) =>
        calendar switch
        {
            VirtualCalendar.AdultA => FindPerson(new PersonId("adult-a")),
            VirtualCalendar.AdultB => FindPerson(new PersonId("adult-b")),
            VirtualCalendar.Child => FindDefaultChild(),
            _ => null
        };

    public VirtualCalendar? FindPrimaryCalendarFor(Person person)
    {
        if (person.Id.Value.Equals("adult-a", StringComparison.OrdinalIgnoreCase))
        {
            return VirtualCalendar.AdultA;
        }

        if (person.Id.Value.Equals("adult-b", StringComparison.OrdinalIgnoreCase))
        {
            return VirtualCalendar.AdultB;
        }

        return IsChild(person) ? VirtualCalendar.Child : null;
    }

    public bool IsFamilyParticipantSet(IReadOnlySet<PersonId> participantIds) =>
        People.All(person => participantIds.Contains(person.Id));

    public bool IsChildResponsibilitySet(IReadOnlySet<PersonId> participantIds, PersonId? responsibleAdultId)
    {
        if (responsibleAdultId is null)
        {
            return false;
        }

        var child = FindDefaultChild();
        if (child is null || !participantIds.Contains(child.Id) || !participantIds.Contains(responsibleAdultId.Value))
        {
            return false;
        }

        return Relationships.Any(relationship =>
            relationship.IsActive &&
            relationship.From.Value == responsibleAdultId.Value.Value &&
            relationship.To.Value == child.Id.Value &&
            relationship.Kind is HouseholdRelationshipKind.ParentOrGuardianOf or HouseholdRelationshipKind.ResponsibleFor);
    }

    public bool IsChild(Person person) =>
        person.Kind == PersonKind.Child;

    private static bool IsReferenced(string normalizedText, Person person)
    {
        var displayName = person.DisplayName.ToLowerInvariant();
        var idAsWords = person.Id.Value.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant();

        return normalizedText.Contains(displayName, StringComparison.Ordinal) ||
            normalizedText.Contains(idAsWords, StringComparison.Ordinal);
    }
}

public static class HouseholdMemberKindExtensions
{
    public static PersonKind ToPersonKind(this HouseholdMemberKind kind) =>
        kind switch
        {
            HouseholdMemberKind.Adult => PersonKind.Adult,
            HouseholdMemberKind.Child => PersonKind.Child,
            _ => PersonKind.Adult
        };
}
