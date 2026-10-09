using System.Diagnostics.Eventing.Reader;
using SystemCrashInspector.Models;

namespace SystemCrashInspector.Services;

public sealed class CrashEventReader
{
    private const int MaximumEvents = 2000;

    private static readonly (string Log, int[] Ids)[] Sources =
    [
        ("System", [41, 1001, 1074, 6005, 6006, 6008]),
        ("Application", [1000, 1001, 1002])
    ];

    public IReadOnlyList<CrashEvent> Read(DateTime since, CancellationToken cancellationToken)
    {
        var events = new List<CrashEvent>();

        foreach (var (log, ids) in Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var idsQuery = string.Join(" or ", ids.Select(id => $"EventID={id}"));
            var query = new EventLogQuery(log, PathType.LogName,
                $"*[System[({idsQuery})]]")
            {
                ReverseDirection = true
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
                    message = record.FormatDescription() ?? string.Empty;
                }
                catch (EventLogException)
                {
                    message = "Event description is unavailable.";
                }

                events.Add(new CrashEvent(
                    record.TimeCreated,
                    log,
                    record.Id,
                    record.ProviderName ?? "Unknown",
                    Categorize(log, record.Id),
                    record.LevelDisplayName ?? "Unknown",
                    message,
                    record.RecordId));
            }
        }

        return events
            .OrderByDescending(e => e.TimeCreated)
            .Take(MaximumEvents)
            .ToArray();
    }

    private static string Categorize(string log, int id) => (log, id) switch
    {
        ("System", 41) => "Unexpected power loss or restart",
        ("System", 6008) => "Unexpected shutdown",
        ("System", 1001) => "Bug check",
        ("System", 1074) => "Planned shutdown or restart",
        ("System", 6005) => "Event Log service started",
        ("System", 6006) => "Event Log service stopped",
        ("Application", 1000) => "Application crash",
        ("Application", 1001) => "Windows Error Reporting",
        ("Application", 1002) => "Application hang",
        _ => "System event"
    };
}
