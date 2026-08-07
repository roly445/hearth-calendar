using HearthCalendar.Server.Auth;
using Command = HearthCalendar.Server.Auth.AdminPasswordHashCommand;

namespace HearthCalendar.Tests.Server.AdminPasswordHashCommand;

public sealed class RunTests
{
    [Fact]
    public void Valid_password_from_stdin_writes_matching_hash_only()
    {
        const string password = "correct horse battery staple";
        using var input = new StringReader(password);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Command.Run(
            ["admin-password-hash", "--password-stdin"],
            input,
            output,
            error);

        var hash = output.ToString().Trim();
        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.StartsWith("pbkdf2-sha256:", hash, StringComparison.Ordinal);
        Assert.True(HearthCalendarAdminPasswordHasher.Matches(password, hash));
        Assert.DoesNotContain(password, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_password_stdin_fails_without_writing_hash()
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Command.Run(
            ["admin-password-hash", "--password-stdin"],
            input,
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Password is required", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_arguments_fail_with_usage()
    {
        using var input = new StringReader("password");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Command.Run(
            ["admin-password-hash"],
            input,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);
    }
}
