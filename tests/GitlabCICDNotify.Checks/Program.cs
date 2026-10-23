using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitlabCICDNotify.App;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.Checks;

internal static class Program
{
    private static int _passed;
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            CheckUrls();
            CheckNotification();
            ApiChecks().GetAwaiter().GetResult();
            TriggeringUserChecks().GetAwaiter().GetResult();
            MonitorChecks().GetAwaiter().GetResult();
            if (args.Contains("--render") || args.Contains("--desktop")) RenderWindows(args.Contains("--desktop"));
            Console.WriteLine($"PASS: {_passed} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        _passed++;
        Console.WriteLine("PASS: " + label);
    }

    private static void CheckUrls()
    {
        Check(InstanceUrl.Parse(" https://GITLAB.example.com/gitlab ").AbsoluteUri == "https://gitlab.example.com/gitlab/", "Normalize host and preserve subpath");
        foreach (var value in new[] { "http://gitlab.com", "file:///C:/temp", "https://user:password@gitlab.com", "https://gitlab.com?token=secret", "https://gitlab.com/#fragment" })
        {
            try { InstanceUrl.Parse(value); throw new InvalidOperationException("Accepted invalid URL"); }
            catch (ArgumentException) { Check(true, "Reject unsafe instance URL"); }
        }
        Check(!InstanceUrl.IsSafePipelineUrl("https://attacker.example/pipeline", "https://gitlab.com/"), "Reject external pipeline URL");
        Check(!InstanceUrl.IsSafePipelineUrl("https://gitlab.com:444/pipeline", "https://gitlab.com/"), "Reject different pipeline origin port");
    }

