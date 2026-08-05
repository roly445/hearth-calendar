namespace HearthCalendar.Client.Features.CalendarWorkspace;

public sealed class CreateEventModel
{
    public string RawText { get; set; } = "";

    public string DateText { get; set; } = "";

    public string StartTimeText { get; set; } = "";

    public string EndTimeText { get; set; } = "";
}
