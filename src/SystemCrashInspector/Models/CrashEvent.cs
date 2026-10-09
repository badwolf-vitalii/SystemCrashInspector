namespace SystemCrashInspector.Models;

public sealed record CrashEvent(
    DateTime? TimeCreated,
    string LogName,
    int EventId,
    string Source,
    string Category,
    string Level,
    string Message,
    long? RecordId);