    private static async Task ApiChecks()
    {
        using var handler = new FakeGitLab();
        using var http = new HttpClient(handler);
        var api = new GitLabClient(http);
        var identity = await api.AuthenticateAsync("https://gitlab.example/gitlab", "test-token", default);
        Check(identity.Id == 42 && identity.Emails.Count == 2, "Authenticate and include only confirmed secondary emails");
        Check(handler.Requests.All(r => r.Contains("/gitlab/api/v4/")), "API respects GitLab installation subpath");
        var account = new Account
        {
            Instance = "https://gitlab.example/gitlab/", Token = "test-token", Emails = identity.Emails,
            MonitoringSince = handler.Now.AddMinutes(-5), Checkpoint = handler.Now.AddMinutes(-5), MonitoringMode = MonitoringMode.CommitAuthor
        };
        handler.ProjectsPaginated = true;
        var scan = await api.ScanAsync(account, default);
        Check(scan.ProjectCount == 2, "Follow project pagination");
        Check(scan.Pipelines.Count == 2, "Match author email and exclude another author's commit and old history");
        Check(scan.Pipelines.Any(p => p.Source == "push") && scan.Pipelines.Any(p => p.Source == "merge_request_event"), "Discover push and merge request pipelines");
        Check(handler.Requests.Where(r => r.Contains("/pipelines?")).All(r => r.Contains("updated_after=")), "Use overlapping incremental pipeline scans");
        handler.ReturnMaliciousUrl = true;
        try { await api.ScanAsync(account, default); throw new Exception("Accepted external pipeline URL"); }
        catch (InvalidOperationException) { Check(true, "API rejects untrusted notification destinations"); }
        handler.ReturnMaliciousUrl = false;
        handler.Failure = HttpStatusCode.Unauthorized;
        try { await api.ScanAsync(account, default); throw new Exception("Expected authentication failure"); }
        catch (GitLabApiException ex) { Check(ex.Status == HttpStatusCode.Unauthorized && !ex.Message.Contains("test-token"), "Invalid token produces safe actionable error"); }
        handler.Failure = HttpStatusCode.TooManyRequests;
        try { await api.ScanAsync(account, default); throw new Exception("Expected rate limit"); }
        catch (GitLabApiException ex) { Check(ex.RetryAfter == TimeSpan.FromSeconds(120), "Respect GitLab Retry-After"); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await api.ScanAsync(account, canceled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Check(true, "Cancel requests during shutdown"); }
    }

    private static async Task TriggeringUserChecks()
    {
        using var handler = new FakeGitLab { GlobalPagination = true };
        using var http = new HttpClient(handler);
        var api = new GitLabClient(http);
        var account = new Account
        {
            Instance = "https://gitlab.example/gitlab/", Token = "test-token", Username = "user42", UserId = 42,
            MonitoringSince = handler.Now.AddMinutes(-5), Checkpoint = handler.Now.AddMinutes(-5)
        };
        Check(account.MonitoringMode == MonitoringMode.TriggeringUser &&
            JsonSerializer.Deserialize<Account>("{}")!.MonitoringMode == MonitoringMode.TriggeringUser, "New and legacy accounts default to triggering user");
        var scan = await api.ScanAsync(account, default);
        Check(scan.UsesGlobalEndpoint && scan.Pipelines.Count == 2 && scan.Pipelines.Select(p => p.ProjectId).Distinct().Count() == 2, "Global API follows cursor Link pagination across projects");
        Check(handler.Requests.Count == 2 && handler.Requests.All(r => !r.Contains("&page=") && !r.Contains("?page=")), "Global discovery does not use offset pages or enumerate projects and commits");
        Check(scan.Pipelines.Any(p => p.Sha == "other" && p.Source == "web"), "Triggering user mode includes another author's commit and manually started pipelines");
        Check(scan.Pipelines.Any(p => p.Project == "team/project") && scan.Pipelines.Any(p => p.Project == "team/second"), "Derive project name from pipeline URL on GitLab 19.3 without embedded project data");
        handler.GlobalPagination = false;
        handler.MaliciousNextLink = true;
        try { await api.ScanAsync(account, default); throw new Exception("Expected invalid pagination rejection"); }
        catch (InvalidOperationException) { Check(handler.Requests.All(r => !r.StartsWith("https://attacker.example")), "Do not forward authentication to an external pagination link"); }
        handler.MaliciousNextLink = false;
        handler.GlobalFailure = HttpStatusCode.Forbidden;
        var before = handler.Requests.Count;
        try { await api.ScanAsync(account, default); throw new Exception("Expected permission failure"); }
        catch (GitLabApiException ex) { Check(ex.Status == HttpStatusCode.Forbidden && handler.Requests.Count == before + 1, "Permission failure is not mistaken for missing global API"); }
        handler.GlobalFailure = HttpStatusCode.NotFound;
        handler.Requests.Clear();
        scan = await api.ScanAsync(account, default);
        await api.ScanAsync(account, default);
        Check(!scan.UsesGlobalEndpoint && handler.Requests.Count(r => new Uri(r).AbsolutePath == "/gitlab/api/v4/pipelines") == 1, "Old GitLab probes unsupported global API only once per instance");
        Check(handler.Requests.Count(r => new Uri(r).AbsolutePath.EndsWith("/projects")) == 1, "Fallback caches project list across polling cycles");
        Check(handler.Requests.Where(r => r.Contains("/projects/1/pipelines?")).All(r => r.Contains("username=user42")) &&
            !handler.Requests.Any(r => r.Contains("/repository/commits/")), "Fallback filters triggering username server-side without fetching commits");
        account.MonitoringMode = MonitoringMode.CommitAuthor;
        try { await api.ScanAsync(account, default); throw new Exception("Expected missing author email error"); }
        catch (InvalidOperationException) { Check(true, "Only author mode requires commit email addresses"); }
    }

    private static void CheckNotification()
    {
        var account = new Account { Instance = "https://gitlab.example/", Username = "developer", Token = "secret-not-in-toast" };
        var pipeline = new Pipeline(10, 1, "group/project", "main", "abc", "running", "https://gitlab.example/group/project/-/pipelines/10", "push", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var content = NotificationService.Build(account, pipeline).GetToastContent().GetContent();
        var xml = System.Xml.Linq.XDocument.Parse(content);
        var action = xml.Descendants("action").Single();
        Check(action.Attribute("activationType")?.Value == "protocol" && action.Attribute("arguments")?.Value == pipeline.Url,
            "Windows notification action opens the exact pipeline URL via protocol activation");
        Check(xml.Root?.Attribute("activationType")?.Value == "protocol" && !content.Contains(account.Token),
            "Clicking the toast body also opens pipeline and never includes credentials");
    }

    private static async Task MonitorChecks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "GitlabNotifyChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "accounts.dat");
        try
        {
            using var handler = new FakeGitLab();
            using var http = new HttpClient(handler);
            var store = new StateStore(path);
            var dashboard = new Dashboard();
            var notifier = new FakeNotifier();
            var monitor = new MonitorService(new GitLabClient(http), store, notifier, dashboard);
            await monitor.InitializeAsync(default);
            await monitor.AddAsync("https://gitlab.example/gitlab", "test-token", default);
            await monitor.AddAsync("https://gitlab.example/gitlab/", "new-token", default);
            Check(dashboard.Accounts.Count == 1, "Same instance and user update existing authentication");
            var stored = await store.LoadAsync(default);
            Check(stored.Single().Token == "new-token", "Updated token survives DPAPI round trip");
            Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)).Contains("new-token"), "Persisted credentials are encrypted");
            handler.UserId = 43;
            await monitor.AddAsync("https://gitlab.example/gitlab", "second-user-token", default);
            Check(dashboard.Accounts.Count == 2, "Allow another user on the same GitLab instance");
            Check(dashboard.AccountSettings.Count == 2 && dashboard.AccountSettings.All(a => a.Mode == MonitoringMode.TriggeringUser), "Each user on the same instance has its own default monitoring setting");
            // Monitor starts at sign-in; fixture pipelines represent a later push.
            handler.Now = DateTimeOffset.UtcNow.AddMinutes(1);
            await monitor.PollAsync(default);
            Check(notifier.Count == 4, "Each account receives its own matching pipeline notifications");
            Check(dashboard.Pipelines.Count == 4 && dashboard.ActiveCount == 4, "Dashboard represents real pipeline status");
            await monitor.PollAsync(default);
            Check(notifier.Count == 4, "Overlapping scans do not repeat notifications");
            var restarted = new MonitorService(new GitLabClient(http), store, notifier, new Dashboard());
            await restarted.InitializeAsync(default);
            await restarted.PollAsync(default);
            Check(notifier.Count == 4, "Restart retains notification deduplication");
            handler.OmitGlobalPipelines = true;
            handler.ActiveStatus = "success";
            handler.Requests.Clear();
            await restarted.PollAsync(default);
            Check(handler.Requests.Count(r => r.Contains("/projects/") && r.Contains("/pipelines/")) == 4 &&
                (await store.LoadAsync(default)).All(a => a.ActivePipelines.Count == 0), "Refresh older active pipelines beyond creation window and stop tracking completed ones");
            Check(notifier.Count == 4, "Status refresh never repeats new-pipeline notification");
            handler.Requests.Clear();
            await restarted.PollAsync(default);
            Check(handler.Requests.Count == 2, "Idle monitoring uses one small global request per account");
            var before = (await store.LoadAsync(default)).First().Checkpoint;
            handler.Failure = HttpStatusCode.InternalServerError;
            await restarted.PollAsync(default);
            Check((await store.LoadAsync(default)).First().Checkpoint == before, "Failed scan does not advance checkpoint");
            handler.Failure = null;
            // Return storage ownership to the original coordinator after the restart simulation.
            await monitor.PollAsync(default);
            var changedAccountId = dashboard.Accounts.First().Id;
            var otherBefore = (await store.LoadAsync(default)).Single(a => a.Id != changedAccountId);
            await monitor.SetMonitoringModeAsync(changedAccountId, MonitoringMode.CommitAuthor, default);
            var afterChange = await store.LoadAsync(default);
            var otherAfter = afterChange.Single(a => a.Id != changedAccountId);
            Check(afterChange.Single(a => a.Id == changedAccountId).MonitoringMode == MonitoringMode.CommitAuthor &&
                otherAfter.MonitoringMode == MonitoringMode.TriggeringUser && otherAfter.Checkpoint == otherBefore.Checkpoint &&
                otherAfter.MonitoringSince == otherBefore.MonitoringSince && dashboard.Pipelines.Count == 2 &&
                dashboard.Pipelines.All(p => p.AccountId != changedAccountId),
                "Changing one account leaves another user on the same instance and its overview untouched");
            handler.UserId = 42;
            await monitor.AddAsync("https://gitlab.example/gitlab", "refreshed-token", default);
            Check((await store.LoadAsync(default)).Single(a => a.Id == changedAccountId).MonitoringMode == MonitoringMode.CommitAuthor,
                "Renewing authentication preserves the account monitoring preference");
            handler.UserId = 44;
            await monitor.AddAsync("https://gitlab.example/gitlab", "third-user-token", default);
            Check((await store.LoadAsync(default)).Single(a => a.UserId == 44).MonitoringMode == MonitoringMode.TriggeringUser, "New user on existing instance starts with the default mode");
            handler.UserId = 42;
            await monitor.AddAsync("https://another.example", "another-instance-token", default);
            Check((await store.LoadAsync(default)).Single(a => a.Instance == "https://another.example/").MonitoringMode == MonitoringMode.TriggeringUser,
                "Same user on a different GitLab instance keeps independent default mode");
            var modesAfterRestart = new Dashboard();
            await new MonitorService(new GitLabClient(http), store, notifier, modesAfterRestart).InitializeAsync(default);
            Check(modesAfterRestart.AccountSettings.Count == 4 && modesAfterRestart.AccountSettings.Single(a => a.Id == changedAccountId).Mode == MonitoringMode.CommitAuthor &&
                modesAfterRestart.AccountSettings.Where(a => a.Id != changedAccountId).All(a => a.Mode == MonitoringMode.TriggeringUser),
                "Independent user-instance modes survive encrypted storage and restart");
            await monitor.RemoveAsync(dashboard.Accounts.First().Id, default);
            Check((await store.LoadAsync(default)).Count == 3 && dashboard.Accounts.Count == 3, "Disconnect removes encrypted account and dashboard entry");
            await File.WriteAllTextAsync(path, "corrupted");
            var damaged = new MonitorService(new GitLabClient(http), store, notifier, new Dashboard());
            try { await damaged.InitializeAsync(default); throw new Exception("Expected corrupt storage failure"); }
            catch (InvalidOperationException) { Check(await File.ReadAllTextAsync(path) == "corrupted", "Corrupt credential file is preserved"); }
            try { await damaged.AddAsync("https://gitlab.example", "token", default); throw new Exception("Expected storage protection"); }
            catch (InvalidOperationException) { Check(true, "Corrupt storage cannot be overwritten by adding account"); }
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void RenderWindows(bool desktop)
    {
        var app = new App.App();
        app.InitializeComponent();
        var folder = Path.GetFullPath("artifacts/previews");
        Directory.CreateDirectory(folder);
        var accountId = Guid.NewGuid();
        app.Dashboard.Accounts.Add(new AccountRow(accountId, "jan.novak", "https://gitlab.example.com/", "Sleduje", "12 projektov · posledná kontrola 14:32:08", Brushes.SeaGreen));
        app.Dashboard.AccountSettings.Add(new AccountSettingsRow(accountId, "jan.novak", "https://gitlab.example.com/", MonitoringMode.TriggeringUser));
        foreach (var item in new[] { (101L, "running", "feature/notifications"), (100L, "success", "main"), (99L, "failed", "fix/build-cache") })
        {
            app.Dashboard.Pipelines.Add(new PipelineRow(accountId, new Pipeline(item.Item1, 1, "team / deployment-service", item.Item3, "a7b648debaae", item.Item2,
                "https://gitlab.example.com/team/project/-/pipelines/" + item.Item1, "push", DateTimeOffset.Now, DateTimeOffset.Now), "jan.novak", "https://gitlab.example.com/"));
        }
        Render(new StatusWindow(app), Path.Combine(folder, "status.png"));
        Render(new SettingsWindow(app), Path.Combine(folder, "settings.png"));
        Render(new ConnectAccountWindow(app), Path.Combine(folder, "connect-account.png"));
        Render(new AboutWindow(), Path.Combine(folder, "about.png"));
        Check(true, "Render status, settings, connect account and About windows");
        app.ShowConnectAccount();
        var connectAccount = app.Windows.OfType<ConnectAccountWindow>().Single();
        var tokenBox = (System.Windows.Controls.PasswordBox)connectAccount.FindName("TokenBox");
        tokenBox.Password = "synthetic-test-token";
        connectAccount.Close();
        Check(!connectAccount.IsVisible && tokenBox.Password.Length == 0, "Closing connect account hides window and clears token");
        app.ShowConnectAccount();
        Check(connectAccount.IsVisible && ReferenceEquals(connectAccount, app.Windows.OfType<ConnectAccountWindow>().Single()), "Reopening connect account reuses window");
        connectAccount.WindowState = WindowState.Minimized;
        app.ShowConnectAccount();
        Check(connectAccount.WindowState == WindowState.Normal, "Opening connect account restores minimized window");
        connectAccount.Close();
        app.ShowAbout();
        var about = app.Windows.OfType<AboutWindow>().Single();
        about.Close();
        Check(!about.IsVisible, "Closing About hides the window");
        app.ShowAbout();
        Check(about.IsVisible && ReferenceEquals(about, app.Windows.OfType<AboutWindow>().Single()), "Reopening About reuses the existing window");
        about.Close();
        app.ShowStatus();
        var status = app.Windows.OfType<StatusWindow>().Single();
        status.Close();
        Check(!status.IsVisible && app.Windows.OfType<StatusWindow>().Count() == 1, "Closing status hides window and keeps app running");
        app.ShowStatus();
        Check(status.IsVisible && ReferenceEquals(status, app.Windows.OfType<StatusWindow>().Single()), "Reopening status reuses the existing window");
        status.WindowState = WindowState.Minimized;
        app.ShowStatus();
        Check(status.WindowState == WindowState.Normal, "Tray action restores minimized status window");
        status.Close();
        if (desktop) NativeMenuChecks.Run(Check);
        app.Shutdown();
    }

