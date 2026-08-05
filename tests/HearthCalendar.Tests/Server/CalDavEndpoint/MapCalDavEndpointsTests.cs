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

public sealed class MapCalDavEndpointsTests : CalDavEndpointTestBase
{
    [Theory]
    [InlineData(WriteToken)]
    [InlineData(FeedToken)]
    public async Task Bearer_tokens_cannot_use_caldav_discovery(string token)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(PropFind("/caldav/calendars/"));

        await Verifier.Verify(EndpointSnapshot.ForResponse(response))
            .UseParameters(token);
    }

    [Theory]
    [InlineData(WriteToken)]
    [InlineData(FeedToken)]
    public async Task Bearer_tokens_cannot_write_caldav_calendar_objects(string token)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        }).UseParameters(token);
    }

    [Fact]
    public async Task Discovery_requires_caldav_basic_authentication_challenge()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(PropFind("/caldav/"));

        await Verifier.Verify(EndpointSnapshot.ForResponse(response));
    }
}
