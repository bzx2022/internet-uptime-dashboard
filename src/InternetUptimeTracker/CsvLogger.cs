using System.Globalization;
using System.Text;

namespace InternetUptimeTracker;

/// <summary>
/// Writes one row when an outage ends:
///
///   DateTime,DelayInSeconds
///   15/12/2024 23:52:35,7.04
///
/// DateTime is the recovery time and DelayInSeconds is the outage duration.
/// Durations are always written with an invariant decimal point and no
/// thousands separator so the CSV stays unambiguous.
/// </summary>
public sealed class CsvLogger
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public CsvLogger(TrackerPaths paths)
    {
        _filePath = paths.CsvPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        lock (_sync)
        {
            EnsureHeader();
        }
    }

    public void Log(DateTimeOffset recoveredAt, TimeSpan duration)
    {
        var recoveredLocal = recoveredAt.ToLocalTime();
        var timestamp = recoveredLocal.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        var seconds = Math.Max(0, duration.TotalSeconds).ToString("0.00", CultureInfo.InvariantCulture);

        lock (_sync)
        {
            EnsureHeader();
            File.AppendAllText(_filePath, $"{timestamp},{seconds}{Environment.NewLine}", Utf8NoBom);
        }
    }

    private void EnsureHeader()
    {
        if (File.Exists(_filePath) && new FileInfo(_filePath).Length > 0)
        {
            return;
        }

        File.WriteAllText(_filePath, "DateTime,DelayInSeconds" + Environment.NewLine, Utf8NoBom);
    }
}
