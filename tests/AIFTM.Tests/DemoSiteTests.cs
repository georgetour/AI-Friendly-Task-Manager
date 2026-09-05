using System.Text.RegularExpressions;
using AIFTM.Api.Demo;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// What the published demo is made of.
///
/// The claim the demo rests on is that it is the real application and not a rebuild of it, so these
/// check exactly that: the front end is copied rather than written, and only the two things that
/// have to change in a folder — where the app thinks it is, and the absolute asset paths — are
/// touched.
/// </summary>
public class DemoSiteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-demo-" + Guid.NewGuid().ToString("N"));

    private string Out => Path.Combine(_root, "_site");
    private static string Wwwroot => Path.Combine(FindRepo(), "src", "AIFTM.Api", "wwwroot");

    private static BacklogService Service()
    {
        var templates = Path.Combine(FindRepo(), "templates");
        return new BacklogService(() => Path.Combine(templates, "BACKLOG.template.yaml"),
                                  () => Path.Combine(templates, "skills"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_site_is_the_real_front_end_plus_one_script()
    {
        DemoSite.Build(Service(), Wwwroot, Out, "/AI-Friendly-Task-Manager");

        // Byte for byte. If app.js ever needed editing to work here, the demo would be a fork.
        // Found by pattern because the export names each asset after a hash of its own contents.
        Assert.Equal(File.ReadAllText(Path.Combine(Wwwroot, "app.js")), File.ReadAllText(Only("app.*.js")));
        Assert.Equal(File.ReadAllText(Path.Combine(Wwwroot, "app.css")), File.ReadAllText(Only("app.*.css")));
        Assert.Single(Directory.GetFiles(Path.Combine(Out, "vendor"), "alpine-csp.min.*.js"));
        Assert.Single(Directory.GetFiles(Out, "demo-api.*.js"));
    }

    [Fact]
    public void Every_answer_the_app_will_ask_for_is_baked_in()
    {
        DemoSite.Build(Service(), Wwwroot, Out, "/AI-Friendly-Task-Manager");
        var data = File.ReadAllText(Only("demo-data.*.js"));

        Assert.StartsWith("window.AIFTM_DEMO = ", data, StringComparison.Ordinal);
        foreach (var key in new[] { "\"board\"", "\"stories\"", "\"skills\"", "\"search\"", "\"validate\"" })
            Assert.Contains(key, data, StringComparison.Ordinal);

        // The shipped demo, in full: 24 stories, each with its folder read.
        Assert.Contains("\"US-24\"", data, StringComparison.Ordinal);
        Assert.Contains("SKILL.md", data, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deep_link_is_answered_by_the_app_itself()
    {
        // GitHub Pages has no router: /core-application is a 404 to it. Serving the app as the 404
        // body is what hands the URL to readUrl() with the address bar intact.
        DemoSite.Build(Service(), Wwwroot, Out, "/AI-Friendly-Task-Manager");

        Assert.Equal(File.ReadAllText(Path.Combine(Out, "index.html")),
                     File.ReadAllText(Path.Combine(Out, "404.html")));
    }

    [Theory]
    [InlineData("/AI-Friendly-Task-Manager", "/AI-Friendly-Task-Manager")]
    [InlineData("AI-Friendly-Task-Manager/", "/AI-Friendly-Task-Manager")]
    [InlineData("/", "")]
    public void The_folder_it_is_served_from_reaches_the_page_and_its_assets(string given, string expected)
    {
        DemoSite.Build(Service(), Wwwroot, Out, given);
        var html = File.ReadAllText(Path.Combine(Out, "index.html"));

        Assert.Contains($"<meta name=\"app-base\" content=\"{expected}/\">", html, StringComparison.Ordinal);
        Assert.Matches(Regex.Escape(expected) + @"/app\.[0-9a-f]{8}\.css", html);
        Assert.Matches(Regex.Escape(expected) + @"/app\.[0-9a-f]{8}\.js", html);
        Assert.Matches(Regex.Escape(expected) + @"/vendor/alpine-csp\.min\.[0-9a-f]{8}\.js", html);

        // Before app.js, which asks for the board the moment Alpine starts. Matched as a script
        // rather than by prefix: app.css is named the same way and is loaded first, in the head.
        var api = Regex.Match(html, Regex.Escape(expected) + @"/demo-api\.[0-9a-f]{8}\.js");
        var app = Regex.Match(html, Regex.Escape(expected) + @"/app\.[0-9a-f]{8}\.js");
        Assert.True(api.Success && app.Success && api.Index < app.Index,
                    "demo-api.js has to be in place before the first fetch.");
    }

    [Fact]
    public void Nothing_else_in_the_page_is_rewritten()
    {
        // The export is a move, not a fork: two edits, and the rest of index.html untouched.
        var original = File.ReadAllText(Path.Combine(Wwwroot, "index.html"));
        DemoSite.Build(Service(), Wwwroot, Out, "/AI-Friendly-Task-Manager");
        var moved = File.ReadAllText(Path.Combine(Out, "index.html"));

        // Undo the export's edits and the original has to come back exactly. Stronger than counting
        // differing lines, and it says precisely what the export is allowed to do.
        var undone = Regex.Replace(moved,
                @"<script src=""/AI-Friendly-Task-Manager/demo-(data|api)\.[0-9a-f]{8}\.js""></script>\r?\n", "")
            .Replace("content=\"/AI-Friendly-Task-Manager/\"", "content=\"/\"", StringComparison.Ordinal)
            .Replace("\"/AI-Friendly-Task-Manager/", "\"/", StringComparison.Ordinal);

        // ...and the hash the export put into the name of everything it kept.
        undone = Regex.Replace(undone, @"\.[0-9a-f]{8}\.(js|css)""", @".$1""");

        Assert.Equal(original.Replace("\r\n", "\n"), undone.Replace("\r\n", "\n"));
    }

    [Fact]
    public void An_asset_that_changed_gets_a_new_url_and_one_that_did_not_keeps_its_own()
    {
        // GitHub Pages sends Cache-Control: max-age=600 and there is no server of ours in front of
        // it to say otherwise, so for ten minutes a returning visitor keeps what they already had —
        // including, worst of all, a fresh page asking for an app.js their browser answers from
        // cache. A name taken from the contents cannot be answered from cache unless the contents
        // match, so the page and the code it runs are always the same vintage.
        DemoSite.Build(Service(), Wwwroot, Out, "/AI-Friendly-Task-Manager");
        var first = File.ReadAllText(Path.Combine(Out, "index.html"));
        Directory.Delete(Out, true);

        // Built again from the same input: nothing changed, so nothing should be re-downloaded.
        DemoSite.Build(Service(), Wwwroot, Out, "/AI-Friendly-Task-Manager");
        Assert.Equal(first, File.ReadAllText(Path.Combine(Out, "index.html")));

        // Now the data changes, the way it does when a status is clicked before a release.
        var moved = Path.Combine(_root, "moved");
        Directory.CreateDirectory(moved);
        File.Copy(Path.Combine(FindRepo(), "templates", "BACKLOG.template.yaml"),
                  Path.Combine(moved, "BACKLOG.yaml"));
        File.AppendAllText(Path.Combine(moved, "BACKLOG.yaml"), "\ncurrentEpic: 1\n");

        Directory.Delete(Out, true);
        DemoSite.Build(new BacklogService(() => Path.Combine(moved, "BACKLOG.yaml"),
                                          () => Path.Combine(FindRepo(), "templates", "skills")),
                       Wwwroot, Out, "/AI-Friendly-Task-Manager");

        var after = File.ReadAllText(Path.Combine(Out, "index.html"));
        Assert.NotEqual(DataUrl(first), DataUrl(after));
        // ...while the code, untouched, keeps the URL a browser already has.
        Assert.Equal(AppUrl(first), AppUrl(after));
    }

    private static string DataUrl(string html) => Regex.Match(html, @"demo-data\.[0-9a-f]{8}\.js").Value;
    private static string AppUrl(string html) => Regex.Match(html, @"/app\.[0-9a-f]{8}\.js").Value;

    [Fact]
    public void A_backlog_that_will_not_parse_writes_nothing_at_all()
    {
        // It used to copy the front end first and fail on the data, leaving index.html and the
        // assets on disk with no demo-data.js beside them — which a deploy publishes as a blank
        // page that looks like the app is broken.
        var broken = Path.Combine(_root, "BACKLOG.yaml");
        Directory.CreateDirectory(_root);
        File.WriteAllText(broken, "project: Test\nepics:\n  - number: 0\n   title: Bad indent\n");

        var svc = new BacklogService(() => broken, () => Path.Combine(_root, "skills"));

        Assert.ThrowsAny<Exception>(() => DemoSite.Build(svc, Wwwroot, Out, "/AI-Friendly-Task-Manager"));
        Assert.False(Directory.Exists(Out), "A failed export must not leave half a site behind.");
    }

    [Fact]
    public void The_repo_is_found_from_inside_the_project_folder()
    {
        // `dotnet run --project src/AIFTM.Api` runs the app from the project folder, so a
        // path typed at the repo root resolves against the wrong place. This is what fixes it —
        // and it is why the Pages workflow can pass a plain relative path.
        var repo = FindRepo();

        Assert.Equal(repo, DemoSite.RepoRoot(Path.Combine(repo, "src", "AIFTM.Api")));
        Assert.Equal(repo, DemoSite.RepoRoot(repo));
    }

    [Fact]
    public void A_developers_uploaded_logo_is_not_published_with_the_demo()
    {
        // wwwroot/uploads is whatever happens to be on this machine. It is not part of the demo and
        // has no business being pushed to a public site.
        Directory.CreateDirectory(Path.Combine(Wwwroot, "uploads"));

        DemoSite.Build(Service(), Wwwroot, Out, "/AI-Friendly-Task-Manager");

        Assert.False(Directory.Exists(Path.Combine(Out, "uploads")));
    }

    /// <summary>The one file matching a pattern — the export names assets after their contents, so
    /// a test cannot know the hash to ask for.</summary>
    private string Only(string pattern) => Assert.Single(Directory.GetFiles(Out, pattern));

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
