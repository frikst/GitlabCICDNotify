namespace GitlabCICDNotify.Core;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Instance { get; set; } = "";
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public List<string> Emails { get; set; } = [];
    public DateTimeOffset MonitoringSince { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Checkpoint { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, DateTimeOffset> Notified { get; set; } = [];
}

public sealed record Pipeline(long Id, long ProjectId, string Project, string Ref, string Sha,
    string Status, string Url, string Source, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public string Key => $"{ProjectId}:{Id}";
    public bool IsCommitTriggered => Source is "push" or "merge_request_event";
}

public sealed record Identity(long Id, string Username, string Name, List<string> Emails);
public sealed record ScanResult(DateTimeOffset StartedAt, IReadOnlyList<Pipeline> Pipelines, int ProjectCount);

public static class InstanceUrl
{
    public static Uri Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Zadajte HTTPS adresu GitLabu, napr. https://gitlab.com (bez query a prihlasovacích údajov).");
        return new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
    }

    public static bool IsSafePipelineUrl(string value, string instance) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        string.IsNullOrEmpty(uri.UserInfo) && Uri.Compare(uri, Parse(instance), UriComponents.SchemeAndServer,
            UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
