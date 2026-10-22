using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _stop = new();
    private HttpClient? _http;
    private TrayService? _tray;
    private Task? _monitorTask;
    private StatusWindow? _status;
    private SettingsWindow? _settings;
    private AboutWindow? _about;
    private bool _exiting;
    public Dashboard Dashboard { get; } = new();
    public MonitorService Monitor { get; private set; } = null!;
    public CancellationToken Stopping => _stop.Token;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        Monitor = new MonitorService(new GitLabClient(_http), new StateStore(), new NotificationService(), Dashboard);
        try
        {
            _tray = new TrayService(ShowStatus, ShowSettings, ShowAbout, () => _ = ExitAsync());
            await Monitor.InitializeAsync(Stopping);
            Dashboard.Log("Sledovanie spustené. Okno môžete zatvoriť; aplikácia zostane v systray.");
            _monitorTask = Monitor.RunAsync(Stopping);
            // Optional local diagnostics only; normal startup remains entirely in the tray.
            if (Array.IndexOf(e.Args, "--status") >= 0) ShowStatus();
            if (Array.IndexOf(e.Args, "--settings") >= 0) ShowSettings();
            if (Array.IndexOf(e.Args, "--about") >= 0) ShowAbout();
        }
        catch (Exception ex)
        {
            Dashboard.Log(MonitorService.FriendlyError(ex));
            ShowStatus();
            MessageBox.Show(MonitorService.FriendlyError(ex), "GitLab CI/CD Notify", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowStatus()
    {
        if (_exiting) return;
        if (_status == null) { _status = new StatusWindow(this); _status.Closing += HideOnClose; }
        ShowWindow(_status);
    }
    public void ShowSettings()
    {
        if (_exiting) return;
        if (_settings == null) { _settings = new SettingsWindow(this); _settings.Closing += HideOnClose; }
        ShowWindow(_settings);
    }
    public void ShowAbout()
    {
        if (_exiting) return;
        if (_about == null) { _about = new AboutWindow(); _about.Closing += HideOnClose; }
        ShowWindow(_about);
    }
    private static void ShowWindow(Window window) { window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
    private void HideOnClose(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        ((Window)sender!).Hide();
    }
    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _stop.Cancel();
        try { if (_monitorTask != null) await _monitorTask; }
        finally { _tray?.Dispose(); _http?.Dispose(); _status?.Close(); _settings?.Close(); _about?.Close(); Shutdown(); }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _exiting = true; _stop.Cancel(); _tray?.Dispose(); base.OnSessionEnding(e);
    }
    protected override void OnExit(ExitEventArgs e) { _stop.Cancel(); _tray?.Dispose(); _http?.Dispose(); base.OnExit(e); }
}
