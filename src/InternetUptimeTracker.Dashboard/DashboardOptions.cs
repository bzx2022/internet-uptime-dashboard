using Microsoft.Extensions.Options;

namespace InternetUptimeTracker.Dashboard;

public sealed class DashboardOptions
{
    public string Url { get; set; } = "http://127.0.0.1:8787";
}

public sealed class DataOptions
{
    public string DataDirectory { get; set; } = @"C:\ProgramData\InternetUptimeTracker";

    public string CsvFileName { get; set; } = "downtime_log.csv";

    public string StatusFileName { get; set; } = "connection_status.json";

    public string? LegacyDataDirectory { get; set; } = @"D:\InternetUptimeTracker";

    public string PingHost { get; set; } = "8.8.8.8";

    public int PingTimeoutMs { get; set; } = 1500;

    public string TrackerProcessName { get; set; } = "InternetUptimeTracker.exe";
}

/// <summary>
/// Resolves data files on every request. If the new ProgramData log does not
/// exist yet (for example during an upgrade from the old D: install), the
/// legacy location is used so the dashboard still shows history immediately.
/// </summary>
public sealed class DataPaths
{
    private readonly DataOptions _options;

    public DataPaths(IOptions<DataOptions> options)
    {
        _options = options.Value;
    }

    public string DataDirectory => PathUtility.ResolveDirectory(_options.DataDirectory);

    public string CsvPath => ResolveFile(_options.CsvFileName);

    public string StatusPath => ResolveFile(_options.StatusFileName);

    public string PingHost => _options.PingHost;

    public int PingTimeoutMs => _options.PingTimeoutMs;

    public string TrackerProcessName => _options.TrackerProcessName;

    private string ResolveFile(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var primary = Path.Combine(DataDirectory, name);
        if (File.Exists(primary))
        {
            return primary;
        }

        if (!string.IsNullOrWhiteSpace(_options.LegacyDataDirectory))
        {
            var legacyDirectory = PathUtility.ResolveDirectory(_options.LegacyDataDirectory);
            var legacy = Path.Combine(legacyDirectory, name);
            if (File.Exists(legacy))
            {
                return legacy;
            }
        }

        return primary;
    }
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
