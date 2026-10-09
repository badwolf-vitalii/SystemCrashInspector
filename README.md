# SystemCrashInspector

A Windows desktop diagnostic tool for inspecting application crashes, unexpected restarts, BugChecks, and related event-log evidence.

## Interface

The application uses **WPF UI (Fluent design)** and a custom, dark diagnostic dashboard inspired by the project's social preview:

- Sidebar navigation between **Incidents** and **Event log**
- Summary cards for detected incidents, system restarts, and events scanned
- Selectable incident cards with status, timestamp, and concise description
- Structured incident analysis: **Overview**, **Event timeline**, **Minidumps**, and **Raw report**
- File Explorer shortcut for located system dump files
- Event log with provider/ID/message search and a dedicated detail/Raw XML pane
- Period selector: 1 hour (default), 12 hours, 24 hours, 7 days, 30 days, or 90 days
- CSV export of the loaded events and copyable incident reports
- Fixed-height table rows and virtualized scrolling for long event lists

The dark colors, typography, spacing, and diagnostic cards live in `src/SystemCrashInspector/Themes/DashboardTheme.xaml`.

## Diagnostics

SystemCrashInspector reads selected event IDs in the **System** and **Application** event logs. It correlates nearby records such as **Kernel-Power 41**, **BugCheck 1001**, and **EventLog 6008** into a restart incident.

Potential supporting clues include some WHEA, graphics-driver recovery, storage, and .NET Runtime events. Application crashes and hangs remain separate incidents.

The application lists nearby Windows minidumps and `MEMORY.DMP` by filename, size, and last-modified time. **It does not parse dump contents, load debugging symbols, or identify the faulty driver.** For a BugCheck such as `VIDEO_TDR_FAILURE (0x116)`, the dashboard describes the stop code but does not infer a cause from the dump.

### Event IDs collected

| Log | IDs | Diagnostic use |
| --- | --- | --- |
| System | 41, 6008 | Unclean restart and unexpected shutdown |
| System | 1001 | BugCheck reporting (provider must match) |
| System | 4101 | Display-driver recovery (provider-dependent) |
| System | 17–20 | Potential WHEA indicators (verify provider) |
| System | 7, 11, 15, 51, 55, 129, 153, 157 | Potential storage issues (verify provider) |
| System | 1, 14, 100, 219, 1074, 6005, 6006 | Additional context |
| Application | 1000, 1001, 1002, 1026 | Application failures, reporting, and .NET Runtime events |

Events are **indicators, not proof of root cause**. Correlation is based on time proximity and event sources. The reader is currently capped at 2,000 events, and the timeline does not include every possible Windows event. Missing event-provider message resources or inaccessible logs may produce warnings without losing other readable logs.

## Requirements

- Windows 10 or Windows 11
- .NET 8 Desktop Runtime to run
- Visual Studio 2022 with the **.NET desktop development** workload, or .NET 8 SDK, to build
- Access to the Windows Event Logs; certain protected records may require elevated rights

## Build

Open `src/SystemCrashInspector.sln` in Visual Studio 2022, or run:

```powershell
dotnet restore src/SystemCrashInspector.sln
dotnet build src/SystemCrashInspector.sln --configuration Release
dotnet run --project src/SystemCrashInspector/SystemCrashInspector.csproj
```

The project references **WPF-UI 4.3.0** (Fluent controls and theme resources) and `System.Diagnostics.EventLog`.

### Validation

The Windows GitHub Actions workflow builds the entire solution and runs a small STA smoke test. It verifies WPF resource initialization and checks that the sample `VIDEO_TDR_FAILURE (0x116)` incident remains separate from a subsequent unrelated application crash:

```powershell
dotnet run --project src/SystemCrashInspector.SmokeTests/SystemCrashInspector.SmokeTests.csproj --configuration Release
```

The smoke test does not replace an interactive visual layout check on Windows.

## Privacy

The application reads local event logs and checks local crash dump paths. It does not transmit diagnostic information to external services. CSV exports and copied reports may include user names, file paths, device identifiers, and other sensitive event content.

## Limitations and future work

- Automatic WinDbg-based dump and driver analysis is **not** implemented.
- WER report ingestion and advanced hardware data collection are **not** implemented.
- The preview-inspired dashboard presents only diagnostic information the current analyzer can support. It does not claim to identify faulty hardware automatically.
