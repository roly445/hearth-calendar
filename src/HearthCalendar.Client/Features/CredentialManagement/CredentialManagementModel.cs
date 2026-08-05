namespace HearthCalendar.Client.Features.CredentialManagement;

public sealed class CredentialManagementModel
{
    public string ClientName { get; set; } = "";

    public string ClientScopes { get; set; } = "intake:write";

    public string FeedName { get; set; } = "";

    public string[] FeedCalendars { get; set; } = ["Combined"];

    public string CalDavName { get; set; } = "";

    public string CalDavReadableCalendars { get; set; } = "adult-a,combined";

    public string CalDavWritableCalendars { get; set; } = "smart-inbox";
}
