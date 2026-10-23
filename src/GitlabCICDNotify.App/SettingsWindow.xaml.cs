using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GitlabCICDNotify.App;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    public SettingsWindow(App app) { InitializeComponent(); _app = app; DataContext = app.Dashboard; }
    private void OpenConnect_Click(object sender, RoutedEventArgs e) => _app.ShowConnectAccount();
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try { await _app.Monitor.RemoveAsync((Guid)button.Tag, _app.Stopping); }
        catch (OperationCanceledException) when (_app.Stopping.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { button.IsEnabled = true; }
    }
    private async void SaveMode_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.Tag is not AccountSettingsRow row) return;
        button.IsEnabled = false;
        try
        {
            await _app.Monitor.SetMonitoringModeAsync(row.Id, row.Mode, _app.Stopping);
            Feedback.Foreground = Brushes.SeaGreen;
            Feedback.Text = "Režim sledovania účtu bol uložený.";
            await _app.Monitor.PollAsync(_app.Stopping);
        }
        catch (OperationCanceledException) when (_app.Stopping.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { button.IsEnabled = true; }
    }
    private void ShowError(Exception ex) { Feedback.Foreground = Brushes.IndianRed; Feedback.Text = MonitorService.FriendlyError(ex); }
}
