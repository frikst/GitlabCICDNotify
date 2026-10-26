using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GitlabCICDNotify.App;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly StartupService _startup;
    private bool _startupEnabled;
    private readonly ThemeService _themes;
    private bool _refreshingTheme;

    public SettingsWindow(App app, StartupService? startup = null, ThemeService? themes = null)
    {
        InitializeComponent();
        _app = app;
        _startup = startup ?? app.WindowsStartup;
        _themes = themes ?? app.Themes;
        DataContext = app.Dashboard;
        IsVisibleChanged += (_, _) => { if (IsVisible) { RefreshStartup(); RefreshTheme(); } };
    }

    private void RefreshTheme()
    {
        _refreshingTheme = true;
        try { ThemeChoice.SelectedValue = _themes.Preference; }
        finally { _refreshingTheme = false; }
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingTheme || _themes == null || ThemeChoice.SelectedValue is not AppTheme theme) return;
        try
        {
            _themes.SetPreference(theme);
            Feedback.Foreground = ThemeService.Brush("SuccessBrush");
            Feedback.Text = "Téma aplikácie bola uložená.";
        }
        catch
        {
            RefreshTheme();
            Feedback.Foreground = ThemeService.Brush("ErrorBrush");
            Feedback.Text = "Nepodarilo sa uložiť tému aplikácie. Overte oprávnenie na zápis do registra.";
        }
    }

    private void RefreshStartup()
    {
        try
        {
            _startupEnabled = _startup.IsEnabled;
            StartWithWindows.IsChecked = _startupEnabled;
            StartWithWindows.IsEnabled = true;
        }
        catch
        {
            StartWithWindows.IsEnabled = false;
            Feedback.Foreground = ThemeService.Brush("ErrorBrush");
            Feedback.Text = "Nepodarilo sa načítať nastavenie spúšťania pri prihlásení do Windows. Otvorte nastavenia znova.";
        }
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = StartWithWindows.IsChecked == true;
            _startup.SetEnabled(enabled);
            _startupEnabled = enabled;
            Feedback.Foreground = ThemeService.Brush("SuccessBrush");
            Feedback.Text = enabled ? "Spúšťanie pri prihlásení do Windows bolo zapnuté." : "Spúšťanie pri prihlásení do Windows bolo vypnuté.";
        }
        catch
        {
            StartWithWindows.IsChecked = _startupEnabled;
            Feedback.Foreground = ThemeService.Brush("ErrorBrush");
            Feedback.Text = "Nepodarilo sa zmeniť spúšťanie pri prihlásení do Windows. Overte oprávnenie na zápis do registra.";
        }
    }
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
            Feedback.Foreground = ThemeService.Brush("SuccessBrush");
            Feedback.Text = "Režim sledovania účtu bol uložený.";
            await _app.Monitor.PollAsync(_app.Stopping);
        }
        catch (OperationCanceledException) when (_app.Stopping.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { button.IsEnabled = true; }
    }
    private void ShowError(Exception ex) { Feedback.Foreground = ThemeService.Brush("ErrorBrush"); Feedback.Text = MonitorService.FriendlyError(ex); }
}
