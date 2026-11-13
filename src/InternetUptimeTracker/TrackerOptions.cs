namespace InternetUptimeTracker;

/// <summary>
/// Configuration for the tracker worker. Values come from the "Tracker"
/// section of appsettings.json and can be overridden with IUT_ environment
/// variables (for example IUT_Tracker__PingHost).
/// </summary>
public sealed class TrackerOptions
{
    public string PingHost { get; set; } = "8.8.8.8";

    public int PingTimeoutMs { get; set; } = 2000;

    public int CheckIntervalMs { get; set; } = 1000;

    /// <summary>
    /// An outage shorter than this is treated as a single dropped packet and is
    /// not written to the CSV log.
    /// </summary>
    public int MinimumOutageMs { get; set; } = 2000;

    /// <summary>
    /// How often the status file is refreshed while nothing changes. This keeps
    /// the dashboard's "live" indicator accurate.
    /// </summary>
    public int StatusHeartbeatMs { get; set; } = 2000;

    public string DataDirectory { get; set; } = @"C:\ProgramData\InternetUptimeTracker";

    public string CsvFileName { get; set; } = "downtime_log.csv";

    public string StatusFileName { get; set; } = "connection_status.json";

    /// <summary>
    /// Optional previous data location. On first start the tracker copies a
    /// legacy log into <see cref="DataDirectory"/> so no history is lost.
    /// </summary>
    public string? LegacyDataDirectory { get; set; } = @"D:\InternetUptimeTracker";

    public bool MigrateLegacyData { get; set; } = true;
}
