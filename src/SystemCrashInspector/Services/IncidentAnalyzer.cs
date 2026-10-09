using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SystemCrashInspector.Models;

namespace SystemCrashInspector.Services;

public sealed record IncidentEvidence(DateTime Time, string Title, string Message);

public sealed record IncidentDump(string Path, DateTime Modified, long Size)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Info => $"{Modified:yyyy-MM-dd HH:mm:ss}  ·  {Size / 1024d / 1024d:F1} MB";
}

public sealed record CrashIncident(DateTime Time, string Type, string Confidence, string Summary, string Details)
{
    public string? BugCheckCode { get; init; }
    public IReadOnlyList<IncidentEvidence> Evidence { get; init; } = [];
    public IReadOnlyList<IncidentDump> Dumps { get; init; } = [];

    public int EvidenceCount => Evidence.Count;
    public int DumpCount => Dumps.Count;
    public string BugCheckLabel => BugCheckCode ?? "—";
    public bool IsSystemRestart => Type.StartsWith("System restart", StringComparison.Ordinal);
    public string Icon => IsSystemRestart ? "⚡" : "!";
    public string StatusLabel => IsSystemRestart
        ? BugCheckCode is null ? "UNCLEAN RESTART" : "BUGCHECK CONFIRMED"
        : Type == "Application hang" ? "APPLICATION HANG" : "APPLICATION CRASH";

    public string DisplayTitle => IsSystemRestart
        ? BugCheckCode?.ToUpperInvariant() switch
        {
            "0X00000116" or "0X116" => "VIDEO_TDR_FAILURE",
            "0X00000124" or "0X124" => "WHEA_UNCORRECTABLE_ERROR",
            "0X0000009F" or "0X9F" => "DRIVER_POWER_STATE_FAILURE",
            "0X0000007E" or "0X7E" => "SYSTEM_THREAD_EXCEPTION",
            "0X0000000A" or "0XA" => "IRQL_NOT_LESS_OR_EQUAL",
            null => "Unexpected system restart",
            _ => "Windows bugcheck"
        }
        : Summary;

    public string Explanation => IsSystemRestart
        ? BugCheckCode?.ToUpperInvariant() is "0X00000116" or "0X116"
            ? "Windows could not recover the graphics adapter after a display-driver timeout. The display stack failed to reset (VIDEO_TDR_FAILURE). This does not by itself prove whether the fault is in the driver, GPU, power delivery, or other hardware."
            : BugCheckCode is not null
                ? "Windows recorded a bugcheck and an unclean restart. Correlated logs may help identify related symptoms; the dump still needs debugging for driver or kernel attribution."
                : "Windows restarted without a clean shutdown. The cause is unknown without additional evidence."
        : Type == "Application hang"
            ? "An application stopped responding. This is not necessarily a system crash."
            : "Windows recorded a process crash. It is a separate incident and does not explain an earlier system restart.";

    public string NextStep => IsSystemRestart
        ? BugCheckCode is null
            ? "Inspect hardware, power, and thermal stability and verify Windows dump configuration."
            : Dumps.Count > 0
                ? "Open the matching minidump in WinDbg and run !analyze -v. Check the failure bucket, stack, and module information before changing drivers or hardware settings."
                : "Check Windows crash-dump settings and look for additional WER or System event evidence."
        : "Check the process exception information and any matching WER report or application dump.";
}

