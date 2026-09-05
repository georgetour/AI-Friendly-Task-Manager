using AIFTM.Api.Backlog;

namespace AIFTM.Tests;

public class YamlIndexTests
{
    private const string Yaml = """
        project: Acme App
        roadmap: [V0.1, V1]
        epics:
          - number: 0
            title: Developer Tooling
            stories:
              - code: US-01
                title: Backlog Board
                status: Done
                release: V0.1
                folder: backlog-board
        """;

    [Fact]
    public void Parse_reads_the_index()
    {
        var board = YamlIndex.Parse(Yaml);

        Assert.Equal("Acme App", board.Project);
        Assert.Equal(new[] { "V0.1", "V1" }, board.Roadmap);
        var epic = Assert.Single(board.Epics);
        Assert.Equal(0, epic.Number);
        Assert.Equal("Developer Tooling", epic.Title);
        var story = Assert.Single(epic.Stories);
        Assert.Equal("US-01", story.Code);
        Assert.Equal("Done", story.Status);
        Assert.Equal("V0.1", story.Release);
        Assert.Equal("backlog-board", story.Folder);
    }

    [Fact]
    public void Parse_assigns_slugs_to_epics_and_stories()
    {
        var board = YamlIndex.Parse(Yaml);

        Assert.Equal("developer-tooling", board.Epics[0].Slug);
        Assert.Equal("backlog-board", board.Epics[0].Stories[0].Slug);
    }

    [Fact]
    public void Write_then_Parse_round_trips()
    {
        var board = YamlIndex.Parse(Yaml);

        var again = YamlIndex.Parse(YamlIndex.Write(board));

        Assert.Equal("Acme App", again.Project);
        Assert.Equal("US-01", again.Epics[0].Stories[0].Code);
        Assert.Equal("Done", again.Epics[0].Stories[0].Status);
        Assert.Equal("backlog-board", again.Epics[0].Stories[0].Folder);
        Assert.Equal(new[] { "V0.1", "V1" }, again.Roadmap);
    }

    [Fact]
    public void The_current_epic_is_remembered_and_absent_until_it_is_chosen()
    {
        // Nobody has chosen: the file gains no line at all, so an untouched backlog is untouched.
        var board = YamlIndex.Parse(Yaml);
        Assert.Null(board.CurrentEpic);
        Assert.DoesNotContain("currentEpic", YamlIndex.Write(board), StringComparison.Ordinal);

        var chosen = YamlIndex.Write(board with { CurrentEpic = 0 });
        Assert.Contains("currentEpic: 0", chosen, StringComparison.Ordinal);
        Assert.Equal(0, YamlIndex.Parse(chosen).CurrentEpic);
    }

    [Fact]
    public void A_current_epic_that_no_longer_exists_is_treated_as_unchosen()
    {
        // The epic it named was deleted. Inferring one from the statuses is better than badging
        // nothing, and better than pointing at an epic that is not on the board.
        var board = YamlIndex.Parse(Yaml + "\ncurrentEpic: 7\n");

        Assert.Null(board.CurrentEpic);
    }

    [Fact]
    public void Write_is_stable_so_an_unchanged_board_produces_an_identical_file()
    {
        // Deterministic output is what keeps a one-status change to a one-line git diff.
        var once = YamlIndex.Write(YamlIndex.Parse(Yaml));

        var twice = YamlIndex.Write(YamlIndex.Parse(once));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Parse_defaults_missing_optional_fields()
    {
        var board = YamlIndex.Parse("""
            project: Minimal
            epics:
              - number: 0
                title: Only Epic
            """);

        Assert.Empty(board.Epics[0].Stories);
        Assert.Empty(board.Roadmap);
    }

    [Fact]
    public void Parse_handles_an_empty_file_without_throwing()
    {
        var board = YamlIndex.Parse("");

        Assert.Empty(board.Epics);
        Assert.Empty(board.Roadmap);
    }

    [Fact]
    public void A_story_with_no_status_defaults_to_Not_Yet_Started()
    {
        var board = YamlIndex.Parse("""
            project: Test
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: Board
                    folder: board
            """);

        Assert.Equal("Not Yet Started", board.Epics[0].Stories[0].Status);
    }

    [Fact]
    public void Duplicate_epic_titles_get_distinct_slugs()
    {
        var board = YamlIndex.Parse("""
            project: Test
            epics:
              - number: 0
                title: Reporting
              - number: 1
                title: Reporting
            """);

        Assert.Equal("reporting", board.Epics[0].Slug);
        Assert.Equal("reporting-2", board.Epics[1].Slug);
    }

    [Fact]
    public void A_duplicate_key_is_rejected()
    {
        // The default is to let the last key win, which would drop a whole epic with no error
        // anywhere. The integrity checks cannot catch that: by the time they run, it is gone.
        var yaml = """
            project: Demo
            epics:
              - number: 1
                title: Kept
            epics:
              - number: 2
                title: Also kept
            """;

        Assert.Throws<YamlDotNet.Core.YamlException>(() => YamlIndex.Parse(yaml));
    }

    [Fact]
    public void A_duplicate_field_on_a_story_is_rejected()
    {
        var yaml = """
            project: Demo
            epics:
              - number: 1
                title: One
                stories:
                  - code: US-01
                    title: First
                    title: Second
            """;

        Assert.Throws<YamlDotNet.Core.YamlException>(() => YamlIndex.Parse(yaml));
    }
}
