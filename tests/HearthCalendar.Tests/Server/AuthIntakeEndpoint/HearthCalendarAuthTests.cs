using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ScottBrady91.AspNetCore.Identity;
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
            adminPolicy!.Requirements.OfType<ClaimsAuthorizationRequirement>().Single());
        var roleRequirement = Assert.IsType<RolesAuthorizationRequirement>(
            adminPolicy.Requirements.OfType<RolesAuthorizationRequirement>().Single());

        Assert.Equal(HearthCalendarAuth.ScopeClaim, claimRequirement.ClaimType);
        Assert.Equal([HearthCalendarAuth.AdminWebScope], claimRequirement.AllowedValues);
        Assert.Equal([HearthCalendarAuth.AdminRole], roleRequirement.AllowedRoles);
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

    [Fact]
    public void BCrypt_password_hasher_matches_only_original_password()
    {
        var hasher = new BCryptPasswordHasher<HearthCalendarUser>(
            Options.Create(new BCryptPasswordHasherOptions()));
        var user = new HearthCalendarUser { UserName = AdminUsername };

        var hash = hasher.HashPassword(user, AdminPassword);

        Assert.StartsWith("$2", hash, StringComparison.Ordinal);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, AdminPassword));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "wrong-password"));
        Assert.DoesNotContain(AdminPassword, hash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bootstrap_admin_is_stored_as_identity_user_with_bcrypt_password()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        _ = await client.GetAsync("/health");

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HearthCalendarUser>>();
        var admin = await users.FindByNameAsync(AdminUsername);

        await Verifier.Verify(new
        {
            HasAdmin = admin is not null,
            admin?.UserName,
            admin?.DisplayName,
            PasswordHashFormat = admin?.PasswordHash?[..2],
            IsAdminRole = admin is not null && await users.IsInRoleAsync(admin, HearthCalendarAuth.AdminRole),
            HasAdminScope = admin is not null && (await users.GetClaimsAsync(admin))
                .Any(claim => claim.Type == HearthCalendarAuth.ScopeClaim && claim.Value == HearthCalendarAuth.AdminWebScope),
            PasswordVerifies = admin is not null &&
                await users.CheckPasswordAsync(admin, AdminPassword),
            WrongPasswordVerifies = admin is not null &&
                await users.CheckPasswordAsync(admin, "wrong-password")
        });
    }
}
