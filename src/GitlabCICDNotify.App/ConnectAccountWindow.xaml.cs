using System;
using System.Windows;
using System.Windows.Media;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.App;

public partial class ConnectAccountWindow : Window
{
    private readonly App _app;
    public ConnectAccountWindow(App app)
    {
        InitializeComponent();
        _app = app;
        IsVisibleChanged += (_, _) => { if (!IsVisible) TokenBox.Clear(); };
    }
    private void CreateToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var instance = InstanceUrl.Parse(InstanceBox.Text);
            NotificationService.Open(new Uri(instance, "-/user_settings/personal_access_tokens").AbsoluteUri + "?name=GitLab%20CI%2FCD%20Notify&scopes=read_api");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        Feedback.Foreground = Brushes.SlateGray;
        Feedback.Text = "Overujem prihlásenie…";
        try
        {
            await _app.Monitor.AddAsync(InstanceBox.Text, TokenBox.Password, _app.Stopping);
            TokenBox.Clear();
            Feedback.Foreground = Brushes.SeaGreen;
            Feedback.Text = "Účet bol pripojený. Sledovanie beží na pozadí.";
            await _app.Monitor.PollAsync(_app.Stopping);
        }
        catch (OperationCanceledException) when (_app.Stopping.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { ConnectButton.IsEnabled = true; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ShowError(Exception ex) { Feedback.Foreground = Brushes.IndianRed; Feedback.Text = MonitorService.FriendlyError(ex); }
}
