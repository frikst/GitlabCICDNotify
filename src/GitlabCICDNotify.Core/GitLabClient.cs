using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GitlabCICDNotify.Core;

public sealed class GitLabApiException(string message, HttpStatusCode status, TimeSpan? retryAfter = null)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed partial class GitLabClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly ConditionalGetCache _responses = new();
    private readonly HashSet<string> _globalUnsupported = [];
    private readonly Dictionary<Guid, (DateTimeOffset Expires, List<ProjectDto> Projects)> _projects = [];

    private async Task<HttpResponseMessage> SendAsync(Account account, string path, CancellationToken ct, bool cache = true, Action<HttpRequestMessage>? configure = null, bool trace = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(InstanceUrl.Parse(account.Instance), "api/v4/" + path));
        request.Headers.Add("PRIVATE-TOKEN", account.Token);
        request.Headers.UserAgent.ParseAdd("GitlabCICDNotify/1.0");
        configure?.Invoke(request);
        var response = cache ? await _responses.SendAsync(http, request, account.Id, account.UserId, ct) : await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || trace && response.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.NotFound or HttpStatusCode.RequestedRangeNotSatisfiable) return response;
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

    private async Task<T> GetAsync<T>(Account account, string path, CancellationToken ct, bool cache = true)
    {
        using var response = await SendAsync(account, path, ct, cache);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new JsonException("Prázdna odpoveď GitLab API.");
    }

    private async Task<List<T>> PagesAsync<T>(Account account, string path, CancellationToken ct, bool cache = true)
    {
        List<T> result = [];
        var page = 1;
        while (true)
        {
            using var response = await SendAsync(account, $"{path}{(path.Contains('?') ? '&' : '?')}per_page=100&page={page}", ct, cache);
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
        var user = await GetAsync<UserDto>(account, "user", ct, cache: false);
        var emails = await PagesAsync<EmailDto>(account, "user/emails", ct, cache: false);
        using var permissionCheck = await SendAsync(account, "projects?membership=true&per_page=1", ct, cache: false);
        var addresses = emails.Where(e => e.ConfirmedAt != null).Select(e => e.Email)
            .Concat(new[] { user.Email, user.CommitEmail }).Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Identity(user.Id, user.Username, user.Name, addresses);
    }

    public async Task<ScanResult> ScanAsync(Account account, CancellationToken ct)
    {
        if (account.MonitoringMode == MonitoringMode.TriggeringUser && !_globalUnsupported.Contains(account.Instance))
        {
            var started = DateTimeOffset.UtcNow;
            List<PipelineDto>? pipelines = null;
            try { pipelines = await GlobalPagesAsync(account, ct); }
            catch (GitLabApiException ex) when (ex.Status == HttpStatusCode.NotFound) { _globalUnsupported.Add(account.Instance); }
            if (pipelines != null) return await ScanGlobalAsync(account, pipelines, started, ct);
        }
        return await ScanProjectsAsync(account, ct);
    }

    private async Task<List<PipelineDto>> GlobalPagesAsync(Account account, CancellationToken ct)
    {
        var since = Uri.EscapeDataString(account.Checkpoint.AddMinutes(-2).ToString("O"));
        string? path = $"pipelines?created_after={since}&per_page=100";
        var apiRoot = new Uri(InstanceUrl.Parse(account.Instance), "api/v4/");
        HashSet<string> visited = [];
        List<PipelineDto> result = [];
        while (path != null)
        {
            if (!visited.Add(path)) throw new JsonException("Opakujúca sa paginácia GitLab API.");
            using var response = await SendAsync(account, path, ct);
            result.AddRange(await response.Content.ReadFromJsonAsync<List<PipelineDto>>(Json, ct) ?? []);
            path = null;
            if (!response.Headers.TryGetValues("Link", out var links)) continue;
            foreach (var link in links)
            {
                var match = Regex.Match(link, "<([^>]+)>\\s*;\\s*rel=\"next\"");
                if (!match.Success) continue;
                var next = new Uri(apiRoot, match.Groups[1].Value);
                // Pagination is server-controlled: never forward a token to another origin or route.
                if (!InstanceUrl.IsSafePipelineUrl(next.AbsoluteUri, account.Instance) ||
                    next.AbsolutePath != apiRoot.AbsolutePath + "pipelines" || next.Fragment.Length > 0)
                    throw new InvalidOperationException("GitLab vrátil neplatný odkaz na ďalšiu stránku pipelines.");
                path = "pipelines" + next.Query;
                break;
            }
        }
        return result;
    }

    private async Task<ScanResult> ScanGlobalAsync(Account account, List<PipelineDto> pipelines, DateTimeOffset started, CancellationToken ct)
    {
        Dictionary<string, Pipeline> result = [];
        foreach (var p in pipelines)
        {
            if (p.CreatedAt < account.MonitoringSince) continue;
            var pipeline = ToPipeline(account, p, p.ProjectId, p.Project?.PathWithNamespace);
            result[pipeline.Key] = pipeline;
        }
        foreach (var active in account.ActivePipelines)
        {
            if (result.ContainsKey(active.Key)) continue;
            try
            {
                var p = await GetAsync<PipelineDto>(account, $"projects/{active.ProjectId}/pipelines/{active.Id}", ct);
                result[active.Key] = ToPipeline(account, p, active.ProjectId, active.Project);
            }
            catch (GitLabApiException ex) when (ex.Status == HttpStatusCode.NotFound)
            {
                result[active.Key] = active with { Status = "unavailable" };
            }
        }
        return new ScanResult(started, result.Values.OrderBy(p => p.CreatedAt).ToList(), result.Values.Select(p => p.ProjectId).Distinct().Count(), true);
    }

    private async Task<List<ProjectDto>> ProjectsAsync(Account account, CancellationToken ct)
    {
        if (_projects.TryGetValue(account.Id, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return cached.Projects;
        var projects = await PagesAsync<ProjectDto>(account, "projects?membership=true&archived=false&simple=true", ct);
        _projects[account.Id] = (DateTimeOffset.UtcNow.AddMinutes(10), projects);
        return projects;
    }

    public void InvalidateAccount(Guid id)
    {
        _projects.Remove(id);
        _responses.InvalidateAccount(id);
    }

    private static Pipeline ToPipeline(Account account, PipelineDto p, long projectId, string? project)
    {
        if (!InstanceUrl.IsSafePipelineUrl(p.WebUrl, account.Instance))
            throw new InvalidOperationException("GitLab vrátil URL pipeline mimo prihlásenej inštancie.");
        if (projectId <= 0) throw new JsonException("GitLab neposkytol ID projektu pipeline.");
        if (string.IsNullOrWhiteSpace(project))
        {
            var relative = InstanceUrl.Parse(account.Instance).MakeRelativeUri(new Uri(p.WebUrl)).GetComponents(UriComponents.SerializationInfoString, UriFormat.Unescaped);
            var marker = relative.LastIndexOf("/-/pipelines/", StringComparison.Ordinal);
            if (marker < 0) marker = relative.LastIndexOf("/pipelines/", StringComparison.Ordinal);
            project = marker > 0 ? relative[..marker] : $"Projekt {projectId}";
        }
        return new Pipeline(p.Id, projectId, project, p.Ref, p.Sha, p.Status, p.WebUrl, p.Source, p.CreatedAt, p.UpdatedAt);
    }

    private async Task<ScanResult> ScanProjectsAsync(Account account, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        if (account.MonitoringMode == MonitoringMode.CommitAuthor && account.Emails.Count == 0)
            throw new InvalidOperationException("GitLab neposkytol e-mail autora commitov. Obnovte prihlásenie alebo sledujte spúšťajúceho používateľa.");
        var projects = await ProjectsAsync(account, ct);
        var since = Uri.EscapeDataString(account.Checkpoint.AddMinutes(-2).ToString("O"));
        List<Pipeline> matches = [];
        foreach (var project in projects)
        {
            var filters = account.MonitoringMode == MonitoringMode.CommitAuthor
                ? new[] { "source=push", "source=merge_request_event" }
                : new[] { "username=" + Uri.EscapeDataString(account.Username) };
            foreach (var filter in filters)
            {
                List<PipelineDto> pipelines;
                try
                {
                    pipelines = await PagesAsync<PipelineDto>(account,
                        $"projects/{project.Id}/pipelines?{filter}&updated_after={since}&order_by=id&sort=desc", ct);
                }
                catch (GitLabApiException ex) when (ex.Status == HttpStatusCode.NotFound) { continue; }
                Dictionary<string, string> authors = [];
                foreach (var p in pipelines)
                {
                    if (p.CreatedAt < account.MonitoringSince) continue;
                    if (account.MonitoringMode == MonitoringMode.CommitAuthor)
                    {
                        if (!authors.TryGetValue(p.Sha, out var email))
                        {
                            var commit = await GetAsync<CommitDto>(account,
                                $"projects/{project.Id}/repository/commits/{Uri.EscapeDataString(p.Sha)}", ct);
                            authors[p.Sha] = email = commit.AuthorEmail;
                        }
                        if (!account.Emails.Contains(email, StringComparer.OrdinalIgnoreCase)) continue;
                    }
                    matches.Add(ToPipeline(account, p, project.Id, project.PathWithNamespace));
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
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long ProjectId, string Source, ProjectDto? Project);
}
