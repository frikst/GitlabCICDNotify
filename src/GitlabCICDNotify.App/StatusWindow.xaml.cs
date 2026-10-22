using System;
using System.Windows;
using System.Windows.Controls;

namespace GitlabCICDNotify.App;

public partial class StatusWindow : Window
{
    private readonly App _app;
    public StatusWindow(App app) { InitializeComponent(); _app = app; DataContext = app.Dashboard; }
    private void Settings_Click(object sender, RoutedEventArgs e) => _app.ShowSettings();
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try { await _app.Monitor.PollAsync(_app.Stopping); }
        catch (OperationCanceledException) { }
        finally { button.IsEnabled = true; }
    }
    private void Pipeline_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not PipelineRow row) return;
        try { NotificationService.Open(row.Pipeline.Url); }
        catch (Exception ex) { _app.Dashboard.Log(MonitorService.FriendlyError(ex)); }
    }
}
