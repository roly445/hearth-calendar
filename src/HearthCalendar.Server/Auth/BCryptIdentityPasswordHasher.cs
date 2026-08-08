using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HearthCalendar.Server.Auth;

public sealed class BCryptIdentityPasswordHasher(
    IOptions<BCryptIdentityPasswordHasherOptions> options)
    : IPasswordHasher<HearthCalendarUser>
{
    private const string Prefix = "$2";

    public string HashPassword(HearthCalendarUser user, string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, options.Value.WorkFactor);

    public PasswordVerificationResult VerifyHashedPassword(
        HearthCalendarUser user,
        string hashedPassword,
        string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(hashedPassword) ||
            string.IsNullOrWhiteSpace(providedPassword) ||
            !hashedPassword.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return PasswordVerificationResult.Failed;
        }
    }
}

public sealed record BCryptIdentityPasswordHasherOptions
{
    public int WorkFactor { get; init; } = 12;
}
