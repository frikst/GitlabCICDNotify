using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace GitlabCICDNotify.Core;

public sealed record PipelineJob(long Id, string Name, string Stage, string Status, DateTimeOffset? StartedAt, bool IsBridge = false)
{
    public bool IsFinished => Status is "success" or "failed" or "canceled" or "skipped";
    public bool CanHaveTrace => !IsBridge && (StartedAt != null || Status is "running" or "canceling" or "success" or "failed");
}

public sealed partial class GitLabClient
{
    public async Task<Pipeline> GetPipelineAsync(Account account, long projectId, long pipelineId, CancellationToken ct)
    {
        var dto = await GetAsync<PipelineDto>(account, $"projects/{projectId}/pipelines/{pipelineId}", ct);
        return ToPipeline(account, dto, projectId, null);
    }

    public async Task<IReadOnlyList<PipelineJob>> GetPipelineJobsAsync(Account account, long projectId, long pipelineId, CancellationToken ct)
    {
        var path = $"projects/{projectId}/pipelines/{pipelineId}";
        var jobs = await PagesAsync<JobDto>(account, path + "/jobs?include_retried=false", ct);
        var bridges = await PagesAsync<JobDto>(account, path + "/bridges", ct);
        return jobs.Select(j => new PipelineJob(j.Id, j.Name, j.Stage, j.Status, j.StartedAt))
            .Concat(bridges.Select(j => new PipelineJob(j.Id, j.Name, j.Stage, j.Status, j.StartedAt, true)))
            .OrderBy(j => j.Id).ToList();
    }

    public async Task<bool> UpdateJobTraceAsync(Account account, long projectId, long jobId, JobTrace trace, CancellationToken ct)
    {
        using var response = await SendAsync(account, $"projects/{projectId}/jobs/{jobId}/trace", ct, cache: false,
            configure: request =>
            {
                request.Headers.Range = new RangeHeaderValue(trace.Offset, null);
                if (trace.ETag != null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(trace.ETag));
            }, trace: true);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (trace.ETag == null) throw new InvalidOperationException("GitLab vrátil 304 bez uloženého logu.");
            return true;
        }
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (response.Content.Headers.ContentRange?.Length == trace.Offset) return true;
            trace.Reset();
            return false;
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var partial = response.StatusCode == HttpStatusCode.PartialContent;
        if (partial)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.Unit != "bytes" || range.From != trace.Offset || range.To - range.From + 1 != bytes.LongLength)
                throw new InvalidOperationException("GitLab vrátil neplatný rozsah logu.");
        }
        trace.Apply(bytes, partial, response.Headers.ETag?.ToString());
        trace.HasMore = partial && response.Content.Headers.ContentRange?.Length > trace.Offset;
        if (trace.HasMore) trace.ClearValidator();
        return true;
    }

    private sealed record JobDto(long Id, string Name, string Stage, string Status, DateTimeOffset? StartedAt);
}

// Only a bounded terminal tail is retained, but Offset always counts raw server bytes.
public sealed class JobTrace
{
    public const int MaxCharacters = 2 * 1024 * 1024;
    private Decoder _decoder = Encoding.UTF8.GetDecoder();
    public long Offset { get; private set; }
    public string? ETag { get; private set; }
    public string Text { get; private set; } = "";
    public bool IsTruncated { get; private set; }
    public bool HasMore { get; internal set; }
    internal void ClearValidator() => ETag = null;
    public void Reset()
    {
        Offset = 0;
        ETag = null;
        Text = "";
        IsTruncated = false;
        HasMore = false;
        _decoder.Reset();
    }
    internal void Apply(byte[] bytes, bool append, string? etag)
    {
        if (!append) Reset();
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        var count = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
        Text += new string(chars, 0, count);
        Offset += bytes.LongLength;
        ETag = etag;
        if (Text.Length > MaxCharacters)
        {
            Text = Text[^MaxCharacters..];
            IsTruncated = true;
        }
    }
}

// One window drives one session sequentially; terminal jobs are fetched once at completion.
public sealed class PipelineProgress(GitLabClient api, long projectId, long pipelineId)
{
    private readonly Dictionary<long, JobTrace> _traces = [];
    private readonly HashSet<long> _completed = [];
    private readonly Dictionary<long, int> _missing = [];
    private DateTimeOffset _nextMetadata;
    public event Action? MetadataUpdated;
    public Pipeline? Pipeline { get; private set; }
    public IReadOnlyList<PipelineJob> Jobs { get; private set; } = [];
    public long? SelectedJobId { get; set; }
    public JobTrace? SelectedTrace => SelectedJobId is { } id ? _traces.GetValueOrDefault(id) : null;
    public bool NeedsTrace => Jobs.Any(j => j.Id == SelectedJobId && j.CanHaveTrace && !_completed.Contains(j.Id));
    public bool NeedsMetadata => Pipeline == null || Pipeline.IsActive || Jobs.Any(j => !j.IsFinished);

    public void Refresh()
    {
        _nextMetadata = default;
        foreach (var id in _missing.Keys) _completed.Remove(id);
        _missing.Clear();
    }

    public async Task UpdateAsync(Account account, CancellationToken ct)
    {
        if (_nextMetadata == default || NeedsMetadata && DateTimeOffset.UtcNow >= _nextMetadata)
        {
            // Read status first, then jobs: a completion cannot freeze a stale jobs list.
            var pipeline = await api.GetPipelineAsync(account, projectId, pipelineId, ct);
            var jobs = await api.GetPipelineJobsAsync(account, projectId, pipelineId, ct);
            foreach (var job in jobs)
                if (!job.IsFinished) { _completed.Remove(job.Id); _missing.Remove(job.Id); }
            Pipeline = pipeline;
            Jobs = jobs;
            _nextMetadata = DateTimeOffset.UtcNow.AddSeconds(Pipeline.Status == "manual" ? 30 : 6);
            if (!Jobs.Any(j => j.Id == SelectedJobId))
                SelectedJobId = Jobs.FirstOrDefault(j => j.Status == "running")?.Id ?? Jobs.FirstOrDefault()?.Id;
            MetadataUpdated?.Invoke();
        }
        var selected = Jobs.FirstOrDefault(j => j.Id == SelectedJobId);
        if (selected == null || !selected.CanHaveTrace || _completed.Contains(selected.Id)) return;
        if (!_traces.TryGetValue(selected.Id, out var trace))
        {
            // Bound inactive job caches; evicted jobs can be loaded again on selection.
            if (_traces.Count >= 8)
            {
                var evicted = _traces.Keys.First();
                _traces.Remove(evicted);
                _completed.Remove(evicted);
            }
            _traces[selected.Id] = trace = new JobTrace();
        }
        var available = await api.UpdateJobTraceAsync(account, projectId, selected.Id, trace, ct);
        if (selected.IsFinished && !trace.HasMore && (available || (_missing[selected.Id] = _missing.GetValueOrDefault(selected.Id) + 1) >= 3))
            _completed.Add(selected.Id);
    }
}
