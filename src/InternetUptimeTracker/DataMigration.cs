namespace InternetUptimeTracker;

/// <summary>
/// Copies the old D:\InternetUptimeTracker data into the new ProgramData
/// location the first time the service starts. Existing new data is never
/// overwritten, and the legacy folder is left untouched.
/// </summary>
public sealed class DataMigration
{
    private readonly TrackerPaths _paths;
    private readonly ILogger<DataMigration> _logger;

    public DataMigration(TrackerPaths paths, ILogger<DataMigration> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Run()
    {
        Directory.CreateDirectory(_paths.DataDirectory);

        if (!_paths.MigrateLegacyData || _paths.LegacyDataDirectory is null)
        {
            return;
        }

        var legacy = _paths.LegacyDataDirectory;
        if (string.Equals(
                Path.GetFullPath(legacy),
                Path.GetFullPath(_paths.DataDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CopyIfMissing(
            Path.Combine(legacy, Path.GetFileName(_paths.CsvPath)),
            _paths.CsvPath,
            "outage log");

        CopyIfMissing(
            Path.Combine(legacy, Path.GetFileName(_paths.StatusPath)),
            _paths.StatusPath,
            "status file");
    }

    private static bool HasDataRows(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim().TrimStart('\uFEFF');
                if (trimmed.Length == 0 ||
                    trimmed.StartsWith("DateTime,", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }
        }
        catch
        {
            // If the existing file cannot be inspected, keep it rather than
            // risk overwriting data.
            return true;
        }

        return false;
    }

    private void CopyIfMissing(string source, string destination, string description)
    {
        try
        {
            if (!File.Exists(source))
            {
                return;
            }

            // A freshly constructed CsvLogger creates a header-only file. Treat
            // that as empty so the first upgrade still imports the old history.
            if (File.Exists(destination) && HasDataRows(destination))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            _logger.LogInformation(
                "Migrated legacy {Description} from {Source} to {Destination}.",
                description,
                source,
                destination);
        }
        catch (Exception ex)
        {
            // A missing upgrade should never stop the service. The dashboard can
            // still fall back to reading the legacy path directly.
            _logger.LogWarning(
                ex,
                "Could not migrate legacy {Description} from {Source}.",
                description,
                source);
        }
    }
}