    private static void Render(Window window, string path)
    {
        window.Show(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
        window.Close();
    }

    private sealed class FakeNotifier : IPipelineNotifier
    {
        public int Count { get; private set; }
        public void Show(Account account, Pipeline pipeline) => Count++;
    }

    private sealed class FakeGitLab : HttpMessageHandler
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public long UserId { get; set; } = 42;
        public List<string> Requests { get; } = [];
        public HttpStatusCode? Failure { get; set; }
        public bool ProjectsPaginated { get; set; }
        public bool ReturnMaliciousUrl { get; set; }
        public bool GlobalPagination { get; set; }
        public bool MaliciousNextLink { get; set; }
        public HttpStatusCode? GlobalFailure { get; set; }
        public bool OmitGlobalPipelines { get; set; }
        public string ActiveStatus { get; set; } = "running";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Check(request.Headers.Contains("PRIVATE-TOKEN"), "Authenticate every API request");
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            if (Failure is { } failure)
            {
                var error = new HttpResponseMessage(failure);
                error.Headers.TryAddWithoutValidation("Retry-After", "120");
                return Task.FromResult(error);
            }
            object data;
            string? next = null;
            string? linkNext = null;
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/user")) data = new { id = UserId, username = "user" + UserId, name = "Test User", email = "author@example.com", commit_email = "author@example.com" };
            else if (path.EndsWith("/user/emails")) data = new object[] { new { email = "alias@example.com", confirmed_at = (DateTimeOffset?)Now }, new { email = "unconfirmed@example.com", confirmed_at = (DateTimeOffset?)null } };
            else if (path.EndsWith("/projects"))
            {
                var second = url.Contains("page=2");
                data = new[] { new { id = second ? 2 : 1, path_with_namespace = second ? "team/second" : "team/project" } };
                next = ProjectsPaginated && !second ? "2" : "";
            }
            else if (path.EndsWith("/api/v4/pipelines"))
            {
                if (GlobalFailure is { } status) return Task.FromResult(new HttpResponseMessage(status));
                var second = url.Contains("cursor=next");
                data = OmitGlobalPipelines ? Array.Empty<object>() : GlobalPagination
                    ? new[] { PipelineFixture(second ? 11 : 10, second ? "other" : "own", Now, second ? "web" : "push") }
                    : new[] { PipelineFixture(10, "own", Now, "push"), PipelineFixture(11, "other", Now, "web") };
                if (GlobalPagination && !second) linkNext = "https://gitlab.example/gitlab/api/v4/pipelines?cursor=next&per_page=100";
                if (MaliciousNextLink) linkNext = "https://attacker.example/api/v4/pipelines?cursor=next";
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(path, @"/pipelines/\d+$"))
                data = PipelineFixture(long.Parse(path.Split('/').Last()), "own", Now, "push", ActiveStatus);
            else if (path.EndsWith("/pipelines"))
            {
                var mr = url.Contains("source=merge_request_event");
                data = path.Contains("/projects/2/") ? Array.Empty<object>() : new object[]
                {
                    PipelineFixture(mr ? 11 : 10, "own", Now, mr ? "merge_request_event" : "push"),
                    PipelineFixture(12, "other", Now, mr ? "merge_request_event" : "push"),
                    PipelineFixture(13, "old", Now.AddDays(-10), mr ? "merge_request_event" : "push")
                };
            }
            else if (path.Contains("/repository/commits/")) data = new { author_email = path.EndsWith("/other") ? "someone-else@example.com" : "AUTHOR@example.com" };
            else throw new InvalidOperationException("Unexpected request: " + url);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
            if (next != null) response.Headers.TryAddWithoutValidation("X-Next-Page", next);
            if (linkNext != null) response.Headers.TryAddWithoutValidation("Link", $"<{linkNext}>; rel=\"next\"");
            return Task.FromResult(response);
        }

        private object PipelineFixture(long id, string sha, DateTimeOffset time, string source, string status = "running") => new
        {
            id, sha, source, project_id = id == 11 ? 2 : 1, @ref = "main", status, created_at = time, updated_at = time,
            web_url = ReturnMaliciousUrl ? "https://attacker.example/pipeline" : $"https://gitlab.example/gitlab/team/{(id == 11 ? "second" : "project")}/-/pipelines/{id}"
        };
    }
}
