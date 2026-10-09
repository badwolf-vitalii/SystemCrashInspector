# SystemCrashInspector

A Windows desktop app for investigating crashes, application hangs, and unexpected system restarts.

## Features

- WPF interface targeting .NET 8 on Windows
- Reads relevant events from **System** and **Application** event logs
- Shows the event timestamp, ID, source, category, severity, and full description
- Filters to the last 7, 30, or 90 days
- Exports the currently loaded events to CSV
- Loads event data without blocking the UI

## Events included

| Log | Event IDs | Meaning |
| --- | --- | --- |
| System | 41 | Kernel-Power: system restarted without a clean shutdown |
| System | 6008 | Unexpected shutdown |
| System | 1001 | Bug check |
| System | 1074 | Planned restart/shutdown |
| System | 6005, 6006 | Event Log service start/stop |
| Application | 1000 | Application error |
| Application | 1001 | Windows Error Reporting |
| Application | 1002 | Application hang |

Events provide evidence to investigate; **they do not establish the root cause by themselves**. The reader currently caps results at 2,000 events.

## Requirements

- Windows 10 or 11
- .NET 8 SDK to build and .NET 8 Desktop Runtime to run
- Access to Windows Event Logs (administrator rights may be needed for some systems)

## Build

```powershell
dotnet restore SystemCrashInspector/SystemCrashInspector.csproj
dotnet build SystemCrashInspector/SystemCrashInspector.csproj -c Release
dotnet run --project SystemCrashInspector/SystemCrashInspector.csproj
```

Open the project in Visual Studio 2022 with the .NET desktop development workload.

## Privacy

Events are read locally. Nothing is transmitted to external services. CSV exports can contain paths, usernames, and other sensitive event details, so review them before sharing.

## Scope

This first iteration is an event-log inspector, not a minidump analyzer. Dump-file parsing, WER report correlations, more sophisticated filtering, and root-cause grouping are potential next steps.
