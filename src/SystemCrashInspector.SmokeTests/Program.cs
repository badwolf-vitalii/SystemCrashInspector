using SystemCrashInspector.Models;
using SystemCrashInspector.Services;

namespace SystemCrashInspector.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new global::SystemCrashInspector.App();
            app.InitializeComponent();

            // Building XAML does not guarantee that the merged resource dictionaries load at runtime.
            var window = new global::SystemCrashInspector.MainWindow();
            Check(window.Title == "System Crash Inspector", "Unexpected window title.");
            Check(window.FindName("IncidentsList") is not null, "Incident cards are missing.");
            Check(window.FindName("IncidentTabs") is not null, "Diagnostic tabs are missing.");
            Check(window.FindName("EventsGrid") is not null, "Raw event grid is missing.");
            Check(window.FindName("PeriodPicker") is not null, "Period selector is missing.");
            TestIncidentPresentation(window);
            window.Close();

            Console.WriteLine("WPF startup, event-table refresh, and incident presentation smoke tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestIncidentPresentation(global::SystemCrashInspector.MainWindow window)
    {
        var restartTime = new DateTime(2026, 10, 9, 19, 39, 32);
        var events = new CrashEvent[]
        {
            new(restartTime, "System", 41, "Microsoft-Windows-Kernel-Power",
                "Unexpected power loss or restart", "Critical",
                "The system has rebooted without cleanly shutting down first.", 1, "<Event/>"),
            new(restartTime.AddSeconds(43), "System", 1001,
                "Microsoft-Windows-WER-SystemErrorReporting", "Bug check", "Error",
                "The computer has rebooted from a bugcheck. The bugcheck was: 0x00000116.", 2, "<Event/>"),
            new(restartTime.AddSeconds(44), "System", 6008, "EventLog",
                "Unexpected shutdown", "Error", "The previous shutdown was unexpected.", 3, "<Event/>"),
            new(restartTime.AddMinutes(14), "Application", 1000, "Application Error",
                "Application crash", "Error",
                "Faulting application name: ServiceHub.DataWarehouseHost.exe, version: 1.0",
                4, "<Event/>")
        };

        var incidents = IncidentAnalyzer.Analyze(events, CancellationToken.None);
        Check(incidents.Count == 2, "System and application crashes must remain separate.");
        var restart = incidents.Single(x => x.IsSystemRestart);
        Check(restart.DisplayTitle == "VIDEO_TDR_FAILURE",
            "BugCheck 0x116 should resolve to VIDEO_TDR_FAILURE.");
        Check(restart.BugCheckCode == "0x00000116", "BugCheck code was not preserved.");
        Check(restart.EvidenceCount == 3, "Correlated restart events are missing.");
        Check(restart.StatusLabel == "BUGCHECK CONFIRMED", "Incorrect restart status.");

        var appCrash = incidents.Single(x => !x.IsSystemRestart);
        Check(appCrash.DisplayTitle == "ServiceHub.DataWarehouseHost.exe",
            "Application crash label must identify the process.");
        Check(appCrash.EvidenceCount == 1, "Application incident must retain its evidence.");

        TestEventTableRefresh(window, events, incidents);
    }

    private static void TestEventTableRefresh(
        global::SystemCrashInspector.MainWindow window,
        IReadOnlyList<CrashEvent> events,
        IReadOnlyList<CrashIncident> incidents)
    {
        // Mimic the actual post-scan UI path, including repeated refreshes and a live search.
        // The prior ObservableCollection + DeferRefresh implementation failed here while
        // WPF's DataGrid accessed its bound CollectionView.
        var result = new CrashEventReadResult(events, []);
        window.ApplyScanResults(result, incidents);
        Check(window.EventsGrid.Items.Count == events.Count, "The event table did not load all rows.");
        Check(window.IncidentsCountText.Text == "2", "Incident count is incorrect.");
        Check(window.EventsCountText.Text == "4", "Event count is incorrect.");
        Check(window.IncidentsList.SelectedItem is CrashIncident, "No incident was selected.");

        window.EventSearchBox.Text = "ServiceHub";
        Check(window.EventsGrid.Items.Count == 1, "Event search must filter the visible rows.");

        window.ApplyScanResults(result, incidents);
        Check(window.EventsGrid.Items.Count == 1,
            "A new scan must retain the current search without a deferred-refresh error.");

        window.EventSearchBox.Text = "";
        Check(window.EventsGrid.Items.Count == events.Count,
            "Clearing the search must restore all loaded events.");

        window.ApplyScanResults(new CrashEventReadResult([], []), []);
        Check(window.EventsGrid.Items.Count == 0, "The event table was not cleared.");
        Check(window.IncidentsCountText.Text == "0", "The incident count was not cleared.");
        Check(window.EventsCountText.Text == "0", "The event count was not cleared.");
        Check(window.IncidentsList.SelectedItem is null, "The incident selection was not cleared.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
