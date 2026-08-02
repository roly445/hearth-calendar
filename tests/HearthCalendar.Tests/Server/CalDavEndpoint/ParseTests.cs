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

public sealed class ParseTests : CalDavEndpointTestBase
{
    [Fact]
    public void Parser_accepts_all_day_vevent_without_faking_midnight_times()
    {
        var parsed = CalDavEventParser.Parse("""
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            SUMMARY:Adult B birthday
            DTSTART;VALUE=DATE:20260725
            END:VEVENT
            END:VCALENDAR
            """);

        Assert.NotNull(parsed);
        Assert.Equal("Adult B birthday", parsed.Summary);
        Assert.Equal(new DateOnly(2026, 7, 25), parsed.Date);
        Assert.Null(parsed.StartTime);
        Assert.Null(parsed.EndTime);
    }
}
