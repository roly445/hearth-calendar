using BluQube.Constants;
using FluentValidation;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

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
}
