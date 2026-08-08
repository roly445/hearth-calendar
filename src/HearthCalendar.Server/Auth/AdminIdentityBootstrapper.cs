using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace HearthCalendar.Server.Auth;

public sealed class AdminIdentityBootstrapper(IServiceProvider services)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        BootstrapAsync(services, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task BootstrapAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var admins = ReadBootstrapAdmins(configuration);
        if (admins.Count == 0)
        {
            return;
        }

        var context = scope.ServiceProvider.GetRequiredService<HearthCalendarIdentityDbContext>();
        await EnsureIdentitySchemaAsync(context, cancellationToken);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<HearthCalendarUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await EnsureRoleAsync(roles, HearthCalendarAuth.AdminRole, cancellationToken);
        foreach (var admin in admins)
        {
            await EnsureAdminUserAsync(users, admin, cancellationToken);
        }
    }

    private static async Task EnsureIdentitySchemaAsync(
        HearthCalendarIdentityDbContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        await context.Database.EnsureCreatedAsync(cancellationToken);
        if (await IdentityTablesAreUsableAsync(context, cancellationToken))
        {
            return;
        }

        var creator = context.GetService<IRelationalDatabaseCreator>();
        await creator.CreateTablesAsync(cancellationToken);
    }

    private static async Task<bool> IdentityTablesAreUsableAsync(
        HearthCalendarIdentityDbContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await context.Users.AnyAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsMissingIdentityTable(exception))
        {
            return false;
        }
    }

    private static bool IsMissingIdentityTable(Exception exception) =>
        exception.GetType().FullName?.Contains("PostgresException", StringComparison.Ordinal) == true &&
        (exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("42P01", StringComparison.Ordinal));

    private static IReadOnlyList<AdminUserOptions> ReadBootstrapAdmins(IConfiguration configuration) =>
        configuration
            .GetSection("Auth:AdminUsers")
            .GetChildren()
            .Select(ReadBootstrapAdmin)
            .ToArray();

    private static AdminUserOptions ReadBootstrapAdmin(IConfigurationSection section)
    {
        var username = section["Username"];
        var displayName = section["DisplayName"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Bootstrap admin users require Username, DisplayName, and Password configuration values.");
        }

        return new AdminUserOptions
        {
            Username = username,
            DisplayName = displayName,
            Password = password,
            Scopes = section.GetSection("Scopes")
                .GetChildren()
                .Select(scope => scope.Value)
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => scope!)
                .ToArray()
        };
    }

    private static async Task EnsureRoleAsync(
        RoleManager<IdentityRole<Guid>> roles,
        string roleName,
        CancellationToken cancellationToken)
    {
        if (await roles.RoleExistsAsync(roleName))
        {
            return;
        }

        var result = await roles.CreateAsync(new IdentityRole<Guid>(roleName));
        ThrowIfFailed(result, $"create role '{roleName}'");
    }

    private static async Task EnsureAdminUserAsync(
        UserManager<HearthCalendarUser> users,
        AdminUserOptions admin,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByNameAsync(admin.Username);
        if (user is null)
        {
            user = new HearthCalendarUser
            {
                Id = Guid.NewGuid(),
                UserName = admin.Username,
                DisplayName = admin.DisplayName
            };
            var create = await users.CreateAsync(user, admin.Password);
            ThrowIfFailed(create, $"create bootstrap admin '{admin.Username}'");
        }
        else if (!string.Equals(user.DisplayName, admin.DisplayName, StringComparison.Ordinal))
        {
            user.DisplayName = admin.DisplayName;
            var update = await users.UpdateAsync(user);
            ThrowIfFailed(update, $"update bootstrap admin '{admin.Username}'");
        }

        if (!await users.IsInRoleAsync(user, HearthCalendarAuth.AdminRole))
        {
            var addRole = await users.AddToRoleAsync(user, HearthCalendarAuth.AdminRole);
            ThrowIfFailed(addRole, $"add bootstrap admin role to '{admin.Username}'");
        }

        var scopes = admin.Scopes.Count == 0 ? [HearthCalendarAuth.AdminWebScope] : admin.Scopes;
        var claims = await users.GetClaimsAsync(user);
        foreach (var scope in scopes)
        {
            if (claims.Any(claim => claim.Type == HearthCalendarAuth.ScopeClaim && claim.Value == scope))
            {
                continue;
            }

            var addClaim = await users.AddClaimAsync(user, new Claim(HearthCalendarAuth.ScopeClaim, scope));
            ThrowIfFailed(addClaim, $"add bootstrap admin scope '{scope}' to '{admin.Username}'");
        }
    }

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Failed to {action}: {errors}");
    }
}

public sealed class AdminIdentityBootstrapState
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool completed;

    public async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (completed)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (completed)
            {
                return;
            }

            await AdminIdentityBootstrapper.BootstrapAsync(services, cancellationToken);
            completed = true;
        }
        finally
        {
            gate.Release();
        }
    }
}

public static class AdminIdentityBootstrapMiddleware
{
    public static IApplicationBuilder UseAdminIdentityBootstrap(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var state = context.RequestServices.GetRequiredService<AdminIdentityBootstrapState>();
            await state.EnsureAsync(context.RequestServices, context.RequestAborted);
            await next();
        });
}
