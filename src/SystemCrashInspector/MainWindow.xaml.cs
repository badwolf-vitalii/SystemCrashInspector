using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SystemCrashInspector.Models;
using SystemCrashInspector.Services;

namespace SystemCrashInspector;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CrashEvent> _events = [];
    private readonly CrashEventReader _reader = new();
    private CancellationTokenSource? _refreshCancellation;

    public MainWindow()
    {
        InitializeComponent();
        EventsGrid.ItemsSource = _events;
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _refreshCancellation?.Cancel();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _refreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Reading Windows Event Logs...";

        try
        {
            var days = int.Parse(((ComboBoxItem)PeriodPicker.SelectedItem).Tag.ToString()!,
                CultureInfo.InvariantCulture);
            var since = DateTime.Now.AddDays(-days);
            var events = await Task.Run(() => _reader.Read(since, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;

            _events.Clear();
            foreach (var item in events)
                _events.Add(item);

            StatusText.Text = $"Loaded {events.Count} events. Event IDs are indicators, not proof of root cause.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to read Windows Event Logs.";
            MessageBox.Show(this, $"Unable to read events: {ex.Message}\n\nTry running as administrator.",
                "Event log error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
            {
                RefreshButton.IsEnabled = true;
                _refreshCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailsText.Text = EventsGrid.SelectedItem is CrashEvent item
            ? $"Date: {item.TimeCreated:yyyy-MM-dd HH:mm:ss}\nLog: {item.LogName}\n" +
              $"Event ID: {item.EventId}\nSource: {item.Source}\nLevel: {item.Level}\n" +
              $"Record ID: {item.RecordId}\nCategory: {item.Category}\n\n{item.Message}"
            : string.Empty;
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
