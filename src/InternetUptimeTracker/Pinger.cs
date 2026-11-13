using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;

namespace InternetUptimeTracker;

public readonly record struct PingResult(
    bool Ok,
    long? RoundtripMs,
    long ElapsedMs,
    string? Error)
{
    public static PingResult Success(long roundtripMs, long elapsedMs) =>
        new(true, roundtripMs, elapsedMs, null);

    public static PingResult Failure(long elapsedMs, string error) =>
        new(false, null, elapsedMs, error);
}

public sealed class Pinger
{
    private readonly TrackerOptions _options;

    public Pinger(IOptions<TrackerOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PingResult> PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var ping = new Ping();
            var reply = await ping
                .SendPingAsync(_options.PingHost, Math.Max(250, _options.PingTimeoutMs))
                .WaitAsync(cancellationToken);
            stopwatch.Stop();

            return reply.Status == IPStatus.Success
                ? PingResult.Success(reply.RoundtripTime, stopwatch.ElapsedMilliseconds)
                : PingResult.Failure(stopwatch.ElapsedMilliseconds, reply.Status.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return PingResult.Failure(stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }
}
