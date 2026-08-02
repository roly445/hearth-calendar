using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace HearthCalendar.Tests.Server;

public sealed class HearthCalendarAuthTests : AuthIntakeEndpointTestBase
{
    [Fact]
    public void Admin_policy_requires_explicit_admin_scope()
    {
        var store = new RecordingHearthCalendarStore();
        using var factory = CreateFactory(store);

        var authorizationOptions = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>()
            .Value;
        var adminPolicy = authorizationOptions.GetPolicy(HearthCalendarAuth.AdminPolicy);
        var claimRequirement = Assert.IsType<ClaimsAuthorizationRequirement>(
            Assert.Single(adminPolicy!.Requirements.OfType<ClaimsAuthorizationRequirement>()));

        Assert.Equal(HearthCalendarAuth.ScopeClaim, claimRequirement.ClaimType);
        Assert.Equal([HearthCalendarAuth.AdminWebScope], claimRequirement.AllowedValues);
    }

    [Fact]
    public void Feed_token_principal_carries_allowed_calendar_claims()
    {
        var principal = HearthCalendarTokenPrincipalFactory.Create(
            "adult-a-feed",
            HearthCalendarAuth.FeedTokenKind,
            [HearthCalendarAuth.FeedReadScope],
            [VirtualCalendar.AdultA.ToString()]);

        Assert.Equal(HearthCalendarAuth.FeedTokenKind, principal.FindFirst(HearthCalendarAuth.TokenKindClaim)?.Value);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == HearthCalendarAuth.ScopeClaim &&
            claim.Value == HearthCalendarAuth.FeedReadScope);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == HearthCalendarAuth.AllowedCalendarClaim &&
            claim.Value == VirtualCalendar.AdultA.ToString());
    }
}
