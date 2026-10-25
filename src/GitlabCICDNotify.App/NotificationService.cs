using System;
using System.Diagnostics;
using CommunityToolkit.WinUI.Notifications;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.App;

public interface IPipelineNotifier { void Show(Account account, Pipeline pipeline); }

public sealed class NotificationService : IPipelineNotifier
{
    public void Show(Account account, Pipeline pipeline)
    {
        Build(account, pipeline).Show(toast => { toast.Tag = pipeline.Id.ToString(); toast.Group = account.Id.ToString("N")[..16]; });
    }

    public static ToastContentBuilder Build(Account account, Pipeline pipeline)
    {
        if (!InstanceUrl.IsSafePipelineUrl(pipeline.Url, account.Instance)) throw new ArgumentException("Neplatná URL pipeline.");
        return new ToastContentBuilder()
            .AddText("Spustená GitLab pipeline")
            .AddText($"{pipeline.Project} · {pipeline.Ref}")
            .AddText($"@{account.Username} · #{pipeline.Id} · {pipeline.Status}")
            .SetProtocolActivation(new Uri(pipeline.Url))
            .AddButton(new ToastButton().SetContent("Zobraziť").AddArgument("action", "show").AddArgument("account", account.Id.ToString("D")).AddArgument("project", pipeline.ProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture)).AddArgument("pipeline", pipeline.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .AddButton(new ToastButton().SetContent("Otvoriť pipeline").SetProtocolActivation(new Uri(pipeline.Url)));
    }

    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Neplatná HTTPS adresa.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
