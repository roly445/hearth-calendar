using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Features.CredentialManagement;

public partial class CredentialTable<TItem>
{
    [Parameter]
    public string Title { get; set; } = "";

    [Parameter]
    public IReadOnlyList<TItem> Items { get; set; } = [];

    [Parameter]
    public Func<TItem, Guid> IdSelector { get; set; } = _ => Guid.Empty;

    [Parameter]
    public Func<TItem, string> NameSelector { get; set; } = _ => "";

    [Parameter]
    public Func<TItem, IReadOnlyList<string>> ScopeSelector { get; set; } = _ => [];

    [Parameter]
    public Func<TItem, IReadOnlyList<string>>? CalendarSelector { get; set; }

    [Parameter]
    public Func<TItem, DateTimeOffset> CreatedAtSelector { get; set; } = _ => default;

    [Parameter]
    public Func<TItem, DateTimeOffset?> LastUsedAtSelector { get; set; } = _ => null;

    [Parameter]
    public Func<TItem, DateTimeOffset?> RevokedAtSelector { get; set; } = _ => null;

    [Parameter]
    public EventCallback<Guid> RotateRequested { get; set; }

    [Parameter]
    public EventCallback<Guid> RevokeRequested { get; set; }
}
