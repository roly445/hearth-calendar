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

public sealed class PropFindAsyncTests : CalDavEndpointTestBase
{
    [Fact]
    public async Task Propfind_calendar_privileges_are_scoped_to_caldav_credential()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/", depth: "1"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var discovery = NormalizeDiscoveryXml(document);

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/adult-a/" &&
            item.Privileges.SequenceEqual(["read"]));
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/combined/" &&
            item.Privileges.SequenceEqual(["read"]));
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/smart-inbox/" &&
            item.Privileges.Count == 0);
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/adult-b/" &&
            item.Privileges.Count == 0);
    }

    [Fact]
    public async Task Propfind_calendars_returns_smart_inbox_and_virtual_calendar_metadata()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/", depth: "1"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        await Verifier.Verify(NormalizeDiscoveryXml(document));
    }

    [Fact]
    public async Task Propfind_root_returns_service_discovery_multistatus()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1, 3, calendar-access", response.Headers.GetValues("DAV").Single());
        await Verifier.Verify(NormalizeDiscoveryXml(document));
    }

    [Fact]
    public async Task Propfind_smart_inbox_marks_calendar_writable()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/smart-inbox/", depth: "0"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var discovery = NormalizeDiscoveryXml(document);

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/smart-inbox/" &&
            item.Privileges.SequenceEqual(["write"]));
    }

    [Fact]
    public async Task Read_only_caldav_credential_can_discover_but_not_write()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var discovery = await client.SendAsync(PropFind("/caldav/calendars/"));
        var write = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal((HttpStatusCode)207, discovery.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }
}
