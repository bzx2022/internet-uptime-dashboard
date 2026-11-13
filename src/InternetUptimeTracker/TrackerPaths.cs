using Microsoft.Extensions.Options;

namespace InternetUptimeTracker;

/// <summary>
/// Resolves the tracker's data files once at startup. Relative paths are
/// resolved against the application directory so the service behaves the same
/// whether it is started by the service control manager or from a terminal.
/// </summary>
public sealed class TrackerPaths
{
    public TrackerPaths(IOptions<TrackerOptions> options)
    {
        var value = options.Value;

        DataDirectory = PathUtility.ResolveDirectory(value.DataDirectory);
        CsvPath = Path.Combine(DataDirectory, Path.GetFileName(value.CsvFileName));
        StatusPath = Path.Combine(DataDirectory, Path.GetFileName(value.StatusFileName));
        LegacyDataDirectory = string.IsNullOrWhiteSpace(value.LegacyDataDirectory)
            ? null
            : PathUtility.ResolveDirectory(value.LegacyDataDirectory);
        MigrateLegacyData = value.MigrateLegacyData;
    }

    public string DataDirectory { get; }

    public string CsvPath { get; }

    public string StatusPath { get; }

    public string? LegacyDataDirectory { get; }

    public bool MigrateLegacyData { get; }
}

internal static class PathUtility
{
    public static string ResolveDirectory(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(AppContext.BaseDirectory, expanded);
        }

        return Path.GetFullPath(expanded);
    }
}
