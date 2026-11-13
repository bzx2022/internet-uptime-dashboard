using System.Globalization;
using System.Text.RegularExpressions;

namespace InternetUptimeTracker.Dashboard;

/// <summary>
/// Port of the original dashboard statistics. It intentionally keeps the same
/// windowing, overlap clipping, rounding, labels and response field names so
/// the existing front-end renders exactly as before.
/// </summary>
public sealed partial class StatsBuilder
{
    private sealed record RangeSpec(string Key, int? Days);

    private sealed record ScaleSpec(int Buckets, string Label, string Title);

    private sealed record Bucket(long StartMs, long EndMs, string Label);

    private sealed record AvailabilityResult(long RangeStartMs, object Value);

    private static readonly IReadOnlyDictionary<string, ScaleSpec> ScaleMeta =
        new Dictionary<string, ScaleSpec>(StringComparer.Ordinal)
        {
            ["day"] = new(30, "Last 30 days", "Daily"),
            ["week"] = new(26, "Last 26 weeks", "Weekly"),
            ["month"] = new(24, "Last 24 months", "Monthly"),
            ["year"] = new(5, "Last 5 years", "Yearly")
        };

    [GeneratedRegex(@"^[+-]?\d+", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingIntegerRegex();

    public object Build(
        List<Outage> outages,
        CsvReadResult source,
        string? rangeValue,
        string? scaleValue)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var range = NormalizeRange(rangeValue);
        var scale = NormalizeScale(scaleValue);
        long? firstOutageMs = outages.Count > 0 ? outages[0].StartMs : null;
        long? lastOutageMs = outages.Count > 0 ? outages[^1].RecoveredMs : null;

        var availability = BuildAvailability(outages, range, nowMs, firstOutageMs);
        var summary = Summarize(
            outages,
            availability.RangeStartMs,
            nowMs,
            firstOutageMs,
            lastOutageMs);
        var allTime = BuildAllTime(outages, nowMs);
        var incidents = BuildIncidentSeries(outages, scale, nowMs);

        var recent = outages
            .Skip(Math.Max(0, outages.Count - 12))
            .Reverse()
            .Select(outage => new
            {
                StartMs = outage.StartMs,
                RecoveredMs = outage.RecoveredMs,
                DurationSeconds = outage.DurationSeconds
            })
            .ToList();

        return new
        {
            GeneratedAt = nowMs,
            Range = new { Key = range.Key, Days = range.Days },
            Availability = availability.Value,
            Summary = summary,
            AllTime = allTime,
            Incidents = incidents,
            Recent = recent,
            Source = new
            {
                File = source.File,
                Warnings = source.Warnings
            }
        };
    }

    private static AvailabilityResult BuildAvailability(
        List<Outage> outages,
        RangeSpec range,
        long nowMs,
        long? firstOutageMs)
    {
        long rangeStartMs;
        if (range.Days is not null)
        {
            rangeStartMs = StartOfDay(AddDays(nowMs, -(range.Days.Value - 1)));
        }
        else if (firstOutageMs is not null)
        {
            rangeStartMs = firstOutageMs.Value;
        }
        else
        {
            rangeStartMs = StartOfDay(AddDays(nowMs, -29));
        }

        var spanDays = Math.Max(1, (nowMs - rangeStartMs) / 86_400_000.0);
        var bucket = AvailabilityBucketFor(range.Days, spanDays);

        var buckets = bucket switch
        {
            "month" => BuildMonthBuckets(rangeStartMs, nowMs),
            "week" => BuildWeekBuckets(rangeStartMs, nowMs),
            _ => BuildDayBuckets(rangeStartMs, nowMs)
        };

        var points = new List<object>(buckets.Count);
        foreach (var entry in buckets)
        {
            var fromMs = Math.Max(entry.StartMs, rangeStartMs);
            var toMs = Math.Min(entry.EndMs, nowMs);
            var totalMs = Math.Max(0, toMs - fromMs);
            var downtimeMs = DowntimeInWindow(outages, fromMs, toMs);
            var uptimeMs = Math.Max(0, totalMs - downtimeMs);
            var count = outages.Count(o => o.RecoveredMs >= fromMs && o.RecoveredMs < toMs);

            points.Add(new
            {
                StartMs = entry.StartMs,
                EndMs = entry.EndMs,
                Label = entry.Label,
                TotalMs = totalMs,
                DowntimeMs = downtimeMs,
                DowntimeSeconds = downtimeMs / 1000.0,
                UptimeSeconds = uptimeMs / 1000.0,
                UptimePct = totalMs > 0 ? Round((double)uptimeMs / totalMs * 100, 4) : (double?)null,
                Count = count
            });
        }

        return new AvailabilityResult(
            rangeStartMs,
            new
            {
                RangeStartMs = rangeStartMs,
                RangeEndMs = nowMs,
                Bucket = bucket,
                Points = points
            });
    }

    private static object Summarize(
        List<Outage> outages,
        long fromMs,
        long toMs,
        long? firstOutageMs,
        long? lastOutageMs)
    {
        var inRange = outages
            .Where(outage => outage.RecoveredMs > fromMs && outage.StartMs < toMs)
            .ToList();

        var downtimeMs = DowntimeInWindow(outages, fromMs, toMs);
        var observedMs = Math.Max(0, toMs - fromMs);
        var uptimePct = observedMs > 0
            ? Round(Clamp((1 - (double)downtimeMs / observedMs) * 100, 0, 100), 4)
            : (double?)null;

        Outage? longest = null;
        long totalDurationMs = 0;
        foreach (var outage in inRange)
        {
            totalDurationMs += outage.DurationMs;
            if (longest is null || outage.DurationMs > longest.DurationMs)
            {
                longest = outage;
            }
        }

        object? longestValue = longest is null
            ? null
            : new
            {
                RecoveredMs = longest.RecoveredMs,
                StartMs = longest.StartMs,
                DurationSeconds = longest.DurationSeconds
            };

        return new
        {
            FromMs = fromMs,
            ToMs = toMs,
            ObservedSeconds = observedMs / 1000.0,
            DowntimeSeconds = downtimeMs / 1000.0,
            UptimePct = uptimePct,
            OutageCount = inRange.Count,
            Longest = longestValue,
            AverageOutageSeconds = inRange.Count > 0
                ? totalDurationMs / 1000.0 / inRange.Count
                : 0,
            FirstOutageMs = firstOutageMs,
            LastOutageMs = lastOutageMs
        };
    }

    private static object BuildAllTime(List<Outage> outages, long nowMs)
    {
        if (outages.Count == 0)
        {
            return new
            {
                UptimePct = (double?)null,
                DowntimeSeconds = 0.0,
                OutageCount = 0,
                ObservedSeconds = (double?)null,
                FirstOutageMs = (long?)null,
                LastOutageMs = (long?)null
            };
        }

        var firstMs = outages[0].StartMs;
        var lastMs = outages[^1].RecoveredMs;
        var observedMs = Math.Max(0, nowMs - firstMs);
        var downtimeMs = outages.Sum(outage => outage.DurationMs);

        return new
        {
            UptimePct = observedMs > 0
                ? Round(Clamp((1 - (double)downtimeMs / observedMs) * 100, 0, 100), 4)
                : (double?)null,
            DowntimeSeconds = downtimeMs / 1000.0,
            OutageCount = outages.Count,
            ObservedSeconds = observedMs / 1000.0,
            FirstOutageMs = (long?)firstMs,
            LastOutageMs = (long?)lastMs
        };
    }

    private static object BuildIncidentSeries(List<Outage> outages, string scale, long nowMs)
    {
        var meta = ScaleMeta[scale];

        var firstStart = scale switch
        {
            "day" => StartOfDay(AddDays(nowMs, -(meta.Buckets - 1))),
            "week" => AddDays(StartOfWeek(nowMs), -7 * (meta.Buckets - 1)),
            "month" => AddMonths(StartOfMonth(nowMs), -(meta.Buckets - 1)),
            _ => AddYears(StartOfYear(nowMs), -(meta.Buckets - 1))
        };

        var buckets = scale switch
        {
            "day" => BuildDayBuckets(firstStart, nowMs),
            "week" => BuildWeekBuckets(firstStart, nowMs),
            "month" => BuildMonthBuckets(firstStart, nowMs),
            _ => BuildYearBuckets(firstStart, nowMs)
        };

        var points = new List<object>(buckets.Count);
        foreach (var entry in buckets)
        {
            var inBucket = outages
                .Where(o => o.RecoveredMs >= entry.StartMs && o.RecoveredMs < entry.EndMs)
                .ToList();
            var durationMs = inBucket.Sum(o => o.DurationMs);

            points.Add(new
            {
                StartMs = entry.StartMs,
                EndMs = entry.EndMs,
                Label = entry.Label,
                Count = inBucket.Count,
                DowntimeSeconds = durationMs / 1000.0
            });
        }

        return new
        {
            Scale = scale,
            Title = meta.Title,
            Label = meta.Label,
            Points = points
        };
    }

    private static RangeSpec NormalizeRange(string? value)
    {
        var key = (value ?? "90").Trim();
        if (key.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new RangeSpec("all", null);
        }

        var match = LeadingIntegerRegex().Match(key);
        if (match.Success &&
            int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed is >= 1 and <= 3650)
        {
            return new RangeSpec(parsed.ToString(CultureInfo.InvariantCulture), parsed);
        }

        return new RangeSpec("90", 90);
    }

    private static string NormalizeScale(string? value)
    {
        var key = (value ?? "day").Trim().ToLowerInvariant();
        return ScaleMeta.ContainsKey(key) ? key : "day";
    }

    private static string AvailabilityBucketFor(int? days, double spanDays)
    {
        if (days is null)
        {
            return spanDays > 1000 ? "month" : "week";
        }

        if (days <= 120)
        {
            return "day";
        }

        return days <= 730 ? "week" : "month";
    }

    private static List<Bucket> BuildDayBuckets(long fromMs, long toMs)
    {
        var buckets = new List<Bucket>();
        var start = StartOfDay(fromMs);
        while (start < toMs)
        {
            var end = AddDays(start, 1);
            buckets.Add(new Bucket(start, end, DayLabel(start)));
            start = end;
        }

        return buckets;
    }

    private static List<Bucket> BuildWeekBuckets(long fromMs, long toMs)
    {
        var buckets = new List<Bucket>();
        var start = StartOfWeek(fromMs);
        while (start < toMs)
        {
            var end = AddDays(start, 7);
            buckets.Add(new Bucket(start, end, DayLabel(start)));
            start = end;
        }

        return buckets;
    }

    private static List<Bucket> BuildMonthBuckets(long fromMs, long toMs)
    {
        var buckets = new List<Bucket>();
        var start = StartOfMonth(fromMs);
        while (start < toMs)
        {
            var end = AddMonths(start, 1);
            buckets.Add(new Bucket(start, end, MonthLabel(start)));
            start = end;
        }

        return buckets;
    }

    private static List<Bucket> BuildYearBuckets(long fromMs, long toMs)
    {
        var buckets = new List<Bucket>();
        var start = StartOfYear(fromMs);
        while (start < toMs)
        {
            var end = AddYears(start, 1);
            buckets.Add(new Bucket(start, end, YearLabel(start)));
            start = end;
        }

        return buckets;
    }

    private static long DowntimeInWindow(List<Outage> outages, long fromMs, long toMs)
    {
        long total = 0;
        foreach (var outage in outages)
        {
            total += WindowOverlapMs(outage, fromMs, toMs);
        }

        return total;
    }

    private static long WindowOverlapMs(Outage item, long fromMs, long toMs)
    {
        var start = Math.Max(item.StartMs, fromMs);
        var end = Math.Min(item.RecoveredMs, toMs);
        return Math.Max(0, end - start);
    }

    private static double Round(double value, int digits = 4)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        var factor = Math.Pow(10, digits);
        return Math.Round(value * factor, MidpointRounding.AwayFromZero) / factor;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(max, Math.Max(min, value));

    private static DateTime Local(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    private static DateTime LocalStartOfDay(long ms) => Local(ms).Date;

    private static long ToMs(DateTime local) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    private static long StartOfDay(long ms) => ToMs(LocalStartOfDay(ms));

    private static long AddDays(long ms, int amount) => ToMs(Local(ms).AddDays(amount));

    private static long StartOfWeek(long ms)
    {
        var date = LocalStartOfDay(ms);
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return ToMs(date.AddDays(-offset));
    }

    private static long StartOfMonth(long ms)
    {
        var date = Local(ms);
        return ToMs(new DateTime(date.Year, date.Month, 1));
    }

    private static long AddMonths(long ms, int amount)
    {
        var date = Local(ms);
        return ToMs(new DateTime(date.Year, date.Month, 1).AddMonths(amount));
    }

    private static long StartOfYear(long ms)
    {
        var date = Local(ms);
        return ToMs(new DateTime(date.Year, 1, 1));
    }

    private static long AddYears(long ms, int amount) => ToMs(Local(ms).AddYears(amount));

    private static string DayLabel(long ms) =>
        Local(ms).ToString("d MMM", CultureInfo.InvariantCulture);

    private static string MonthLabel(long ms)
    {
        var date = Local(ms);
        return $"{date.ToString("MMM", CultureInfo.InvariantCulture)} '{date.Year % 100:00}";
    }

    private static string YearLabel(long ms) =>
        Local(ms).Year.ToString(CultureInfo.InvariantCulture);
}
