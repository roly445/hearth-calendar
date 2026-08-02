using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.CalDav;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;

namespace HearthCalendar.Tests.Server;

public sealed class OptionsAsyncTests : CalDavEndpointTestBase
{
    [Fact]
    public async Task Options_advertises_caldav_discovery_methods()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var root = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/caldav/"));
        var smartInbox = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Options,
            "/caldav/calendars/smart-inbox/"));
        var smartInboxArchive = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Options,
            "/caldav/calendars/smart-inbox-archive/"));

        Assert.Equal(HttpStatusCode.NoContent, root.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND", root.Content.Headers.Allow.ToString());
        Assert.Equal("1, 3, calendar-access", root.Headers.GetValues("DAV").Single());
        Assert.Equal(HttpStatusCode.NoContent, smartInbox.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND, PUT", smartInbox.Content.Headers.Allow.ToString());
        Assert.Equal("1, 3, calendar-access", smartInbox.Headers.GetValues("DAV").Single());
        Assert.Equal(HttpStatusCode.NoContent, smartInboxArchive.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND", smartInboxArchive.Content.Headers.Allow.ToString());
    }
}
