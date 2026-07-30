using System.ComponentModel.DataAnnotations;

namespace HearthCalendar.Server.Persistence;

public sealed class HearthCalendarDatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string SchemaName { get; init; } = "hearth_calendar";
}
