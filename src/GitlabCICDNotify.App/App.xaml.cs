using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.WinUI.Notifications;
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
    private ConnectAccountWindow? _connectAccount;
    private bool _exiting;
    private GitLabClient _api = null!;
    private readonly TaskCompletionSource _initialized = new();
    private readonly Dictionary<(Guid Account, long Project, long Pipeline), PipelineWindow> _pipelines = [];
    public Dashboard Dashboard { get; } = new();
    public MonitorService Monitor { get; private set; } = null!;
    public CancellationToken Stopping => _stop.Token;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        _api = new GitLabClient(_http);
        Monitor = new MonitorService(_api, new StateStore(), new NotificationService(), Dashboard);
        ToastNotificationManagerCompat.OnActivated += ToastActivated;
        try
        {
            _tray = new TrayService(ShowStatus, ShowSettings, ShowAbout, () => _ = ExitAsync());
            await Monitor.InitializeAsync(Stopping);
            _initialized.TrySetResult();
            Dashboard.Log("Sledovanie spustené. Okno môžete zatvoriť; aplikácia zostane v systray.");
            _monitorTask = Monitor.RunAsync(Stopping);
            // Optional local diagnostics only; normal startup remains entirely in the tray.
            if (Array.IndexOf(e.Args, "--status") >= 0) ShowStatus();
            if (Array.IndexOf(e.Args, "--settings") >= 0) ShowSettings();
            if (Array.IndexOf(e.Args, "--about") >= 0) ShowAbout();
        }
        catch (Exception ex)
        {
            _initialized.TrySetResult();
            Dashboard.Log(MonitorService.FriendlyError(ex));
            ShowStatus();
            MessageBox.Show(MonitorService.FriendlyError(ex), "GitLab CI/CD Notify", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            await _initialized.Task;
            try
            {
                var arguments = ToastArguments.Parse(e.Argument);
                if (arguments.TryGetValue("action", out var action) && action == "show" &&
                    arguments.TryGetValue("account", out var accountText) && Guid.TryParse(accountText, out var account) &&
                    arguments.TryGetValue("project", out var projectText) && long.TryParse(projectText, out var project) && project > 0 &&
                    arguments.TryGetValue("pipeline", out var pipelineText) && long.TryParse(pipelineText, out var pipeline) && pipeline > 0)
                    ShowPipeline(account, project, pipeline);
            }
            catch (Exception ex) { Dashboard.Log(MonitorService.FriendlyError(ex)); }
        }));
    }

    public void ShowPipeline(Guid accountId, long projectId, long pipelineId)
    {
        if (_exiting) return;
        if (Monitor.FindAccount(accountId) == null)
        {
            Dashboard.Log("Účet z oznámenia už nie je pripojený. Obnovte ho v nastaveniach.");
            ShowSettings();
            return;
        }
        var key = (accountId, projectId, pipelineId);
        if (!_pipelines.TryGetValue(key, out var window))
        {
            window = new PipelineWindow(new PipelineProgress(_api, projectId, pipelineId), () => Monitor.FindAccount(accountId), Stopping);
            window.Closing += HideOnClose;
            _pipelines.Add(key, window);
        }
        ShowWindow(window);
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
    public void ShowConnectAccount()
    {
        if (_exiting) return;
        if (_connectAccount == null) { _connectAccount = new ConnectAccountWindow(this); _connectAccount.Closing += HideOnClose; }
        ShowWindow(_connectAccount);
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
        try
        {
            if (_monitorTask != null) await _monitorTask;
            await Task.WhenAll(_pipelines.Values.Select(window => window.StopAsync()));
        }
        finally { _tray?.Dispose(); _http?.Dispose(); _status?.Close(); _settings?.Close(); _connectAccount?.Close(); _about?.Close(); foreach (var window in _pipelines.Values) window.Close(); Shutdown(); }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _exiting = true; _stop.Cancel(); _tray?.Dispose(); base.OnSessionEnding(e);
    }
    protected override void OnExit(ExitEventArgs e) { ToastNotificationManagerCompat.OnActivated -= ToastActivated; _stop.Cancel(); _tray?.Dispose(); _http?.Dispose(); base.OnExit(e); }
}
