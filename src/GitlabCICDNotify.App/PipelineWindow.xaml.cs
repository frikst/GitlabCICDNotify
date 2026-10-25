using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.App;

public partial class PipelineWindow : Window
{
    private readonly PipelineProgress _progress;
    private readonly Func<Account?> _resolveAccount;
    private readonly CancellationToken _stopping;
    private CancellationTokenSource? _visible;
    private Task _loop = Task.CompletedTask;
    private IReadOnlyList<PipelineJob> _renderedJobs = [];
    private long? _renderedJob;
    private string? _renderedText;
    private bool _buildingTree;

    public PipelineWindow(PipelineProgress progress, Func<Account?> resolveAccount, CancellationToken stopping)
    {
        InitializeComponent();
        _progress = progress;
        _resolveAccount = resolveAccount;
        _stopping = stopping;
        _progress.MetadataUpdated += UpdateView;
        Closed += (_, _) => _progress.MetadataUpdated -= UpdateView;
        UpdateView();
        IsVisibleChanged += VisibilityChanged;
    }

    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _visible?.Cancel();
        if (IsVisible) StartMonitoring();
    }

    private async void StartMonitoring()
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        _visible = source;
        try
        {
            await _loop;
            if (source.IsCancellationRequested) return;
            _progress.Refresh();
            _loop = RunAsync(source.Token);
            await _loop;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_visible, source)) _visible = null;
            source.Dispose();
        }
    }

    public Task StopAsync()
    {
        _visible?.Cancel();
        return _loop;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(2);
            try
            {
                var account = _resolveAccount();
                if (account == null)
                {
                    ConnectionState.Text = "Účet už nie je pripojený. Obnovte ho v nastaveniach a znovu otvorte okno.";
                    return;
                }
                await _progress.UpdateAsync(account, ct);
                ct.ThrowIfCancellationRequested();
                UpdateView();
                failures = 0;
                ConnectionState.Text = _progress.NeedsMetadata
                    ? "● Priebežné sledovanie · stav približne každých 6 s (manuálna pipeline 30 s) · výstup každé 2 s"
                    : _progress.NeedsTrace ? "Načítavam záver výstupu dokončeného jobu…"
                    : "✓ Pipeline dokončená · automatické obnovovanie zastavené · nové spustenie zistíte cez Obnoviť";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                failures++;
                delay = TimeSpan.FromSeconds(Math.Min(120, 5 * Math.Pow(2, Math.Min(failures - 1, 5))));
                if (ex is GitLabApiException { RetryAfter: { } retry } && retry > delay) delay = retry;
                ConnectionState.Text = MonitorService.FriendlyError(ex) + $" Ďalší pokus: {DateTimeOffset.Now.Add(delay):HH:mm:ss}.";
                if (ex is GitLabApiException { Status: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound })
                {
                    ConnectionState.Text = MonitorService.FriendlyError(ex) + " Po vyriešení použite Obnoviť.";
                    return;
                }
            }
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }

    private void UpdateView()
    {
        var pipeline = _progress.Pipeline;
        if (pipeline != null)
        {
            PipelineTitle.Text = $"{pipeline.Project} · #{pipeline.Id}";
            PipelineDetail.Text = $"{pipeline.Ref} · {pipeline.Sha[..Math.Min(8, pipeline.Sha.Length)]} · {StatusLabel(pipeline.Status)}";
            Title = $"#{pipeline.Id} · {pipeline.Project} · Priebeh pipeline";
        }
        if (!_renderedJobs.SequenceEqual(_progress.Jobs))
        {
            _buildingTree = true;
            var collapsed = JobsTree.Items.OfType<TreeViewItem>().Where(i => !i.IsExpanded).Select(i => i.Tag as string).ToHashSet();
            JobsTree.Items.Clear();
            foreach (var stage in _progress.Jobs.GroupBy(j => j.Stage))
            {
                var parent = new TreeViewItem { Header = $"{stage.Key}  ·  {stage.Count()}", Tag = stage.Key, FontWeight = FontWeights.SemiBold, IsExpanded = !collapsed.Contains(stage.Key) };
                foreach (var job in stage)
                {
                    var label = new StackPanel { Orientation = Orientation.Horizontal };
                    label.Children.Add(new TextBlock { Text = StatusIcon(job.Status), Foreground = StatusColor(job.Status), Width = 24, FontSize = 16 });
                    label.Children.Add(new TextBlock { Text = job.Name, VerticalAlignment = VerticalAlignment.Center });
                    parent.Items.Add(new TreeViewItem { Header = label, Tag = job.Id, ToolTip = $"{job.Name} · {StatusLabel(job.Status)}" + (job.IsBridge ? " · nadväzujúca pipeline" : ""), FontWeight = FontWeights.Normal, IsSelected = job.Id == _progress.SelectedJobId });
                }
                JobsTree.Items.Add(parent);
            }
            _renderedJobs = _progress.Jobs.ToArray();
            _buildingTree = false;
        }
        var selected = _progress.Jobs.FirstOrDefault(j => j.Id == _progress.SelectedJobId);
        JobTitle.Text = selected == null ? "Vyberte job" : $"{selected.Name} · {StatusLabel(selected.Status)}";
        var trace = _progress.SelectedTrace;
        var raw = trace?.Text ?? "";
        if (_renderedJob != selected?.Id || _renderedText != raw)
        {
            var changedJob = _renderedJob != selected?.Id;
            var text = FormatTerminal(raw);
            var selection = Terminal.SelectionStart;
            var length = Terminal.SelectionLength;
            var vertical = Terminal.VerticalOffset;
            var horizontal = Terminal.HorizontalOffset;
            if (!changedJob && text.StartsWith(Terminal.Text, StringComparison.Ordinal)) Terminal.AppendText(text[Terminal.Text.Length..]);
            else Terminal.Text = text;
            if (!changedJob) Terminal.Select(Math.Min(selection, Terminal.Text.Length), Math.Min(length, Math.Max(0, Terminal.Text.Length - selection)));
            if (FollowOutput.IsChecked == true) Terminal.ScrollToEnd();
            else { Terminal.ScrollToVerticalOffset(vertical); Terminal.ScrollToHorizontalOffset(horizontal); }
            _renderedJob = selected?.Id;
            _renderedText = raw;
        }
        TraceHint.Text = trace?.IsTruncated == true ? "Zobrazený koniec logu (limit 2 mil. znakov)" :
            selected?.IsBridge == true ? "Trigger job nemá vlastný terminálový výstup" :
            raw.Length == 0 ? "Job zatiaľ nemá dostupný výstup" : "Ctrl+C kopírovať · Ctrl+A označiť všetko";
    }

    private void Job_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_buildingTree || e.NewValue is not TreeViewItem { Tag: long id }) return;
        _progress.SelectedJobId = id;
        UpdateView();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _progress.Refresh();
        if (_loop.IsCompleted && IsVisible) StartMonitoring();
    }
    private void Follow_Checked(object sender, RoutedEventArgs e) => Terminal?.ScrollToEnd();
    private void Browser_Click(object sender, RoutedEventArgs e)
    {
        try { if (_progress.Pipeline is { } pipeline) NotificationService.Open(pipeline.Url); }
        catch (Exception ex) { ConnectionState.Text = MonitorService.FriendlyError(ex); }
    }

    public static string FormatTerminal(string text)
    {
        // Render plain terminal output: remove ANSI/OSC commands, GitLab fold markers,
        // and apply carriage-return progress updates without executing escape sequences.
        text = Regex.Replace(text, @"\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B\[[0-?]*[ -/]*[@-~]|section_(?:start|end):\d+:[^\r\n]*", "");
        return string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(line => line[(line.LastIndexOf('\r') + 1)..]));
    }
    public static string StatusLabel(string status) => status switch
    {
        "success" => "Úspešný",
        "failed" => "Zlyhal",
        "running" => "Beží",
        "pending" => "Čaká",
        "created" => "Vytvorený",
        "preparing" => "Pripravuje sa",
        "waiting_for_resource" => "Čaká na zdroj",
        "manual" => "Manuálny krok",
        "scheduled" => "Naplánovaný",
        "canceled" => "Zrušený",
        "canceling" => "Ruší sa",
        "skipped" => "Preskočený",
        _ => status
    };
    public static string StatusIcon(string status) => status switch
    {
        "success" => "✓",
        "failed" => "✕",
        "running" => "▶",
        "manual" => "▷",
        "canceled" or "canceling" => "⊘",
        "skipped" => "↷",
        _ => "◷"
    };
    private static Brush StatusColor(string status) => status switch
    {
        "success" => Brushes.SeaGreen,
        "failed" => Brushes.IndianRed,
        "running" => Brushes.SlateBlue,
        _ => Brushes.SlateGray
    };
}
