namespace InternetUptimeTracker.Dashboard;

public sealed record Outage(
    long RecoveredMs,
    long StartMs,
    long DurationMs,
    double DurationSeconds);

public sealed record CsvFileInfo(
    string Path,
    bool Exists,
    long Size,
    long? ModifiedMs);

public sealed record CsvReadResult(
    List<Outage> Outages,
    List<string> Warnings,
    CsvFileInfo File);
