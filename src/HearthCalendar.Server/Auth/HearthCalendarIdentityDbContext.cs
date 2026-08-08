using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HearthCalendar.Server.Auth;

public sealed class HearthCalendarIdentityDbContext(
    DbContextOptions<HearthCalendarIdentityDbContext> options,
    IOptions<HearthCalendarDatabaseOptions> databaseOptions)
    : IdentityDbContext<HearthCalendarUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var schemaName = databaseOptions.Value.SchemaName;
        if (!string.IsNullOrWhiteSpace(schemaName))
        {
            builder.HasDefaultSchema(schemaName);
        }
    }
}
