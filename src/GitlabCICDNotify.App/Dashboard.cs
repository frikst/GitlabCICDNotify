using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.App;

public sealed record AccountRow(Guid Id, string Username, string Instance, string State, string Detail, Brush Color);
public sealed class AccountSettingsRow(Guid id, string username, string instance, MonitoringMode mode)
{
    public Guid Id { get; } = id;
    public string Username { get; } = username;
    public string Instance { get; } = instance;
    public MonitoringMode Mode { get; set; } = mode;
}
public sealed record PipelineRow(Guid AccountId, Pipeline Pipeline, string Username, string Instance)
{
    public string Key => $"{AccountId}:{Pipeline.Key}";
    public string Title => $"{Pipeline.Project}  ·  #{Pipeline.Id}";
    public string Detail => $"{Pipeline.Ref}  ·  {Pipeline.Sha[..Math.Min(8, Pipeline.Sha.Length)]}  ·  @{Username}";
    public string Time => Pipeline.CreatedAt.ToLocalTime().ToString("dd.MM. HH:mm:ss");
    public string Status => Pipeline.Status switch
    {
        "success" => "Úspešná",
        "failed" => "Zlyhala",
        "running" => "Beží",
        "pending" => "Čaká",
        "created" => "Vytvorená",
        "canceled" => "Zrušená",
        "skipped" => "Preskočená",
        "manual" => "Manuálny krok",
        "unavailable" => "Nedostupná",
        _ => Pipeline.Status
    };
    public Brush Color => Pipeline.Status switch
    {
        "success" => Brushes.SeaGreen,
        "failed" => Brushes.IndianRed,
        "running" or "pending" or "created" or "preparing" => Brushes.SlateBlue,
        _ => Brushes.SlateGray
    };
}

public sealed class Dashboard : INotifyPropertyChanged
{
    public ObservableCollection<AccountRow> Accounts { get; } = [];
    public ObservableCollection<AccountSettingsRow> AccountSettings { get; } = [];
    public ObservableCollection<PipelineRow> Pipelines { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public string Summary => Accounts.Count == 0 ? "Pridajte prvý GitLab účet v nastaveniach." :
        $"{Accounts.Count} {(Accounts.Count == 1 ? "účet" : Accounts.Count < 5 ? "účty" : "účtov")} · kontrola každých 30 sekúnd";
    public int ActiveCount => Pipelines.Count(p => p.Pipeline.Status is "running" or "pending" or "created" or "preparing" or "waiting_for_resource");
    public int SuccessCount => Pipelines.Count(p => p.Pipeline.Status == "success");
    public int FailedCount => Pipelines.Count(p => p.Pipeline.Status == "failed");
    public bool IsEmpty => Pipelines.Count == 0;
    public bool HasNoAccounts => Accounts.Count == 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Dashboard()
    {
        Accounts.CollectionChanged += (_, _) => { Changed(nameof(Summary)); Changed(nameof(HasNoAccounts)); };
        Pipelines.CollectionChanged += (_, _) => { Changed(nameof(ActiveCount)); Changed(nameof(SuccessCount)); Changed(nameof(FailedCount)); Changed(nameof(IsEmpty)); };
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public void Log(string message)
    {
        Logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
    }
}
