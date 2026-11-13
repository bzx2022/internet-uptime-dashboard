using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace InternetUptimeTracker.Dashboard;

public sealed class StatusProbeService
{
    private readonly DataPaths _paths;
    private readonly ILogger<StatusProbeService> _logger;

    public StatusProbeService(DataPaths paths, ILogger<StatusProbeService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<object> GetCurrentStatusAsync(CancellationToken cancellationToken)
    {
        var trackerStatus = ReadTrackerStatus(_paths.StatusPath);
        var processInfo = CheckProcess(_paths.TrackerProcessName);

        using var ping = new Ping();
        var stopwatch = Stopwatch.StartNew();
        bool pingOk;
        long? roundtripMs = null;
        string? pingError = null;
        var commandAvailable = true;

        try
        {
            var reply = await ping
                .SendPingAsync(_paths.PingHost, Math.Max(250, _paths.PingTimeoutMs))
                .WaitAsync(cancellationToken);
            stopwatch.Stop();

            pingOk = reply.Status == IPStatus.Success;
            if (pingOk)
            {
                roundtripMs = reply.RoundtripTime;
            }
            else
            {
                pingError = reply.Status.ToString();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlatformNotSupportedException ex)
        {
            stopwatch.Stop();
            pingOk = false;
            pingError = ex.Message;
            commandAvailable = false;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            pingOk = false;
            pingError = ex.Message;
            _logger.LogDebug(ex, "Live ICMP probe failed.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var trackerSaysOnline =
            trackerStatus.Offline == false &&
            trackerStatus.LastStatusAgeSeconds is not null &&
            trackerStatus.LastStatusAgeSeconds < 6;

        // Live ICMP is authoritative. Fall back to the tracker's status file
        // only when ICMP itself is unavailable.
        var online = pingOk || (!commandAvailable && trackerSaysOnline);

        double? ongoingOutageSeconds = null;
        if (trackerStatus.Offline == true)
        {
            if (trackerStatus.OutageStartedMs is not null)
            {
                ongoingOutageSeconds = Math.Max(
                    0,
                    (now - trackerStatus.OutageStartedMs.Value) / 1000.0);
            }
            else if (trackerStatus.LastStatusMs is not null)
            {
                // Older status files only contain {"Offline": true}; in that
                // case the file timestamp is the best available outage start.
                ongoingOutageSeconds = Math.Max(
                    0,
                    (now - trackerStatus.LastStatusMs.Value) / 1000.0);
            }
        }

        return new
        {
            CheckedAt = now,
            Online = online,
            Ping = new
            {
                Ok = pingOk,
                Ms = roundtripMs,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = pingError,
                CommandAvailable = commandAvailable
            },
            Tracker = new
            {
                Offline = trackerStatus.Offline,
                Exists = trackerStatus.Exists,
                Error = trackerStatus.Error,
                LastStatusMs = trackerStatus.LastStatusMs,
                LastStatusAgeSeconds = trackerStatus.LastStatusAgeSeconds,
                Running = processInfo.Running,
                ProcessError = processInfo.Error,
                ProcessName = _paths.TrackerProcessName
            },
            OngoingOutageSeconds = ongoingOutageSeconds,
            Host = _paths.PingHost,
            Source = new
            {
                StatusPath = _paths.StatusPath,
                CsvPath = _paths.CsvPath
            }
        };
    }

    private static TrackerStatus ReadTrackerStatus(string statusPath)
    {
        var result = new TrackerStatus();

        try
        {
            var info = new FileInfo(statusPath);
            if (!info.Exists)
            {
                result.Error = "Status file not found";
                return result;
            }

            result.Exists = true;
            result.LastStatusMs = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            result.LastStatusAgeSeconds = Math.Max(
                0,
                (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - result.LastStatusMs.Value) / 1000.0);

            string json;
            using (var stream = new FileStream(
                       statusPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                json = reader.ReadToEnd();
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (TryGetProperty(root, "Offline", out var offline))
            {
                result.Offline = ParseBoolean(offline);
            }

            if (TryGetProperty(root, "OutageStartedUtc", out var started) &&
                started.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(
                    started.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var startedAt))
            {
                result.OutageStartedMs = startedAt.ToUnixTimeMilliseconds();
            }
        }
        catch (FileNotFoundException)
        {
            result.Error = "Status file not found";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private static bool? ParseBoolean(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when element.TryGetInt64(out var number) => number != 0,
            _ => null
        };
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value))
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static ProcessCheckResult CheckProcess(string processName)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(processName);
            var processes = Process.GetProcessesByName(name);
            try
            {
                return new ProcessCheckResult(processes.Length > 0, null);
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            return new ProcessCheckResult(null, ex.Message);
        }
    }

    private sealed class TrackerStatus
    {
        public bool? Offline { get; set; }

        public long? LastStatusMs { get; set; }

        public double? LastStatusAgeSeconds { get; set; }

        public long? OutageStartedMs { get; set; }

        public bool Exists { get; set; }

        public string? Error { get; set; }
    }

    private readonly record struct ProcessCheckResult(bool? Running, string? Error);
}
