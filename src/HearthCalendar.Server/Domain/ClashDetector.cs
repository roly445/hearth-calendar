namespace HearthCalendar.Server.Domain;

public static class ClashDetector
{
    public static IReadOnlyList<Clash> FindClashes(CalendarEvent candidate, IReadOnlyList<CalendarEvent> existingEvents)
    {
        if (candidate.BusyStatus == BusyStatus.Free)
        {
            return [];
        }

        var affectedPeople = candidate.Participants
            .Where(participant => participant.BusyStatus == BusyStatus.Busy)
            .Select(participant => participant.Person)
            .DistinctBy(person => person.Id)
            .ToArray();

        return existingEvents
            .Where(existing => existing.ReviewStatus == ReviewStatus.Approved)
            .Where(existing => existing.BusyStatus == BusyStatus.Busy)
            .Where(existing => existing.Id != candidate.Id)
            .Where(existing => existing.ParentEventId != candidate.Id)
            .Where(existing => candidate.ParentEventId != existing.Id)
            .Where(existing => existing.Time.Overlaps(candidate.Time))
            .Select(existing => BuildClash(existing, affectedPeople))
            .Where(clash => clash.AffectedPeople.Count > 0)
            .ToArray();
    }

    private static Clash BuildClash(CalendarEvent existing, IReadOnlyList<Person> affectedPeople)
    {
        var existingPeople = existing.Participants
            .Where(participant => participant.BusyStatus == BusyStatus.Busy)
            .Select(participant => participant.Person.Id)
            .ToHashSet();
        var overlappingPeople = affectedPeople
            .Where(person => existingPeople.Contains(person.Id))
            .ToArray();

        return new Clash(
            existing.Id,
            overlappingPeople,
            ClashSeverity.Warning,
            $"Conflicts with {existing.Title}.");
    }
}
