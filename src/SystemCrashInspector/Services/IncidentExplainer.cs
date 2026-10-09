using System.Text;
using System.Text.RegularExpressions;
using SystemCrashInspector.Models;

namespace SystemCrashInspector.Services;

/// <summary>
/// Evidence-based interpretations, not definitive root-cause diagnoses.
/// </summary>
public static class IncidentExplainer
{
    public static string Explain(CrashEvent selected, IReadOnlyList<CrashEvent> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DIAGNOSTIC INTERPRETATION (not a confirmed root cause)");
        sb.AppendLine(Interpret(selected));
        sb.AppendLine();
        sb.AppendLine("EVENT INFORMATION");
        sb.AppendLine($"Time: {selected.TimeCreated:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Log: {selected.LogName}; ID: {selected.EventId}; Provider: {selected.Source}");
        sb.AppendLine($"Level: {selected.Level}; Record ID: {selected.RecordId}");
        sb.AppendLine();
        sb.AppendLine("RELATED EVENTS (+/- 2 minutes)");
        if (selected.TimeCreated is { } timestamp)
        {
            var related = events.Where(e => !ReferenceEquals(e, selected) &&
                                           e.TimeCreated is { } t &&
                                           Math.Abs((t - timestamp).TotalMinutes) <= 2)
                .OrderBy(e => e.TimeCreated).Take(30).ToArray();
            if (related.Length == 0)
                sb.AppendLine("No other collected events in this time window.");
            foreach (var e in related)
                sb.AppendLine($"{e.TimeCreated:HH:mm:ss}  [{e.LogName}/{e.EventId}] {e.Source} - {e.Level}");
        }
        else
        {
            sb.AppendLine("Event timestamp is unavailable.");
        }

        sb.AppendLine();
        sb.AppendLine("EVENT DESCRIPTION");
        sb.AppendLine(selected.Message);
        sb.AppendLine();
        sb.AppendLine("RAW EVENT XML");
        sb.AppendLine(selected.RawXml);
        return sb.ToString();
    }

    private static string Interpret(CrashEvent e)
    {
        if (e.LogName == "System" && e.EventId == 41)
            return "Kernel-Power 41 records an unclean restart. It does not prove a power-supply fault. " +
                   "Check BugCheck 1001, dump files, hardware/WHEA errors, and events preceding the reboot.";
        if (e.LogName == "System" && e.EventId == 6008)
            return "Event 6008 reports that the previous shutdown was unexpected. It is generally a consequence, not the original cause.";
        if (e.LogName == "System" && e.EventId == 1001)
            return "A system bugcheck (BSOD) was reported. The stop code and a matching memory dump are important. " +
                   "A driver or kernel diagnosis requires analyzing the dump.";
        if (e.LogName == "Application" && e.EventId == 1000 && e.Source.Contains("Application Error", StringComparison.OrdinalIgnoreCase))
        {
            var code = Regex.Match(e.Message, @"Exception code:\s*(0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase);
            var explanation = code.Success ? code.Groups[1].Value.ToUpperInvariant() switch
            {
                "0XC0000005" => "Access violation: invalid memory access. Inspect the faulting module and a crash dump.",
                "0XC0000409" => "Fast-fail / stack buffer overrun family. Examine the dump to determine the actual failure.",
                "0XE0434352" => "CLR exception. Search for a nearby .NET Runtime event 1026 for the managed exception and stack trace.",
                "0XC0000374" => "Heap corruption reported by Windows. A dump is needed to find the corrupting code.",
                "0XC0000006" => "In-page I/O error. Check storage, disk errors, and file availability.",
                _ => "Exception code " + code.Groups[1].Value + " requires further investigation."
            } : "Check the faulting application, module, and exception code in the description.";
            return "Application Error 1000 indicates that a process crashed. " + explanation +
                   " WER reports and process dumps may provide more evidence.";
        }
        if (e.LogName == "Application" && e.EventId == 1002)
            return "The application stopped responding. This is not necessarily a crash; inspect hang dumps and blocked threads.";
        if (e.LogName == "Application" && e.EventId == 1001)
            return "Windows Error Reporting recorded a problem report. Its report files may include fault signatures and dump references.";
        if (e.EventId == 1074)
            return "A shutdown or reboot was requested by a process or user. Inspect the event details for the initiating process.";
        return "This event alone cannot establish a crash cause. Compare its timestamp and provider with other system evidence.";
    }
}
