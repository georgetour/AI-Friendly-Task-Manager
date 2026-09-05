using System.Text.RegularExpressions;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// Where the browser and the server both check the same rule, they must agree.
///
/// They didn't once: the server was changed to require a .yaml backlog while the Configure form
/// still demanded .md, so the form refused a path the server would have accepted — and no amount of
/// server-side testing could see it, because the request never got that far. A client check that
/// disagrees with the server is worse than no client check at all.
/// </summary>
public class ClientServerAgreementTests
{
    private static string AppJs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AIFTM.Api", "wwwroot", "app.js");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("wwwroot/app.js not found — expected under the repo root.");
    }

    /// <summary>Pulls the backlog-path pattern straight out of the shipped JavaScript.</summary>
    private static Regex ClientBacklogPattern()
    {
        var match = Regex.Match(AppJs(), @"!\s*/(?<body>[^/\n]+)/(?<flags>[a-z]*)\s*\.test\(backlog\)");
        Assert.True(match.Success,
            "Could not find the backlog-path check in app.js. If it moved, update this test — the "
          + "point is that the two rules stay in step.");

        var options = match.Groups["flags"].Value.Contains('i')
            ? RegexOptions.IgnoreCase : RegexOptions.None;
        return new Regex(match.Groups["body"].Value, options);
    }

    [Theory]
    [InlineData("C:/projects/app/BACKLOG.yaml")]
    [InlineData("C:/projects/app/backlog.yml")]
    [InlineData("C:/projects/app/BACKLOG.YAML")]
    public void The_form_accepts_exactly_what_the_server_accepts(string path)
    {
        // Server: no exception. Client: the pattern matches, so the form lets it through.
        var fromServer = AppConfigService.ValidateBacklogPath(path);
        Assert.Equal(Path.GetFullPath(path), fromServer);

        Assert.True(ClientBacklogPattern().IsMatch(path),
            $"The server accepts '{path}' but the Configure form would block it.");
    }

    [Theory]
    [InlineData("C:/projects/app/BACKLOG.md")]
    [InlineData("C:/projects/app/BACKLOG.txt")]
    [InlineData("C:/projects/app/BACKLOG")]
    public void The_form_rejects_exactly_what_the_server_rejects(string path)
    {
        Assert.Throws<AIFTM.Api.Backlog.BacklogValidationException>(
            () => AppConfigService.ValidateBacklogPath(path));

        Assert.False(ClientBacklogPattern().IsMatch(path),
            $"The server rejects '{path}' but the Configure form would let it through.");
    }

    [Fact]
    public void The_UI_never_tells_anyone_the_backlog_is_a_markdown_file()
    {
        // Placeholders, hints and error messages are documentation people actually read. After the
        // move to YAML these all said ".md", which is how the failing Configure page looked correct.
        var js = AppJs();

        Assert.DoesNotContain("BACKLOG.md", js);
        Assert.DoesNotContain(".md file", js);
    }
}
