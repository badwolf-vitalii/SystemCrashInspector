using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using SystemCrashInspector.Models;

namespace SystemCrashInspector.Services;

public sealed record CrashEventReadResult(IReadOnlyList<CrashEvent> Events, IReadOnlyList<string> Warnings);

public sealed class CrashEventReader
{
    private const int MaximumEvents = 2000;

    private static readonly (string Log, int[] Ids)[] Sources =
    [
        ("System", [1, 7, 11, 14, 15, 17, 18, 19, 20, 41, 51, 55, 100, 129, 153, 157, 219, 4101, 1001, 1074, 6005, 6006, 6008]),
        ("Application", [1000, 1001, 1002, 1026])
    ];

    public CrashEventReadResult Read(DateTime since, CancellationToken cancellationToken)
    {
        var events = new List<CrashEvent>();
        var warnings = new List<string>();

        foreach (var (log, ids) in Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var idsQuery = string.Join(" or ", ids.Select(id => $"EventID={id}"));
                var query = new EventLogQuery(log, PathType.LogName,
                    $"*[System[({idsQuery})]]")
                {
                    ReverseDirection = true,
                    TolerateQueryErrors = true
                };

                using var reader = new EventLogReader(query);
                while (events.Count < MaximumEvents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var record = reader.ReadEvent();
                    if (record is null)
                        break;

                    if (record.TimeCreated is { } time && time < since)
                        break;

                    string message;
                    try
                    {
                        message = record.FormatDescription() ?? "No event description was provided.";
                    }
                    catch (Exception ex) when (ex is EventLogException or Win32Exception or
                                               FileNotFoundException or DirectoryNotFoundException)
                    {
                        // Event providers can have missing message DLLs even when the log entry is valid.
                        message = $"Event description is unavailable ({ex.Message}).";
                    }

                    events.Add(new CrashEvent(
                        record.TimeCreated,
                        log,
                        record.Id,
                        record.ProviderName ?? "Unknown",
                        Categorize(log, record.Id),
                        record.LevelDisplayName ?? "Unknown",
                        message,
                        record.RecordId,
                        SafeXml(record)));
                }
            }
            catch (Exception ex) when (ex is EventLogException or Win32Exception or
                                       FileNotFoundException or DirectoryNotFoundException)
            {
                warnings.Add($"Unable to read the {log} log: {ex.Message}");
            }
        }

        return new CrashEventReadResult(
            events.OrderByDescending(e => e.TimeCreated).Take(MaximumEvents).ToArray(),
            warnings);
    }

    private static string SafeXml(EventRecord record)
    {
        try
        {
            return record.ToXml();
        }
        catch (EventLogException ex)
        {
            return $"Event XML could not be retrieved: {ex.Message}";
        }
    }

    private static string Categorize(string log, int id) => (log, id) switch
    {
        ("System", 4101) => "Display driver recovery",
        ("System", 17 or 18 or 19 or 20) => "Possible hardware error (verify WHEA provider)",
        ("System", 7 or 11 or 15 or 51 or 55 or 129 or 153 or 157) => "Possible storage error (verify source)",
        ("System", 41) => "Unexpected power loss or restart",
        ("System", 6008) => "Unexpected shutdown",
        ("System", 1001) => "Bug check",
        ("System", 1074) => "Planned shutdown or restart",
        ("System", 6005) => "Event Log service started",
        ("System", 6006) => "Event Log service stopped",
        ("Application", 1026) => ".NET Runtime exception (verify source)",
        ("Application", 1000) => "Application crash",
        ("Application", 1001) => "Windows Error Reporting",
        ("Application", 1002) => "Application hang",
        _ => "System event"
    };
}
