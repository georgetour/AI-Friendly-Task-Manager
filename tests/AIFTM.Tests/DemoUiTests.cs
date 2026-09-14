using System.Net;
using Microsoft.Playwright;
using AIFTM.Api.Demo;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// The exported demo, in a browser, served the way GitHub Pages serves it — from a folder, with
/// nothing behind it.
///
/// Every other test here runs the app against its own C# process. This one proves the claim the
/// demo actually makes: that the same front end works with no server at all, and that a link
/// someone pastes still opens the story it names.
/// </summary>
[Collection("ui")]
public class DemoUiTests(UiFixture fx) : IDisposable
{
    private const string Prefix = "/AI-Friendly-Task-Manager";

    private readonly string _site = Path.Combine(Path.GetTempPath(), "aiftm-site-" + Guid.NewGuid().ToString("N"));
    private HttpListener? _server;

    public void Dispose()
    {
        _server?.Close();
        if (Directory.Exists(_site)) Directory.Delete(_site, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_demo_is_the_whole_app_with_no_server_behind_it()
    {
        var origin = ExportAndServe();
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync($"{origin}{Prefix}/");

        // The board drew, which means Alpine started, the assets resolved from a folder, and
        // demo-api.js answered the board request.
        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".summary-total")).ToContainTextAsync("24 stories in 8 epics");
        await Assertions.Expect(page.Locator(".demo-chip")).ToBeVisibleAsync();

        // Navigation is real URLs, under the folder.
        await page.Locator(".epic-open").First.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{origin}{Prefix}/developer-tooling");
        // Scoped to the epic view: every view stays in the document behind x-show, so an unscoped
        // .story-row finds the board's copy, which is on the page but not on the screen.
        await page.Locator("#epicView .story-row .aiftm-open").First.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{origin}{Prefix}/developer-tooling/backlog-board");

        // And a story's folder — tasks, test cases, description — came out of the baked data.
        await Assertions.Expect(page.Locator(".detail .md")).ToContainTextAsync("board");
        await Assertions.Expect(page.Locator(".detail .lrow").First).ToBeVisibleAsync();

        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task A_pasted_deep_link_opens_the_story_it_names()
    {
        // Pages answers an unknown path with 404.html, so the app has to boot from that and read
        // the address bar. Without it, every link anyone shares would land on an error page.
        var origin = ExportAndServe();
        var (page, errors) = await fx.NewPageAsync();

        var response = await page.GotoAsync($"{origin}{Prefix}/core-application/user-management");

        Assert.Equal(404, response!.Status);      // Pages' own status; the body is the app
        await Assertions.Expect(page.Locator(".detail h1")).ToHaveTextAsync("User Management");
        await Assertions.Expect(page).ToHaveURLAsync($"{origin}{Prefix}/core-application/user-management");

        // The only complaint is the browser noting that status. Nothing in the app failed.
        Assert.All(errors, e => Assert.Contains("404", e, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Clicking_things_works_and_says_so_when_it_cannot()
    {
        var origin = ExportAndServe();
        var (page, errors) = await fx.NewPageAsync();

        // In through the front door, so the only console output is the app's own. A deep link
        // arrives as a 404 by design, and that is asserted in its own test.
        await page.GotoAsync($"{origin}{Prefix}/");
        await page.Locator(".story-row .aiftm-open").First.ClickAsync();

        // A status change writes to the session's copy — the demo is for trying, not for reading.
        await page.Locator(".detail-bar .chip.lg").ClickAsync();
        await page.Locator(".pop .pop-row:has-text('On Hold')").ClickAsync();
        await Assertions.Expect(page.Locator(".detail-bar .chip.lg")).ToContainTextAsync("On Hold");

        // And it is still there after navigating away and back, because it is one page's memory.
        await page.Locator(".crumb-link").First.ClickAsync();
        await Assertions.Expect(page.Locator(".summary-total")).ToBeVisibleAsync();
        await page.Locator(".story-row .aiftm-open").First.ClickAsync();
        await Assertions.Expect(page.Locator(".detail-bar .chip.lg")).ToContainTextAsync("On Hold");

        // Choosing the current epic is a write like any other, so it works here too. Left out, this
        // one control would refuse while the status chip beside it worked.
        await page.Locator(".crumb-link").First.ClickAsync();
        // Developer Tooling, not Core Application — the latter is already current by inference in
        // the shipped demo, so clicking it would prove nothing.
        var second = page.Locator(".epic-head", new() { HasTextString = "Developer Tooling" }).Locator(".cur-set");
        await Assertions.Expect(second).ToHaveTextAsync("Set as current");
        await second.ClickAsync();
        await Assertions.Expect(second).ToHaveTextAsync("CURRENT EPIC");

        // What the demo cannot do, it explains — rather than failing as a bare network error.
        await page.Locator("#btnStage").ClickAsync();
        await Assertions.Expect(page.Locator(".toast")).ToContainTextAsync("Not in the demo");

        UiFixture.AssertNoConsoleErrors(errors);
    }

    /// <summary>Builds the site and serves it the way a project site is served: everything under
    /// /AI-Friendly-Task-Manager/, and 404.html for anything that is not a file.</summary>
    private string ExportAndServe()
    {
        var repo = FindRepo();
        var templates = Path.Combine(repo, "templates");
        var svc = new BacklogService(() => Path.Combine(templates, "BACKLOG.template.yaml"),
                                     () => Path.Combine(templates, "skills"));

        DemoSite.Build(svc, Path.Combine(repo, "src", "AIFTM.Api", "wwwroot"), _site, Prefix);

        var port = FreePort();
        _server = new HttpListener();
        _server.Prefixes.Add($"http://127.0.0.1:{port}/");
        _server.Start();
        _ = Task.Run(Serve);

        return $"http://127.0.0.1:{port}";
    }

    private async Task Serve()
    {
        while (_server is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _server.GetContextAsync(); }
            catch (Exception) { return; }         // listener closed

            var path = ctx.Request.Url!.AbsolutePath;
            var rel = path.StartsWith(Prefix, StringComparison.Ordinal) ? path[Prefix.Length..] : null;
            if (rel is "" or "/") rel = "/index.html";

            var file = rel is null ? null : Path.Combine(_site, rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            var found = file is not null && File.Exists(file);

            ctx.Response.StatusCode = found ? 200 : 404;
            ctx.Response.ContentType = ContentType(found ? file! : "404.html");

            var body = await File.ReadAllBytesAsync(found ? file! : Path.Combine(_site, "404.html"));
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        }
    }

    private static string ContentType(string file) => Path.GetExtension(file) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        _ => "application/octet-stream",
    };

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "templates"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("templates/ not found above " + AppContext.BaseDirectory);
    }
}
