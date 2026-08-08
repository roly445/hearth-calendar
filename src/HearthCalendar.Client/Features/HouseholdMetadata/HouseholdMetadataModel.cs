namespace HearthCalendar.Client.Features.HouseholdMetadata;

public sealed class HouseholdMetadataModel
{
    public string MemberId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string MemberKind { get; set; } = "Adult";

    public string FromMemberId { get; set; } = "";

    public string ToMemberId { get; set; } = "";

    public string RelationshipKind { get; set; } = "ParentOrGuardianOf";
}
