using BluQube.Authorization;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Features.Ui;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace HearthCalendar.Tests.Server;

public sealed class CredentialManagementAuthorizerAuthorizeTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Credential_management_requires_admin_scope()
    {
        var deniedAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var denied = await new CredentialInventoryQueryAuthorizer(deniedAccessor)
            .Authorize(new GetCredentialInventoryQuery(), CancellationToken.None);
        var allowedAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(HearthCalendarAuth.ScopeClaim, HearthCalendarAuth.AdminWebScope)],
                    "Test"))
            }
        };
        var allowed = await new CredentialInventoryQueryAuthorizer(allowedAccessor)
            .Authorize(new GetCredentialInventoryQuery(), CancellationToken.None);

        await Verifier.Verify(new
        {
            Denied = denied.IsAuthorized,
            Allowed = allowed.IsAuthorized
        });
    }

    [Theory]
    [InlineData(typeof(GetCredentialInventoryQuery), typeof(CredentialInventoryQueryAuthorizer))]
    [InlineData(typeof(CreateClientCredentialCommand), typeof(CreateClientCredentialCommandAuthorizer))]
    [InlineData(typeof(CreateFeedTokenCommand), typeof(CreateFeedTokenCommandAuthorizer))]
    [InlineData(typeof(CreateCalDavCredentialCommand), typeof(CreateCalDavCredentialCommandAuthorizer))]
    [InlineData(typeof(RotateClientCredentialCommand), typeof(RotateClientCredentialCommandAuthorizer))]
    [InlineData(typeof(RotateFeedTokenCommand), typeof(RotateFeedTokenCommandAuthorizer))]
    [InlineData(typeof(RotateCalDavCredentialCommand), typeof(RotateCalDavCredentialCommandAuthorizer))]
    [InlineData(typeof(RevokeClientCredentialCommand), typeof(RevokeClientCredentialCommandAuthorizer))]
    [InlineData(typeof(RevokeFeedTokenCommand), typeof(RevokeFeedTokenCommandAuthorizer))]
    [InlineData(typeof(RevokeCalDavCredentialCommand), typeof(RevokeCalDavCredentialCommandAuthorizer))]
    public void Credential_management_request_has_admin_authorizer(Type requestType, Type authorizerType)
    {
        var expectedContract = typeof(IBluQubeAuthorizer<>).MakeGenericType(requestType);

        Assert.True(expectedContract.IsAssignableFrom(authorizerType));
        Assert.True(typeof(AdminBluQubeAuthorizer<>).MakeGenericType(requestType).IsAssignableFrom(authorizerType));
    }
}
