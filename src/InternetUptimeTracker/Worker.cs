using Microsoft.Extensions.Options;

namespace InternetUptimeTracker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TrackerOptions _options;
    private readonly Pinger _pinger;
    private readonly CsvLogger _csvLogger;
    private readonly ConnectionStatusManager _statusManager;
    private readonly DataMigration _migration;
    private readonly TrackerPaths _paths;

    public Worker(
        ILogger<Worker> logger,
        IOptions<TrackerOptions> options,
        Pinger pinger,
        CsvLogger csvLogger,
        ConnectionStatusManager statusManager,
        DataMigration migration,
        TrackerPaths paths)
    {
        _logger = logger;
        _options = options.Value;
        _pinger = pinger;
        _csvLogger = csvLogger;
        _statusManager = statusManager;
        _migration = migration;
        _paths = paths;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _migration.Run();

        var interval = TimeSpan.FromMilliseconds(Math.Max(250, _options.CheckIntervalMs));
        var heartbeat = TimeSpan.FromMilliseconds(Math.Max(500, _options.StatusHeartbeatMs));
        var minimumOutage = TimeSpan.FromMilliseconds(Math.Max(0, _options.MinimumOutageMs));

        _logger.LogInformation(
            "Internet Uptime Tracker started. Host={Host}, interval={IntervalMs}ms, data={DataDirectory}.",
            _options.PingHost,
            interval.TotalMilliseconds,
            _paths.DataDirectory);

        var previous = _statusManager.TryRead();
        var offline = previous?.Offline;
        DateTimeOffset? outageStarted = offline == true
            ? previous?.OutageStartedUtc ?? previous?.LastCheckedUtc ?? DateTimeOffset.UtcNow
            : null;

        if (offline == true)
        {
            _logger.LogInformation(
                "Resuming an in-progress outage that began at {OutageStartedUtc}.",
                outageStarted);
        }

        bool? lastWrittenOffline = null;
        var lastWrite = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var result = await _pinger.PingAsync(stoppingToken);

                if (result.Ok)
                {
                    if (offline == true && outageStarted is not null)
                    {
                        var duration = now - outageStarted.Value;
                        if (duration < TimeSpan.Zero)
                        {
                            duration = TimeSpan.Zero;
                        }

                        if (duration >= minimumOutage)
                        {
                            try
                            {
                                _csvLogger.Log(now, duration);
                                _logger.LogWarning(
                                    "Internet restored after {DurationSeconds:0.00} seconds; outage recorded.",
                                    duration.TotalSeconds);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Could not write the outage to the CSV log.");
                            }
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Ignored a {DurationMs}ms outage below the {MinimumMs}ms threshold.",
                                duration.TotalMilliseconds,
                                minimumOutage.TotalMilliseconds);
                        }
                    }

                    offline = false;
                    outageStarted = null;
                }
                else if (offline != true)
                {
                    offline = true;
                    outageStarted = now;
                    _logger.LogWarning(
                        "Internet offline ({Reason}). Outage timer started.",
                        result.Error ?? "no reply");
                }

                var shouldWrite = lastWrittenOffline != offline || now - lastWrite >= heartbeat;
                if (shouldWrite)
                {
                    try
                    {
                        _statusManager.Write(offline == true, outageStarted);
                        lastWrite = now;
                        lastWrittenOffline = offline;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not write the connection status file.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected tracker error; the worker will continue.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Internet Uptime Tracker stopped.");
    }
}