/// <summary>Groups evidence into incidents; never treats temporal coincidence as proof of causation.</summary>
public static class IncidentAnalyzer
{
    public static IReadOnlyList<CrashIncident> Analyze(IReadOnlyList<CrashEvent> events, CancellationToken cancellationToken)
    {
        var ordered = events.Where(e => e.TimeCreated.HasValue).OrderBy(e => e.TimeCreated).ToArray();
        var incidents = new List<CrashIncident>();
        var assigned = new HashSet<CrashEvent>();
        var dumps = FindDumps(cancellationToken);
        foreach (var e in ordered.Where(IsRestartEvidence))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assigned.Contains(e)) continue;
            var time = e.TimeCreated!.Value;
            // BugCheck 1001 is often logged after Windows has already restarted.
            var group = ordered.Where(x => IsRestartEvidence(x) && x.TimeCreated.HasValue &&
                Math.Abs((x.TimeCreated.Value - time).TotalMinutes) <= 10).ToArray();
            foreach (var x in group) assigned.Add(x);
            var anchor = group.FirstOrDefault(x => x.EventId == 41)?.TimeCreated ?? time;
            var related = ordered.Where(x => x.TimeCreated is { } t && t >= anchor.AddMinutes(-10) &&
                t <= anchor.AddMinutes(5)).ToArray();
            var bugcheck = group.FirstOrDefault(IsBugcheck);
            var code = bugcheck is null ? null : ExtractBugcheck(bugcheck);
            var dumpMatches = dumps.Where(d => Math.Abs((d.Time - anchor).TotalMinutes) <= 15).ToArray();
            var clues = related.Where(x => x.Source.Contains("WHEA", StringComparison.OrdinalIgnoreCase) ||
                x.EventId == 4101 && x.Source.Contains("Display", StringComparison.OrdinalIgnoreCase) ||
                x.Source.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
                x.Source.Contains("stor", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var summary = bugcheck is null
                ? "Unclean restart detected; no matching BugCheck event was collected."
                : $"Windows bugcheck recorded{(code is null ? "" : $" ({code})")}; {(dumpMatches.Length > 0 ? "matching dump file found" : "no matching dump located")}.";
            var details = new StringBuilder();
            details.AppendLine("SYSTEM RESTART INCIDENT");
            details.AppendLine($"Restart evidence time: {anchor:yyyy-MM-dd HH:mm:ss}");
            details.AppendLine($"Assessment: {(bugcheck is null ? "Unconfirmed cause" : "BugCheck confirmed; cause not determined")}");
            details.AppendLine(summary);
            details.AppendLine();
            details.AppendLine("EVIDENCE");
            foreach (var ev in group.OrderBy(x => x.TimeCreated))
                details.AppendLine($"- {ev.TimeCreated:HH:mm:ss} {ev.Source} (ID {ev.EventId}): {Trim(ev.Message)}");
            details.AppendLine();
            details.AppendLine("POSSIBLE PRECURSOR EVENTS (not automatically causal)");
            if (clues.Length == 0) details.AppendLine("No GPU / WHEA / storage indicators among collected events.");
            foreach (var clue in clues)
                details.AppendLine($"- {clue.TimeCreated:HH:mm:ss} {clue.Source} (ID {clue.EventId}): {Trim(clue.Message)}");
            details.AppendLine();
            details.AppendLine("NEARBY WINDOWS SYSTEM DUMPS");
            if (dumpMatches.Length == 0) details.AppendLine("None found in the standard Windows dump folders.");
            foreach (var dump in dumpMatches)
                details.AppendLine($"- {dump.Time:yyyy-MM-dd HH:mm:ss} | {dump.Size:N0} bytes | {dump.Path}");
            details.AppendLine();
            details.AppendLine("NEXT STEP");
            details.AppendLine(bugcheck is null
                ? "Check Windows memory dump settings and investigate power, thermal, and hardware stability."
                : "Analyze the matching Windows minidump in WinDbg using !analyze -v. A dump is evidence, not automatic proof of a faulty driver.");
            details.AppendLine("Other application crashes outside this incident are intentionally excluded.");
            incidents.Add(new CrashIncident(anchor, "System restart / BSOD",
                bugcheck is null ? "Unconfirmed" : "Confirmed", summary, details.ToString())
            {
                BugCheckCode = code,
                Evidence = group.Concat(clues).Distinct().OrderBy(x => x.TimeCreated)
                    .Select(x => new IncidentEvidence(x.TimeCreated!.Value,
                        $"{x.Source} (Event ID {x.EventId})", Trim(x.Message))).ToArray(),
                Dumps = dumpMatches.Select(x => new IncidentDump(x.Path, x.Time, x.Size)).ToArray()
            });
        }

        // Standalone application crashes are separate incidents; never attach a later VS crash to a reboot.
        foreach (var ev in ordered.Where(x => x.LogName == "Application" &&
                     x.EventId is 1000 or 1002 &&
                     x.Source.Contains("Application", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var app = Regex.Match(ev.Message, @"Faulting application name:\s*([^,\r\n]+)", RegexOptions.IgnoreCase);
            var name = app.Success ? app.Groups[1].Value : ev.Source;
            incidents.Add(new CrashIncident(ev.TimeCreated!.Value,
                ev.EventId == 1002 ? "Application hang" : "Application crash", "Confirmed",
                name, $"APPLICATION INCIDENT\nTime: {ev.TimeCreated:yyyy-MM-dd HH:mm:ss}\nProcess: {name}\n\n{ev.Message}\n\n" +
                "A process crash is not evidence that it caused an earlier system reboot.")
            {
                Evidence = [new IncidentEvidence(ev.TimeCreated.Value,
                    $"{ev.Source} (Event ID {ev.EventId})", Trim(ev.Message))]
            });
        }
        return incidents.OrderByDescending(x => x.Time).ToArray();
    }

    private static bool IsRestartEvidence(CrashEvent e) => e.LogName == "System" &&
        (e.EventId is 41 or 6008 || IsBugcheck(e));
    private static bool IsBugcheck(CrashEvent e) => e.LogName == "System" && e.EventId == 1001 &&
        (e.Source.Contains("BugCheck", StringComparison.OrdinalIgnoreCase) ||
         e.Source.Contains("SystemErrorReporting", StringComparison.OrdinalIgnoreCase));
    private static string? ExtractBugcheck(CrashEvent e)
    {
        var match = Regex.Match(e.Message, @"(?:bugcheck was|bugcheck code|stop code)\s*:?\s*(0x[0-9a-fA-F]+)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
    private static string Trim(string value) => value.Replace("\r", " ").Replace("\n", " ") is var s &&
        s.Length > 240 ? s[..240] + "..." : value.Replace("\r", " ").Replace("\n", " ");

    private sealed record DumpFile(string Path, DateTime Time, long Size);
    private static IReadOnlyList<DumpFile> FindDumps(CancellationToken ct)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var paths = new[] { Path.Combine(root, "Minidump"), Path.Combine(root, "MEMORY.DMP") };
        var result = new List<DumpFile>();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var names = File.Exists(path) ? new[] { path } :
                    Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.dmp").Take(500) :
                    Enumerable.Empty<string>();
                foreach (var name in names)
                {
                    ct.ThrowIfCancellationRequested();
                    var f = new FileInfo(name);
                    result.Add(new DumpFile(f.FullName, f.LastWriteTime, f.Length));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Missing / protected dump folders do not prevent event analysis.
            }
        }
        return result;
    }
}
