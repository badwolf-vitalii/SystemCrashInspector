using System.Globalization;
using System.IO;
using System.Text;
using SystemCrashInspector.Models;

namespace SystemCrashInspector.Services;

/// <summary>
/// Produces Excel-friendly CSV while keeping fields quoted and preserving non-ASCII characters.
/// The sep= directive is Excel-specific, not part of the standard CSV format.
/// </summary>
internal static class ExcelCsvExporter
{
    internal const string Header = "TimeCreated,LogName,EventId,Source,Category,Level,RecordId,Message";
    private const string NewLine = "\r\n";

    internal static string Build(IReadOnlyList<CrashEvent> events)
    {
        // Excel uses the user's regional list separator when opening a .csv directly.
        // This directive forces a comma separator even on installations expecting semicolons.
        var csv = new StringBuilder("sep=,\r\n");
        csv.Append(Header).Append(NewLine);

        foreach (var item in events)
        {
            var values = new[]
            {
                item.TimeCreated?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                item.LogName,
                item.EventId.ToString(CultureInfo.InvariantCulture),
                item.Source,
                item.Category,
                item.Level,
                item.RecordId?.ToString(CultureInfo.InvariantCulture) ?? "",
                item.Message
            };

            csv.Append(string.Join(",", values.Select(EscapeField))).Append(NewLine);
        }

        return csv.ToString();
    }

    internal static void WriteFile(string path, IReadOnlyList<CrashEvent> events)
    {
        // Excel detects UTF-8 consistently when the BOM is present.
        File.WriteAllText(path, Build(events), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string EscapeField(string value)
    {
        // An event is one physical CSV row. Preserve words and punctuation while flattening
        // multi-line Windows event descriptions (also avoids oversized Excel row heights).
        var singleLine = value.Replace("\r\n", " ", StringComparison.Ordinal)
                              .Replace('\r', ' ')
                              .Replace('\n', ' ');
        return "\"" + singleLine.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
