namespace HearthCalendar.Server.Auth;

public static class AdminPasswordHashCommand
{
    private const string CommandName = "admin-password-hash";

    public static bool IsCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    public static int Run(
        IReadOnlyList<string> args,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        if (args.Count != 2 || !string.Equals(args[1], "--password-stdin", StringComparison.Ordinal))
        {
            error.WriteLine("Usage: dotnet run --no-launch-profile --project src/HearthCalendar.Server -- admin-password-hash --password-stdin");

            return 2;
        }

        var password = input.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
        {
            error.WriteLine("Password is required on stdin.");

            return 1;
        }

        output.WriteLine(HearthCalendarAdminPasswordHasher.Hash(password));

        return 0;
    }
}
