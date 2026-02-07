namespace WireBound.Elevation.Windows;

/// <summary>
/// Parses command-line arguments for the Windows elevation helper.
/// Extracted from Program.cs for testability.
/// </summary>
internal static class CliParser
{
    /// <summary>
    /// Extracts the --caller-sid value from command-line arguments.
    /// Returns null if the argument is not present or has no value.
    /// </summary>
    internal static string? ParseCallerSid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--caller-sid", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
