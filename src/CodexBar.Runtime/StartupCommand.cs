namespace CodexBar.Runtime;

public enum StartupCommand
{
    Open,
    Overlay,
    Settings,
    TrayOnly
}

public static class StartupCommandResolver
{
    public static StartupCommand Resolve(IEnumerable<string>? args)
    {
        var values = args?.ToArray() ?? [];
        if (Contains(values, "--settings"))
        {
            return StartupCommand.Settings;
        }

        if (Contains(values, "--overlay"))
        {
            return StartupCommand.Overlay;
        }

        if (Contains(values, "--tray-only"))
        {
            return StartupCommand.TrayOnly;
        }

        return StartupCommand.Open;
    }

    private static bool Contains(IEnumerable<string> args, string expected)
        => args.Any(arg => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase));
}
