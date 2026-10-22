using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.App;

public sealed class MonitorService(GitLabClient api, StateStore store, IPipelineNotifier notifications, Dashboard dashboard)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Account> _accounts = [];
    private readonly Dictionary<Guid, (int Failures, DateTimeOffset Retry)> _retries = [];
    private bool _storageHealthy = true;

    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            _accounts.AddRange(await store.LoadAsync(ct));
            RefreshAccountSettings();
            foreach (var account in _accounts) SetState(account, "Pripravené", "Čaká na prvú kontrolu", Brushes.SlateBlue);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _storageHealthy = false;
            dashboard.Log("Účty sa nepodarilo dešifrovať alebo načítať. Pôvodný súbor nebol zmenený.");
            throw new InvalidOperationException("Uložené účty sa nedajú načítať. Súbor accounts.dat zostal zachovaný. Obnovte ho zo zálohy alebo ho premenujte a reštartujte aplikáciu.", ex);
        }
    }

    public async Task AddAsync(string instance, string token, CancellationToken ct)
    {
        if (!_storageHealthy) throw new InvalidOperationException("Najprv obnovte súbor účtov a reštartujte aplikáciu.");
        var normalized = InstanceUrl.Parse(instance).AbsoluteUri;
        var identity = await api.AuthenticateAsync(normalized, token, ct);
        await _gate.WaitAsync(ct);
        try
        {
            var existing = _accounts.FirstOrDefault(a => a.Instance == normalized && a.UserId == identity.Id);
            (string Token, List<string> Emails)? old = existing is null ? null : (existing.Token, existing.Emails);
            var account = existing ?? new Account { Instance = normalized, UserId = identity.Id, Username = identity.Username, Name = identity.Name };
            account.Token = token.Trim();
            account.Emails = identity.Emails;
            if (existing is null) _accounts.Add(account);
            try { await store.SaveAsync(_accounts, ct); }
            catch
            {
                if (existing is null) _accounts.Remove(account);
                else if (old is { } value) { account.Token = value.Token; account.Emails = value.Emails; }
                throw;
            }
            _retries.Remove(account.Id);
            api.InvalidateAccount(account.Id);
            RefreshAccountSettings();
            SetState(account, "Pripojené", "Prihlásenie overené · read_api", Brushes.SeaGreen);
            dashboard.Log($"Pripojený účet @{account.Username} na {account.Instance}");
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var index = _accounts.FindIndex(a => a.Id == id);
            if (index < 0) return;
            var account = _accounts[index];
            _accounts.RemoveAt(index);
            try { await store.SaveAsync(_accounts, ct); }
            catch { _accounts.Insert(index, account); throw; }
            var row = dashboard.Accounts.FirstOrDefault(a => a.Id == id);
            if (row != null) dashboard.Accounts.Remove(row);
            foreach (var p in dashboard.Pipelines.Where(p => p.AccountId == id).ToList()) dashboard.Pipelines.Remove(p);
            _retries.Remove(id);
            api.InvalidateAccount(id);
            RefreshAccountSettings();
            dashboard.Log($"Odpojený účet @{account.Username}. Token v GitLabe nebol zrušený.");
        }
        finally { _gate.Release(); }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do { await PollAsync(ct); } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task SetMonitoringModeAsync(Guid accountId, MonitoringMode mode, CancellationToken ct)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Neplatný režim sledovania.");
        await _gate.WaitAsync(ct);
        try
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account is null || account.MonitoringMode == mode) return;
            var previous = (Mode: account.MonitoringMode, Since: account.MonitoringSince, account.Checkpoint, Active: account.ActivePipelines);
            var now = DateTimeOffset.UtcNow;
            account.MonitoringMode = mode;
            account.MonitoringSince = now;
            account.Checkpoint = now;
            account.ActivePipelines = [];
            try { await store.SaveAsync(_accounts, ct); }
            catch
            {
                account.MonitoringMode = previous.Mode; account.MonitoringSince = previous.Since;
                account.Checkpoint = previous.Checkpoint; account.ActivePipelines = previous.Active;
                throw;
            }
            _retries.Remove(account.Id);
            api.InvalidateAccount(account.Id);
            foreach (var pipeline in dashboard.Pipelines.Where(p => p.AccountId == account.Id).ToList()) dashboard.Pipelines.Remove(pipeline);
            SetState(account, "Pripravené", ModeLabel(mode) + " · čaká na kontrolu", Brushes.SlateBlue);
            RefreshAccountSettings();
            dashboard.Log($"@{account.Username} na {account.Instance}: {ModeLabel(mode)}. Nový režim platí od tejto chvíle pre tento účet.");
        }
        finally { _gate.Release(); }
    }

    private static string ModeLabel(MonitoringMode mode) => mode == MonitoringMode.TriggeringUser ? "Spúšťajúci používateľ" : "Autor commitu";

    private void RefreshAccountSettings()
    {
        dashboard.AccountSettings.Clear();
        foreach (var account in _accounts) dashboard.AccountSettings.Add(new AccountSettingsRow(account.Id, account.Username, account.Instance, account.MonitoringMode));
    }

    public async Task PollAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return;
        try
        {
            foreach (var account in _accounts)
            {
                if (_retries.TryGetValue(account.Id, out var retry) && retry.Retry > DateTimeOffset.UtcNow) continue;
                SetState(account, "Kontrolujem…", ModeLabel(account.MonitoringMode), Brushes.SlateBlue);
                try
                {
                    var scan = await api.ScanAsync(account, ct);
                    foreach (var pipeline in scan.Pipelines)
                    {
                        var row = new PipelineRow(account.Id, pipeline, account.Username, account.Instance);
                        var old = dashboard.Pipelines.FirstOrDefault(p => p.Key == row.Key);
                        if (old != null) dashboard.Pipelines[dashboard.Pipelines.IndexOf(old)] = row;
                        else dashboard.Pipelines.Insert(0, row);
                        while (dashboard.Pipelines.Count > 100) dashboard.Pipelines.RemoveAt(dashboard.Pipelines.Count - 1);
                        if (account.Notified.ContainsKey(pipeline.Key)) continue;
                        notifications.Show(account, pipeline);
                        account.Notified[pipeline.Key] = pipeline.CreatedAt;
                        await store.SaveAsync(_accounts, ct);
                        dashboard.Log($"Nová pipeline #{pipeline.Id} · {pipeline.Project} · @{account.Username}");
                    }
                    var previous = account.Checkpoint;
                    var previousActive = account.ActivePipelines;
                    account.ActivePipelines = previousActive.Concat(scan.Pipelines)
                        .GroupBy(p => p.Key).Select(g => g.Last()).Where(p => p.IsActive).ToList();
                    account.Checkpoint = scan.StartedAt;
                    try { await store.SaveAsync(_accounts, ct); }
                    catch { account.Checkpoint = previous; account.ActivePipelines = previousActive; throw; }
                    _retries.Remove(account.Id);
                    var method = scan.UsesGlobalEndpoint ? "Globálne API" : $"{scan.ProjectCount} projektov";
                    SetState(account, "Sleduje", $"{ModeLabel(account.MonitoringMode)} · {method} · {DateTime.Now:HH:mm:ss}", Brushes.SeaGreen);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    var failures = _retries.GetValueOrDefault(account.Id).Failures + 1;
                    var delay = TimeSpan.FromSeconds(Math.Min(900, 30 * Math.Pow(2, Math.Min(failures - 1, 5))));
                    if (ex is GitLabApiException { RetryAfter: { } requested } && requested > delay) delay = requested;
                    _retries[account.Id] = (failures, DateTimeOffset.UtcNow + delay);
                    var message = FriendlyError(ex);
                    SetState(account, "Vyžaduje pozornosť", message, Brushes.IndianRed);
                    dashboard.Log($"@{account.Username}: {message} Ďalší pokus o {DateTimeOffset.Now.Add(delay):HH:mm:ss}.");
                }
            }
        }
        finally { _gate.Release(); }
    }

    public static string FriendlyError(Exception ex) => ex switch
    {
        GitLabApiException => ex.Message,
        HttpRequestException => "Nepodarilo sa pripojiť ku GitLabu. Skontrolujte sieť a certifikát servera.",
        OperationCanceledException => "Požiadavka trvala príliš dlho.",
        ArgumentException => ex.Message,
        InvalidOperationException => ex.Message,
        _ => "Operácia zlyhala. Overte dostupnosť API, úložiska a Windows notifikácií."
    };

    private void SetState(Account account, string state, string detail, Brush color)
    {
        var row = new AccountRow(account.Id, account.Username, account.Instance, state, detail, color);
        var old = dashboard.Accounts.FirstOrDefault(a => a.Id == account.Id);
        if (old == null) dashboard.Accounts.Add(row);
        else dashboard.Accounts[dashboard.Accounts.IndexOf(old)] = row;
    }
}
