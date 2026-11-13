using System.Globalization;
using System.Text.RegularExpressions;

namespace InternetUptimeTracker.Dashboard;

/// <summary>
/// Reads the tracker's CSV log. Each row is one completed outage:
///
///   DateTime,DelayInSeconds
///   15/12/2024 23:52:35,7.04
///
/// DateTime is the recovery time; DelayInSeconds is the outage duration. The
/// parser deliberately splits on the first comma so older rows that contain a
/// thousands separator in the duration (for example 1,310.05) still work.
/// </summary>
public static partial class CsvOutageReader
{
    [GeneratedRegex(@"^(\d{1,2})/(\d{1,2})/(\d{4})(?:\s+|,)(\d{1,2}):(\d{2}):(\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex DmyDateTimeRegex();

    [GeneratedRegex(@"^""?date\s*time""?\s*,", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    public static CsvReadResult Read(string csvPath)
    {
        var outages = new List<Outage>();
        var warnings = new List<string>();

        CsvFileInfo fileInfo;
        try
        {
            var info = new FileInfo(csvPath);
            if (!info.Exists)
            {
                warnings.Add($"CSV log not found at {csvPath}");
                return new CsvReadResult(
                    outages,
                    warnings,
                    new CsvFileInfo(csvPath, false, 0, null));
            }

            fileInfo = new CsvFileInfo(
                csvPath,
                true,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read CSV log: {ex.Message}");
            return new CsvReadResult(
                outages,
                warnings,
                new CsvFileInfo(csvPath, false, 0, null));
        }

        string text;
        try
        {
            text = File.ReadAllText(csvPath);
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not open CSV log: {ex.Message}");
            return new CsvReadResult(outages, warnings, fileInfo);
        }

        text = text.TrimStart('\uFEFF');
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var skipped = 0;
        var sawHeader = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!sawHeader && HeaderRegex().IsMatch(trimmed))
            {
                sawHeader = true;
                continue;
            }

            var comma = trimmed.IndexOf(',');
            if (comma < 0)
            {
                skipped++;
                continue;
            }

            var dateText = trimmed[..comma].Trim().Trim('"');
            var delayText = trimmed[(comma + 1)..].Trim().Trim('"');

            var recoveredMs = ParseDateTime(dateText);
            var durationSeconds = ParseDelay(delayText);

            if (recoveredMs is null || durationSeconds is null)
            {
                skipped++;
                continue;
            }

            var durationMs = (long)Math.Round(
                durationSeconds.Value * 1000.0,
                MidpointRounding.AwayFromZero);

            outages.Add(new Outage(
                recoveredMs.Value,
                recoveredMs.Value - durationMs,
                durationMs,
                durationSeconds.Value));
        }

        outages.Sort(static (a, b) =>
        {
            var byRecovery = a.RecoveredMs.CompareTo(b.RecoveredMs);
            return byRecovery != 0 ? byRecovery : a.StartMs.CompareTo(b.StartMs);
        });

        if (skipped > 0)
        {
            warnings.Add(
                $"Skipped {skipped} unreadable line{(skipped == 1 ? string.Empty : "s")} in the CSV log.");
        }

        return new CsvReadResult(outages, warnings, fileInfo);
    }

    internal static long? ParseDateTime(string value)
    {
        var text = (value ?? string.Empty).Trim().Trim('"');

        var match = DmyDateTimeRegex().Match(text);
        if (match.Success)
        {
            try
            {
                var date = new DateTime(
                    int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture),
                    DateTimeKind.Local);

                return new DateTimeOffset(date).ToUnixTimeMilliseconds();
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        // Fallback for ISO-style rows in case the tracker format changes.
        if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed) ||
            DateTime.TryParse(
                text,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal,
                out parsed))
        {
            if (parsed.Kind == DateTimeKind.Unspecified)
            {
                parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            return new DateTimeOffset(parsed).ToUnixTimeMilliseconds();
        }

        return null;
    }

    internal static double? ParseDelay(string value)
    {
        var text = (value ?? string.Empty).Trim().Trim('"');
        if (text.Length == 0)
        {
            return null;
        }

        text = text.Replace(",", string.Empty, StringComparison.Ordinal);
        return double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var number) && number >= 0 && double.IsFinite(number)
            ? number
            : null;
    }
}
