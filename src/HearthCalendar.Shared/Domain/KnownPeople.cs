namespace HearthCalendar.Shared.Domain;

public static class KnownPeople
{
    public static Person AdultA { get; } = new(new PersonId("adult-a"), "Adult A", PersonKind.Adult);

    public static Person AdultB { get; } = new(new PersonId("adult-b"), "Adult B", PersonKind.Adult);

    public static Person Child { get; } = new(new PersonId("child"), "Child", PersonKind.Child);

    public static IReadOnlyList<Person> All { get; } = [AdultA, AdultB, Child];
}
