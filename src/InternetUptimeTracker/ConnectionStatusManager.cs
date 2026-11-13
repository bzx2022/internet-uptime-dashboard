using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InternetUptimeTracker;

public sealed class ConnectionStatusFile
{
    public bool Offline { get; set; }

    public DateTimeOffset? LastCheckedUtc { get; set; }

    public DateTimeOffset? OutageStartedUtc { get; set; }
}

/// <summary>
/// Maintains connection_status.json. The original dashboard only needs the
/// "Offline" property, so that keeps working, while the extra timestamps let
/// the new dashboard report an accurate ongoing outage and let the service
/// resume an outage across a restart.
/// </summary>
public sealed class ConnectionStatusManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _filePath;

    public ConnectionStatusManager(TrackerPaths paths)
    {
        _filePath = paths.StatusPath;
    }

    public ConnectionStatusFile? TryRead()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = ReadAllTextShared(_filePath);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<ConnectionStatusFile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Write(bool offline, DateTimeOffset? outageStartedUtc)
    {
        var status = new ConnectionStatusFile
        {
            Offline = offline,
            LastCheckedUtc = DateTimeOffset.UtcNow,
            OutageStartedUtc = outageStartedUtc
        };

        var json = JsonSerializer.Serialize(status, JsonOptions);
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        // Write to a sibling temp file and replace atomically. The dashboard
        // opens the file with delete sharing so this never races with a read.
        var temp = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, json, Utf8NoBom);
            File.Move(temp, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
