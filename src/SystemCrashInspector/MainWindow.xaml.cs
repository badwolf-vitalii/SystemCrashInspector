using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SystemCrashInspector.Models;
using SystemCrashInspector.Services;
using Microsoft.Win32;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace SystemCrashInspector;

public partial class MainWindow : FluentWindow
{
    private IReadOnlyList<CrashEvent> _events = [];
    private readonly ObservableCollection<CrashIncident> _incidents = [];
    private readonly CrashEventReader _reader = new();
    private ICollectionView _eventView;
    private CancellationTokenSource? _refreshCancellation;
    private IReadOnlyList<CrashEvent> _contextEvents = [];

    public MainWindow()
    {
        InitializeComponent();
        _eventView = CollectionViewSource.GetDefaultView(_events);
        _eventView.Filter = MatchesSearch;
        EventsGrid.ItemsSource = _eventView;
        IncidentsList.ItemsSource = _incidents;

        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _refreshCancellation?.Cancel();
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        // The initially checked navigation item can fire before InitializeComponent finishes.
        if (!IsLoaded || IncidentsPage is null || EventsPage is null)
            return;

        IncidentsPage.Visibility = IncidentsNav.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
        EventsPage.Visibility = EventsNav.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _refreshCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Scanning Windows Event Logs...";

        try
        {
            var hours = PeriodPicker.SelectedItem is ComboBoxItem selection &&
                        int.TryParse(selection.Tag?.ToString(), NumberStyles.Integer,
                                     CultureInfo.InvariantCulture, out var value)
                ? value : 1;

            var since = DateTime.Now.AddHours(-hours);
            var result = await Task.Run(() => _reader.Read(since, cancellation.Token),
                                        cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            var incidents = await Task.Run(() => IncidentAnalyzer.Analyze(result.Events, cancellation.Token),
                                           cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_refreshCancellation, cancellation))
                return;

            ApplyScanResults(result, incidents);

            StatusText.Text = $"Scanned {result.Events.Count} events • {incidents.Count} incidents" +
                              (result.Warnings.Count == 0 ? " • Completed" :
                                  $" • Warnings: {string.Join(" | ", result.Warnings)}");
        }
        catch (OperationCanceledException)
        {
            // Another scan replaced this request, or the window was closed.
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_refreshCancellation, cancellation))
                return;

            StatusText.Text = $"Scan failed: {ex.Message}";
            MessageBox.Show(this, $"Unable to read Windows events:\n\n{ex.Message}",
                "Scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
            {
                RefreshButton.IsEnabled = true;
                _refreshCancellation = null;
            }
        }
    }

    // Switch the event table to a newly constructed snapshot, not a deferred refresh of the
    // currently bound collection. WPF DataGrid may query its CollectionView during item changes.
    // Mutating that view under DeferRefresh throws InvalidOperationException at runtime.
    internal void ApplyScanResults(CrashEventReadResult result, IReadOnlyList<CrashIncident> incidents)
    {
        var loadedEvents = result.Events.ToArray();
        var replacementView = CollectionViewSource.GetDefaultView(loadedEvents);
        replacementView.Filter = MatchesSearch;

        var previouslySelected = IncidentsList.SelectedItem as CrashIncident;

        _contextEvents = loadedEvents;
        _events = loadedEvents;
        _eventView = replacementView;
        EventsGrid.ItemsSource = replacementView;

        _incidents.Clear();
        foreach (var incident in incidents)
            _incidents.Add(incident);

        IncidentsCountText.Text = incidents.Count.ToString(CultureInfo.InvariantCulture);
        RestartsCountText.Text = incidents.Count(x => x.IsSystemRestart)
            .ToString(CultureInfo.InvariantCulture);
        EventsCountText.Text = loadedEvents.Length.ToString(CultureInfo.InvariantCulture);
        IncidentListCountText.Text = incidents.Count.ToString(CultureInfo.InvariantCulture);
        NoIncidentsText.Visibility = incidents.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        IncidentsList.SelectedItem = incidents.FirstOrDefault(x =>
            previouslySelected is not null &&
            x.Time == previouslySelected.Time && x.Type == previouslySelected.Type)
            ?? incidents.FirstOrDefault();

        if (incidents.Count == 0)
            SetSelectedIncident(null);
    }

    private void IncidentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SetSelectedIncident(IncidentsList.SelectedItem as CrashIncident);
    }

    private void SetSelectedIncident(CrashIncident? incident)
    {
        if (IncidentDetailsRoot is null || EmptyIncidentRoot is null)
            return;

        IncidentDetailsRoot.DataContext = incident;
        IncidentDetailsRoot.Visibility = incident is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyIncidentRoot.Visibility = incident is null ? Visibility.Visible : Visibility.Collapsed;
        if (incident is not null)
            IncidentTabs.SelectedIndex = 0;
    }

    private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventDetailsText is null)
            return;

        EventDetailsText.Text = EventsGrid.SelectedItem is CrashEvent item
            ? IncidentExplainer.Explain(item, _contextEvents)
            : "Select an event to inspect the original description and XML.";
    }

    private bool MatchesSearch(object item)
    {
        if (item is not CrashEvent ev)
            return false;

        var query = EventSearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query))
            return true;

        return ev.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               ev.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               ev.Message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               ev.LogName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               ev.EventId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void EventSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _eventView?.Refresh();
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (IncidentsList.SelectedItem is not CrashIncident incident)
            return;

        try
        {
            Clipboard.SetText(incident.Details);
            StatusText.Text = "Incident report copied to clipboard.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException
                                   or System.Threading.ThreadStateException)
        {
            StatusText.Text = $"Unable to copy incident report: {ex.Message}";
        }
    }

    private void OpenDump_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string filePath } ||
            !File.Exists(filePath))
        {
            StatusText.Text = "The selected dump file is no longer available.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Unable to open dump location:\n\n{ex.Message}",
                "File unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_events.Count == 0)
        {
            MessageBox.Show(this, "There are no events to export.", "Export CSV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export crash events",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"SystemCrashInspector-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var csv = new StringBuilder("TimeCreated,LogName,EventId,Source,Category,Level,RecordId,Message\r\n");
            foreach (var item in _events)
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
                csv.AppendLine(string.Join(",", values.Select(EscapeCsv)));
            }

            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            StatusText.Text = $"Exported {_events.Count} events.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to export events: {ex.Message}",
                "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string EscapeCsv(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
