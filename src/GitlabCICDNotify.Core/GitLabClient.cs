using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GitlabCICDNotify.Core;

public sealed class GitLabApiException(string message, HttpStatusCode status, TimeSpan? retryAfter = null)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class GitLabClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private async Task<HttpResponseMessage> SendAsync(Account account, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(InstanceUrl.Parse(account.Instance), "api/v4/" + path));
        request.Headers.Add("PRIVATE-TOKEN", account.Token);
        request.Headers.UserAgent.ParseAdd("GitlabCICDNotify/1.0");
        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        response.Dispose();
        throw new GitLabApiException(status switch
        {
            HttpStatusCode.Unauthorized => "Token je neplatný alebo jeho platnosť skončila. Obnovte prihlásenie v nastaveniach.",
            HttpStatusCode.Forbidden => "GitLab odmietol prístup. Overte oprávnenie read_api a prístup k projektu.",
            HttpStatusCode.TooManyRequests => "GitLab obmedzil počet požiadaviek. Sledovanie sa automaticky zopakuje neskôr.",
            _ => $"GitLab API vrátilo HTTP {(int)status}."
        }, status, delay);
    }

    private async Task<T> GetAsync<T>(Account account, string path, CancellationToken ct)
    {
        using var response = await SendAsync(account, path, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new JsonException("Prázdna odpoveď GitLab API.");
    }

    private async Task<List<T>> PagesAsync<T>(Account account, string path, CancellationToken ct)
    {
        List<T> result = [];
        var page = 1;
        while (true)
        {
            using var response = await SendAsync(account, $"{path}{(path.Contains('?') ? '&' : '?')}per_page=100&page={page}", ct);
            var batch = await response.Content.ReadFromJsonAsync<List<T>>(Json, ct) ?? [];
            result.AddRange(batch);
            if (response.Headers.TryGetValues("X-Next-Page", out var values))
            {
                var value = values.FirstOrDefault();
                if (string.IsNullOrEmpty(value)) break;
                if (!int.TryParse(value, out var next) || next <= page) throw new JsonException("Neplatná paginácia GitLab API.");
                page = next;
            }
            else if (batch.Count == 100) page++;
            else break;
        }
        return result;
    }

    public async Task<Identity> AuthenticateAsync(string instance, string token, CancellationToken ct)
    {
        var account = new Account { Instance = InstanceUrl.Parse(instance).AbsoluteUri, Token = token.Trim() };
        if (account.Token.Length == 0) throw new ArgumentException("Vložte osobný prístupový token.");
        var user = await GetAsync<UserDto>(account, "user", ct);
        var emails = await PagesAsync<EmailDto>(account, "user/emails", ct);
        using var permissionCheck = await SendAsync(account, "projects?membership=true&per_page=1", ct);
        var addresses = emails.Where(e => e.ConfirmedAt != null).Select(e => e.Email)
            .Concat(new[] { user.Email, user.CommitEmail }).Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (addresses.Count == 0) throw new InvalidOperationException("GitLab neposkytol žiadnu e-mailovú adresu autora commitov.");
        return new Identity(user.Id, user.Username, user.Name, addresses);
    }

    public async Task<ScanResult> ScanAsync(Account account, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var projects = await PagesAsync<ProjectDto>(account, "projects?membership=true&archived=false&simple=true", ct);
        var since = Uri.EscapeDataString(account.Checkpoint.AddMinutes(-2).ToString("O"));
        List<Pipeline> matches = [];
        foreach (var project in projects)
        {
            foreach (var source in new[] { "push", "merge_request_event" })
            {
                List<PipelineDto> pipelines;
                try
                {
                    pipelines = await PagesAsync<PipelineDto>(account,
                        $"projects/{project.Id}/pipelines?source={source}&updated_after={since}&order_by=id&sort=desc", ct);
                }
                catch (GitLabApiException ex) when (ex.Status == HttpStatusCode.NotFound) { continue; }
                Dictionary<string, string> authors = [];
                foreach (var p in pipelines)
                {
                    if (p.CreatedAt < account.MonitoringSince) continue;
                    if (!authors.TryGetValue(p.Sha, out var email))
                    {
                        var commit = await GetAsync<CommitDto>(account,
                            $"projects/{project.Id}/repository/commits/{Uri.EscapeDataString(p.Sha)}", ct);
                        authors[p.Sha] = email = commit.AuthorEmail;
                    }
                    if (!account.Emails.Contains(email, StringComparer.OrdinalIgnoreCase)) continue;
                    if (!InstanceUrl.IsSafePipelineUrl(p.WebUrl, account.Instance))
                        throw new InvalidOperationException("GitLab vrátil URL pipeline mimo prihlásenej inštancie.");
                    matches.Add(new Pipeline(p.Id, project.Id, project.PathWithNamespace, p.Ref, p.Sha,
                        p.Status, p.WebUrl, source, p.CreatedAt, p.UpdatedAt));
                }
            }
        }
        return new ScanResult(started, matches.OrderBy(p => p.CreatedAt).ToList(), projects.Count);
    }

    private sealed record UserDto(long Id, string Username, string Name, string? Email, string? CommitEmail);
    private sealed record EmailDto(string Email, DateTimeOffset? ConfirmedAt);
    private sealed record ProjectDto(long Id, string PathWithNamespace);
    private sealed record CommitDto(string AuthorEmail);
    private sealed record PipelineDto(long Id, string Ref, string Sha, string Status, string WebUrl,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
