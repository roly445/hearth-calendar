namespace HearthCalendar.Server.Domain;

public static class KnownPeople
{
    public static Person AdultA { get; } =
        DefaultHouseholdMetadata.Instance.FindPerson(new PersonId("adult-a")) ??
        new Person(new PersonId("adult-a"), "Adult A", PersonKind.Adult);

    public static Person AdultB { get; } =
        DefaultHouseholdMetadata.Instance.FindPerson(new PersonId("adult-b")) ??
        new Person(new PersonId("adult-b"), "Adult B", PersonKind.Adult);

    public static Person Child { get; } =
        DefaultHouseholdMetadata.Instance.FindPerson(new PersonId("child")) ??
        new Person(new PersonId("child"), "Child", PersonKind.Child);

    public static IReadOnlyList<Person> All { get; } = DefaultHouseholdMetadata.Instance.People;
}
