using BluQube.Authorization;
using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace HearthCalendar.Tests.Server;

public sealed class AdminBluQubeAuthorizerAuthorizeTests : CalendarUiFeatureTestBase
{
    [Fact]
    public async Task Admin_authorizer_requires_admin_scope()
    {
        var deniedAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var denied = await new ReviewQueueQueryAuthorizer(deniedAccessor).Authorize(new GetReviewQueueQuery(), CancellationToken.None);

        Assert.False(denied.IsAuthorized);
    }

    [Fact]
    public async Task Admin_authorizer_allows_admin_scope()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(HearthCalendarAuth.ScopeClaim, HearthCalendarAuth.AdminWebScope)],
                    "Test"))
            }
        };

        var result = await new ReviewQueueQueryAuthorizer(accessor).Authorize(new GetReviewQueueQuery(), CancellationToken.None);

        Assert.True(result.IsAuthorized);
    }

    [Theory]
    [InlineData(typeof(GetReviewQueueQuery), typeof(ReviewQueueQueryAuthorizer))]
    [InlineData(typeof(GetUpcomingEventsQuery), typeof(UpcomingEventsQueryAuthorizer))]
    [InlineData(typeof(SubmitWebEventIntentCommand), typeof(SubmitWebEventIntentCommandAuthorizer))]
    [InlineData(typeof(ApproveReviewItemCommand), typeof(ApproveReviewItemCommandAuthorizer))]
    [InlineData(typeof(RejectReviewItemCommand), typeof(RejectReviewItemCommandAuthorizer))]
    [InlineData(typeof(EditReviewItemCommand), typeof(EditReviewItemCommandAuthorizer))]
    [InlineData(typeof(DeleteEventCommand), typeof(DeleteEventCommandAuthorizer))]
    [InlineData(typeof(RescheduleEventCommand), typeof(RescheduleEventCommandAuthorizer))]
    public void Ui_bluqube_request_has_admin_authorizer(Type requestType, Type authorizerType)
    {
        var expectedContract = typeof(IBluQubeAuthorizer<>).MakeGenericType(requestType);

        Assert.True(expectedContract.IsAssignableFrom(authorizerType));
        Assert.True(typeof(AdminBluQubeAuthorizer<>).MakeGenericType(requestType).IsAssignableFrom(authorizerType));
    }
}
