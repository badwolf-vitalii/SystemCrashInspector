# SystemCrashInspector

A Windows desktop app for investigating crashes, application hangs, and unexpected system restarts.

## Features

- WPF interface targeting .NET 8 on Windows
- Reads relevant events from **System** and **Application** event logs
- Shows the event timestamp, ID, source, category, severity, and full description
- Provides event-specific diagnostic explanations, raw event XML, and a +/- 5 minute timeline of other collected events
- Looks for nearby Windows minidumps, MEMORY.DMP, and per-user crash dumps (file metadata only)
- Includes selected WHEA, graphics-driver, storage, and .NET exception indicators
- Filters to the last 1, 12, or 24 hours, or 7, 30, or 90 days
- Exports the currently loaded events to CSV
- Loads event data without blocking the UI

## Events included

| Log | Event IDs | Meaning |
| --- | --- | --- |
| System | 4101 | Display driver timeout / recovery (provider-dependent) |\n| System | 17-20 | Hardware errors (only when emitted by WHEA providers) |\n| System | 7, 11, 15, 51, 55, 129, 153, 157 | Potential storage errors (source-dependent) |\n| Application | 1026 | .NET exception reporting (source-dependent) |\n| System | 41 | Kernel-Power: system restarted without a clean shutdown |
| System | 6008 | Unexpected shutdown |
| System | 1001 | Bug check |
| System | 1074 | Planned restart/shutdown |
| System | 6005, 6006 | Event Log service start/stop |
| Application | 1000 | Application error |
| Application | 1001 | Windows Error Reporting |
| Application | 1002 | Application hang |

Missing Windows event-provider message resources do not stop event loading; the app shows a fallback description. Unavailable logs are reported in the status bar while other logs are still processed.\n\nEvents provide evidence to investigate; **they do not establish the root cause by themselves**. The reader currently caps results at 2,000 events.

## Requirements

- Windows 10 or 11
- .NET 8 SDK to build and .NET 8 Desktop Runtime to run
- Access to Windows Event Logs (administrator rights may be needed for some systems)

## Build

```powershell
dotnet restore src/SystemCrashInspector/SystemCrashInspector.csproj
dotnet build src/SystemCrashInspector/SystemCrashInspector.csproj -c Release
dotnet run --project src/SystemCrashInspector/SystemCrashInspector.csproj
```

Open `src/SystemCrashInspector.sln` in Visual Studio 2022 with the .NET desktop development workload.

## Privacy

Events are read locally. Nothing is transmitted to external services. CSV exports can contain paths, usernames, and other sensitive event details, so review them before sharing.

## Scope

This stage provides evidence-based interpretations and local event correlation, not a confirmed diagnosis. Dump metadata is listed, but dump contents are not analyzed. Dump-file parsing, WER report correlations, more sophisticated filtering, and root-cause grouping are potential next steps.
