using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GitlabCICDNotify.App;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.Checks;

internal static partial class Program
{
    private static Account ProgressAccount() => new() { Instance = "https://gitlab.example/gitlab/", UserId = 42, Token = "synthetic-progress-token" };

    private static async Task ProgressChecks()
    {
        using var handler = new ProgressGitLab();
        using var http = new HttpClient(handler);
        var api = new GitLabClient(http);
        var account = ProgressAccount();
        var progress = new PipelineProgress(api, 1, 10);
        await progress.UpdateAsync(account, default);
        Check(progress.Jobs.Count == 5 && progress.Jobs.Last().IsBridge, "Progress paginates jobs and includes trigger jobs");
        Check(progress.SelectedJobId == 2 && progress.SelectedTrace?.Text == "Build ✓\n", "Select running job and decode initial UTF-8 log");
        Check(handler.Requests.All(r => r.Url.StartsWith("https://gitlab.example/gitlab/api/v4/")), "Progress keeps installation subpath and authenticates API requests");
        var metadata = handler.MetadataRequests;
        handler.Log += "Tests passed\n";
        await progress.UpdateAsync(account, default);
        Check(handler.MetadataRequests == metadata && handler.Requests.Last().Range == Encoding.UTF8.GetByteCount("Build ✓\n"), "Live refresh requests only new bytes without refetching metadata before interval");
        Check(progress.SelectedTrace?.Text == handler.Log, "Append trace ranges without duplicating output");
        await progress.UpdateAsync(account, default);
        Check(progress.SelectedTrace?.Text == handler.Log, "416 at end of log leaves trace intact");
        progress.SelectedJobId = 1;
        await progress.UpdateAsync(account, default);
        var requests = handler.Requests.Count;
        await progress.UpdateAsync(account, default);
        Check(handler.Requests.Count == requests, "Completed selected job log is fetched once");
        progress.SelectedJobId = 3;
        await progress.UpdateAsync(account, default);
        Check(handler.Requests.Count == requests, "Pending job makes no trace requests");
        progress.SelectedJobId = 5;
        await progress.UpdateAsync(account, default);
        Check(handler.Requests.Count == requests, "Trigger job makes no trace requests");
        progress.SelectedJobId = 2;
        handler.Finished = true;
        handler.Log += "Finished\n";
        progress.Refresh();
        await progress.UpdateAsync(account, default);
        Check(progress.SelectedTrace?.Text.EndsWith("Finished\n") == true && !progress.NeedsMetadata, "Completion fetches final trace after fresh jobs list");
        requests = handler.Requests.Count;
        await progress.UpdateAsync(account, default);
        Check(handler.Requests.Count == requests, "Finished pipeline and final trace stop network polling");
        handler.Finished = false;
        progress.Refresh();
        await progress.UpdateAsync(account, default);
        Check(progress.NeedsMetadata, "Explicit refresh discovers a restarted pipeline");
        handler.Manual = true;
        progress.Refresh();
        await progress.UpdateAsync(account, default);
        Check(progress.NeedsMetadata, "Manual pipeline stays eligible for future server changes");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            progress.Refresh();
            try { await progress.UpdateAsync(account, canceled.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { Check(true, "Cancellation reaches progress HTTP requests"); }
        }

        handler.Manual = false;
        handler.SupportRanges = false;
        var trace = new JobTrace();
        await api.UpdateJobTraceAsync(account, 1, 2, trace, default);
        handler.Log += "More\n";
        await api.UpdateJobTraceAsync(account, 1, 2, trace, default);
        Check(trace.Text == handler.Log, "Server ignoring Range replaces full response instead of appending duplicates");
        await api.UpdateJobTraceAsync(account, 1, 2, trace, default);
        Check(handler.NotModified > 0 && trace.Text == handler.Log, "Unchanged full log uses ETag and handles 304");
        handler.SupportRanges = true;
        handler.Log = "Reset\n";
        await api.UpdateJobTraceAsync(account, 1, 2, trace, default);
        Check(trace.Offset == 0, "416 for a shortened log resets raw byte cursor");
        await api.UpdateJobTraceAsync(account, 1, 2, trace, default);
        Check(trace.Text == handler.Log, "Reload shortened trace from byte zero");
        handler.Log = "ž🙂\n";
        handler.MaxChunk = 1;
        trace = new JobTrace();
        for (var i = 0; i < Encoding.UTF8.GetByteCount(handler.Log); i++) await api.UpdateJobTraceAsync(account, 1, 2, trace, default);
        Check(trace.Text == handler.Log && trace.Offset == Encoding.UTF8.GetByteCount(handler.Log), "Byte cursor and streaming decoder handle split multibyte UTF-8");
        handler.Finished = true;
        handler.Log = "chunked final trace";
        progress = new PipelineProgress(api, 1, 10);
        progress.SelectedJobId = 2;
        for (var i = 0; i < handler.Log.Length; i++) await progress.UpdateAsync(account, default);
        Check(progress.SelectedTrace?.Text == handler.Log, "Completed chunked trace is read to EOF before freezing");
        handler.MaxChunk = int.MaxValue;
        handler.Log = new string('x', JobTrace.MaxCharacters + 50);
        trace = new JobTrace();
        await api.UpdateJobTraceAsync(account, 1, 2, trace, default);
        Check(trace.Text.Length == JobTrace.MaxCharacters && trace.IsTruncated && trace.Offset == handler.Log.Length, "Bound terminal memory while preserving full server byte offset");
        handler.Missing = true;
        progress = new PipelineProgress(api, 1, 10) { SelectedJobId = 2 };
        for (var i = 0; i < 3; i++) await progress.UpdateAsync(account, default);
        requests = handler.Requests.Count;
        await progress.UpdateAsync(account, default);
        Check(handler.Requests.Count == requests, "Missing final trace stops retrying after three attempts");
        handler.Missing = false;
        handler.Failure = HttpStatusCode.TooManyRequests;
        try { await api.UpdateJobTraceAsync(account, 1, 2, new JobTrace(), default); throw new Exception("Expected rate limit"); }
        catch (GitLabApiException ex) { Check(ex.RetryAfter == TimeSpan.FromSeconds(90), "Trace preserves server Retry-After for backoff"); }
        handler.Failure = null;
        handler.InvalidRange = true;
        try { await api.UpdateJobTraceAsync(account, 1, 2, new JobTrace(), default); throw new Exception("Expected invalid range"); }
        catch (InvalidOperationException) { Check(true, "Reject mismatched partial response before corrupting log"); }
        Check(PipelineWindow.FormatTerminal("\u001b[32mOK\u001b[0m\r\n10%\r100%\n") == "OK\n100%\n", "Terminal strips ANSI and applies carriage-return progress updates");
    }

    private static void RenderProgress(string folder)
    {
        using var handler = new ProgressGitLab { Log = "\u001b[32m$ dotnet build --configuration Release\u001b[0m\n  Determining projects to restore...\n  All projects are up-to-date for restore.\n  GitlabCICDNotify.Core -> bin/Release/net10.0/Core.dll\n\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n\n$ dotnet run --project tests/Checks\nPASS: Pipeline authentication\nPASS: Incremental trace\n▶ Running remaining checks...\n" };
        using var http = new HttpClient(handler);
        var progress = new PipelineProgress(new GitLabClient(http), 1, 10);
        progress.UpdateAsync(ProgressAccount(), default).GetAwaiter().GetResult();
        var window = new PipelineWindow(progress, () => null, new CancellationToken(true));
        var terminal = (TextBox)window.FindName("Terminal");
        Check(terminal.IsReadOnly && !terminal.IsUndoEnabled && terminal.FontFamily.Source == "Consolas", "Terminal supports native text selection and copy without editing");
        terminal.Select(0, 10);
        Check(terminal.SelectedText.Length == 10, "Terminal text can be selected");
        Render(window, Path.Combine(folder, "pipeline.png"));
        Check(true, "Render pipeline stage/job tree and terminal");
        using var liveHandler = new ProgressGitLab { BlockTrace = true };
        using var liveHttp = new HttpClient(liveHandler);
        var live = new PipelineProgress(new GitLabClient(liveHttp), 1, 10);
        var liveWindow = new PipelineWindow(live, ProgressAccount, default);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(liveWindow.Dispatcher));
        liveWindow.Show();
        Check(liveHandler.ActiveTraces == 1, "Visible progress window starts selected trace request");
        liveWindow.Hide();
        PumpDispatcher();
        Check(liveHandler.ActiveTraces == 0, "Hiding progress cancels outstanding HTTP request");
        liveWindow.Show();
        PumpDispatcher();
        Check(liveHandler.ActiveTraces == 1 && liveHandler.MaxActiveTraces == 1, "Reopening progress resumes without overlapping sessions");
        var tree = (TreeView)liveWindow.FindName("JobsTree");
        ((TreeViewItem)((TreeViewItem)tree.Items[0]).Items[0]).IsSelected = true;
        Check(live.SelectedJobId == 1, "Selecting a job in stage tree changes terminal target");
        liveWindow.Close();
        PumpDispatcher();
        Check(liveHandler.ActiveTraces == 0, "Closing progress releases outstanding request");
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class ProgressGitLab : HttpMessageHandler
    {
        public string Log { get; set; } = "Build ✓\n";
        public bool Finished { get; set; }
        public bool Manual { get; set; }
        public bool SupportRanges { get; set; } = true;
        public bool Missing { get; set; }
        public bool BlockTrace { get; set; }
        public int ActiveTraces { get; private set; }
        public int MaxActiveTraces { get; private set; }
        public bool InvalidRange { get; set; }
        public int MaxChunk { get; set; } = int.MaxValue;
        public int NotModified { get; private set; }
        public HttpStatusCode? Failure { get; set; }
        public int MetadataRequests { get; private set; }
        public List<(string Url, long? Range)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Headers.GetValues("PRIVATE-TOKEN").Single() != ProgressAccount().Token) throw new Exception("Missing authentication");
            var uri = request.RequestUri!;
            var offset = request.Headers.Range?.Ranges.Single().From;
            Requests.Add((uri.AbsoluteUri, offset));
            if (Failure is { } failure)
            {
                var error = new HttpResponseMessage(failure);
                error.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
                return Task.FromResult(error);
            }
            HttpResponseMessage response;
            if (uri.AbsolutePath.EndsWith("/trace"))
            {
                if (BlockTrace) return BlockAsync(cancellationToken);
                if (Missing) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var bytes = Encoding.UTF8.GetBytes(Log);
                var etag = new EntityTagHeaderValue("\"" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + "\"");
                if (request.Headers.IfNoneMatch.Any(e => e.ToString() == etag.ToString()))
                {
                    NotModified++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
                }
                if (SupportRanges && offset >= bytes.Length)
                {
                    response = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(bytes.Length);
                    return Task.FromResult(response);
                }
                var from = SupportRanges ? (int)(offset ?? 0) : 0;
                var count = SupportRanges ? Math.Min(bytes.Length - from, MaxChunk) : bytes.Length;
                response = new HttpResponseMessage(SupportRanges ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes, from, count) };
                if (SupportRanges) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(InvalidRange ? from + 1 : from, from + count - 1, bytes.Length);
                response.Headers.ETag = etag;
            }
            else
            {
                MetadataRequests++;
                object data;
                if (uri.AbsolutePath.EndsWith("/jobs"))
                    data = uri.Query.Contains("page=2") ? new[] { Job(3, "deploy-production", "deploy", Finished ? "skipped" : "pending"), Job(4, "lint", "test", Finished ? "success" : "created") }
                        : new[] { Job(1, "restore", "build", "success"), Job(2, "build-and-test", "test", Finished ? "success" : "running") };
                else if (uri.AbsolutePath.EndsWith("/bridges")) data = new[] { Job(5, "release-downstream", "deploy", Finished ? "success" : "pending") };
                else data = new { id = 10, project_id = 1, @ref = "feature/pipeline-progress", sha = "a7b648debaae", status = Manual ? "manual" : Finished ? "success" : "running", source = "push", web_url = "https://gitlab.example/gitlab/team/deployment-service/-/pipelines/10", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
                response.Headers.TryAddWithoutValidation("X-Next-Page", uri.AbsolutePath.EndsWith("/jobs") && !uri.Query.Contains("page=2") ? "2" : "");
            }
            return Task.FromResult(response);
        }
        private async Task<HttpResponseMessage> BlockAsync(CancellationToken ct)
        {
            ActiveTraces++;
            MaxActiveTraces = Math.Max(MaxActiveTraces, ActiveTraces);
            try { await Task.Delay(Timeout.Infinite, ct); throw new Exception("Unreachable"); }
            finally { ActiveTraces--; }
        }
        private static object Job(long id, string name, string stage, string status) => new { id, name, stage, status, started_at = status is "running" or "success" ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1) : null };
    }
}
