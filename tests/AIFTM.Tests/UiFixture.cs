using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Playwright;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// Runs the real app on a real port and drives it with a real browser.
///
/// Everything else in this suite tests C# directly, which is why the UI was the one part with no
/// coverage — and where the bugs kept being found by hand. A menu that cannot render because a
/// parent is display:none, or an Alpine binding the CSP build rejects, is invisible to every other
/// kind of test here.
///
/// The app is started as a process rather than an in-memory TestServer because a browser needs
/// something listening on a socket, and because this exercises the same startup path a user gets.
/// </summary>
public sealed class UiFixture : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-ui-" + Guid.NewGuid().ToString("N"));
    private Process? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        WriteSampleProject();

        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _app = StartApp(port);
        await WaitUntilServing();

        // Installs Chromium on first run, and on Linux the system libraries it needs — a bare
        // .NET SDK image has none of them, and the browser fails to launch with a wall of missing
        // libx11/libnss3 names rather than anything that reads like a cause.
        //
        // Done in-process on purpose: the installer Playwright ships is a PowerShell script, and
        // this repo keeps CI free of shell dependencies. Idempotent once installed.
        var exit = Microsoft.Playwright.Program.Main(["install", "--with-deps", "chromium"]);
        if (exit != 0)
            throw new InvalidOperationException(
                $"Playwright could not install Chromium (exit {exit}). On Linux this needs root or "
              + "passwordless sudo to add the browser's system libraries.");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    /// <summary>A page sized like a phone or a desktop, with console errors collected. Anything
    /// Alpine's CSP build rejects surfaces here rather than as a silently dead button.</summary>
    /// <param name="touch">Emulates a touch screen, which is what makes `pointer: coarse` match.
    /// Controls the app reveals on hover are permanently visible there, so a row is laid out
    /// differently on a real phone than in a narrow desktop window.</param>
    public async Task<(IPage Page, List<string> Errors)> NewPageAsync(int width = 1280, int height = 800,
                                                                      bool touch = false)
    {
        var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
        });
        var page = await context.NewPageAsync();

        var errors = new List<string>();
        // With the URL: "Failed to load resource: 404" names nothing on its own, and a console
        // assertion you cannot act on is a test that wastes the time it just saved.
        page.Console += (_, m) => { if (m.Type == "error" && Ours(m.Location)) errors.Add($"{m.Text} @ {m.Location}"); };
        page.PageError += (_, e) => errors.Add(e);

        return (page, errors);
    }

    /// <summary>
    /// Whether a console error came from something this app is responsible for.
    ///
    /// These tests exist to catch our own mistakes — an Alpine binding the CSP build rejects, a
    /// button wired to nothing. The page also loads its fonts from Google, and CI failed once
    /// because fonts.gstatic.com returned 404 for a woff2 that morning. Failing a build over
    /// somebody else's CDN teaches nothing and trains people to re-run red builds.
    /// </summary>
    private static bool Ours(string location) =>
        !location.Contains("://", StringComparison.Ordinal)
        || location.Contains("127.0.0.1", StringComparison.Ordinal)
        || location.Contains("localhost", StringComparison.Ordinal);

    /// <summary>Fails with what the browser actually said. `Assert.Empty` on its own reports
    /// "collection was not empty", which for an Alpine binding the CSP build rejects is the least
    /// useful half of the information available.</summary>
    public static void AssertNoConsoleErrors(List<string> errors) =>
        Assert.True(errors.Count == 0, string.Join("\n", errors));

    /// <summary>A path for a project that does not exist yet — adding it should create the backlog
    /// from the template, which is the behaviour Configure already has for a single project.</summary>
    public string UncreatedProjectPath => Path.Combine(_root, "second", "BACKLOG.yaml");

    /// <summary>The project every other test expects to be open.</summary>
    public string PrimaryBacklogPath => Path.Combine(_root, "BACKLOG.yaml");

    /// <summary>A writable copy of the shipped demo — 8 epics, 24 stories, real tasks and test
    /// cases. Copied rather than pointed at, because searching it is a read but selecting a project
    /// is a write, and `templates/` is part of the repo.</summary>
    public string DemoBacklogPath
    {
        get
        {
            var demo = Path.Combine(_root, "demo");
            if (!Directory.Exists(demo))
            {
                var templates = FindRepoPath("templates");
                Directory.CreateDirectory(demo);
                File.Copy(Path.Combine(templates, "BACKLOG.template.yaml"),
                          Path.Combine(demo, "BACKLOG.yaml"));
                CopyDirectory(Path.Combine(templates, "skills"), Path.Combine(demo, "skills"));
            }
            return Path.Combine(demo, "BACKLOG.yaml");
        }
    }

    /// <summary>Switches the app to a project by path, the way the Projects page does.</summary>
    public async Task UseProjectAsync(string backlogPath)
    {
        using var http = new HttpClient();
        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { backlogPath, skillsPath = (string?)null }),
            System.Text.Encoding.UTF8, "application/json");

        var res = await http.PostAsync($"{BaseUrl}/api/projects", body);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Forgets a project again. One app serves the whole collection, so a test that adds
    /// one has to take it back out — otherwise it decides how many rows every later test counts.
    /// The backlog file itself is untouched, which is what the remove page promises.</summary>
    public async Task ForgetProjectAsync(string backlogPath)
    {
        using var http = new HttpClient();

        // Asked for rather than guessed: a project is named by its backlog's `project:` field, and
        // removal deliberately refuses a name that does not match.
        var list = await http.GetFromJsonAsync<ProjectList>($"{BaseUrl}/api/projects")
                   ?? throw new InvalidOperationException("The projects list could not be read.");
        var name = list.Projects
            .First(p => string.Equals(Path.GetFullPath(p.BacklogPath), Path.GetFullPath(backlogPath),
                                      StringComparison.OrdinalIgnoreCase))
            .Name;

        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { path = backlogPath, confirmName = name }),
            System.Text.Encoding.UTF8, "application/json");

        var res = await http.PostAsync($"{BaseUrl}/api/projects/remove", body);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Switches back to the primary project. One app serves the whole collection, so a
    /// test that changes which project is open has to put it back — otherwise it decides what
    /// every test after it sees.</summary>
    public async Task UsePrimaryProjectAsync()
    {
        using var http = new HttpClient();
        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { path = PrimaryBacklogPath }),
            System.Text.Encoding.UTF8, "application/json");

        var res = await http.PostAsync($"{BaseUrl}/api/projects/select", body);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Puts the current epic back to unchosen. Targeted at that one line rather than
    /// rewriting the sample backlog, so a test that restores this does not also silently undo the
    /// statuses another test is asserting on.</summary>
    public Task ClearCurrentEpicAsync()
    {
        var kept = File.ReadAllLines(PrimaryBacklogPath)
                       .Where(l => !l.StartsWith("currentEpic:", StringComparison.Ordinal));

        File.WriteAllLines(PrimaryBacklogPath, kept);
        return Task.CompletedTask;
    }

    /// <summary>Puts a known list of tasks on a story, through the app's own endpoint rather than
    /// by writing the file behind its back.</summary>
    public async Task SetTasksAsync(string code, string[] texts)
    {
        using var http = new HttpClient();
        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(
                new { tasks = texts.Select(t => new { text = t, done = false }) }),
            System.Text.Encoding.UTF8, "application/json");

        var res = await http.PutAsync($"{BaseUrl}/api/story/{code}/tasks", body);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Puts the sample story's tasks back the way WriteSampleProject left them. Not
    /// emptied: "pelican" is the word the search tests look for, and a story left holding three
    /// tasks named First/Second/Third decides what the next test finds and what its meter reads.
    /// </summary>
    public Task RestoreSampleTasksAsync() => SetTasksAsync("US-01", [SampleTask]);

    /// <summary>Something to find that is not in a title — see WriteSampleProject.</summary>
    private const string SampleTask = "Paint the pelican column";

    /// <summary>Sets a story's status, so a test that changes it can put it back.</summary>
    public async Task SetStatusAsync(string code, string status)
    {
        using var http = new HttpClient();
        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { status }),
            System.Text.Encoding.UTF8, "application/json");

        var res = await http.PostAsync($"{BaseUrl}/api/story/{code}/status", body);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Puts a known list of test cases on a story, through the app's own endpoint.</summary>
    public async Task SetTestCasesAsync(string code, string[] texts)
    {
        using var http = new HttpClient();
        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(
                new { testCases = texts.Select(t => new { text = t, status = "Not Run" }) }),
            System.Text.Encoding.UTF8, "application/json");

        var res = await http.PutAsync($"{BaseUrl}/api/story/{code}/test-cases", body);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Clears the logo. One app serves the whole collection, so a test that sets one has
    /// to put it back — with a logo present the slot is a link rather than the "+" button, which
    /// decides what a later test sees.</summary>
    public async Task ClearLogoAsync()
    {
        using var http = new HttpClient();
        var res = await http.DeleteAsync($"{BaseUrl}/api/config/logo");
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Uploads a logo through the app's own endpoint, so the logo-set state is reached the
    /// way a person reaches it rather than by writing config behind the app's back.</summary>
    public async Task SetLogoAsync()
    {
        // A 1x1 PNG — the endpoint only cares that it is an image with an allowed extension.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        using var http = new HttpClient();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "logo", "logo.png");

        var res = await http.PostAsync($"{BaseUrl}/api/config/logo", form);
        res.EnsureSuccessStatusCode();
    }

    private void WriteSampleProject()
    {
        Directory.CreateDirectory(Path.Combine(_root, "skills", "board"));
        Directory.CreateDirectory(Path.Combine(_root, "skills", "write-back"));

        File.WriteAllText(Path.Combine(_root, "BACKLOG.yaml"), """
            project: UI Test
            roadmap: [V1]
            epics:
              - number: 0
                title: Tooling
                release: V1
                stories:
                  - code: US-01
                    title: Backlog Board
                    status: Done
                    folder: board
                  - code: US-02
                    title: Status Write Back
                    status: Not Yet Started
                    folder: write-back
              - number: 1
                title: Empty Epic
            """);

        // One epic has a release, one does not: that difference is what used to knock the epic
        // header's own columns out of line, now that the release moved from the story to the epic.
        File.WriteAllText(Path.Combine(_root, "skills", "board", "SKILL.md"),
            "---\nname: board\n---\n\n# Backlog Board\n\n## Description\n\nThe board.\n");
        File.WriteAllText(Path.Combine(_root, "skills", "write-back", "SKILL.md"),
            "---\nname: write-back\n---\n\n# Status Write Back\n\n## Description\n\nWrites back.\n");

        // Something to find that is not in a title: search's whole claim is that it reaches inside
        // the story folders, and a query that only ever matches a heading would not prove it.
        File.WriteAllText(Path.Combine(_root, "skills", "board", "tasks.yaml"),
            $"- text: {SampleTask}\n  done: false\n");
    }

    private Process StartApp(int port)
    {
        // A content root of our own, holding a copy of wwwroot. Pointing at the source project
        // would work, but aiftm.config.json lives there — so the tests would inherit whatever
        // backlog and logo the developer happens to have configured, and pass or fail by machine.
        var dll = Path.Combine(AppContext.BaseDirectory, "AIFTM.Api.dll");
        var contentRoot = Path.Combine(_root, "app");
        Directory.CreateDirectory(contentRoot);
        CopyDirectory(Path.Combine(FindRepoPath(Path.Combine("src", "AIFTM.Api")), "wwwroot"),
                      Path.Combine(contentRoot, "wwwroot"));

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Seeded through aiftm.config.json rather than --BacklogPath. A deploy-time override
        // always wins over configuration, which would pin the app and make project switching a
        // no-op — so driving it that way would test a mode nobody runs interactively.
        File.WriteAllText(Path.Combine(contentRoot, "aiftm.config.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                BacklogPath = Path.Combine(_root, "BACKLOG.yaml"),
                SkillsPath = Path.Combine(_root, "skills"),
                LogoPath = (string?)null,
                IsDemo = false,
            }));

        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add($"--urls={BaseUrl}");
        psi.ArgumentList.Add($"--contentRoot={contentRoot}");

        var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the app.");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private async Task WaitUntilServing()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var i = 0; i < 60; i++)
        {
            if (_app is { HasExited: true })
                throw new InvalidOperationException($"The app exited early with code {_app.ExitCode}.");
            try
            {
                var res = await http.GetAsync($"{BaseUrl}/api/board");
                if (res.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { /* not listening yet */ }
            catch (TaskCanceledException) { /* still starting */ }
            await Task.Delay(500);
        }
        throw new TimeoutException($"The app never served {BaseUrl}/api/board.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static string FindRepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"{relative} not found above {AppContext.BaseDirectory}.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_app is { HasExited: false })
        {
            _app.Kill(entireProcessTree: true);
            _app.WaitForExit(5000);
        }
        _app?.Dispose();

        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

/// <summary>Binds the fixture to the "ui" collection, so one app and one browser serve every UI
/// test rather than being started per test.</summary>
[CollectionDefinition("ui")]
public sealed class UiCollectionDefinition : ICollectionFixture<UiFixture>;
