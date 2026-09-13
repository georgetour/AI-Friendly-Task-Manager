using AIFTM.Api.Backlog;

namespace AIFTM.Tests;

public class YamlIndexTests
{
    private const string Yaml = """
        project: Acme App
        roadmap: [1.0.0, 1.5.0]
        epics:
          - number: 0
            version: 0.1.0
            release: 1.0.0
            title: Developer Tooling
            stories:
              - code: US-01
                title: Backlog Board
                status: Done
                folder: backlog-board
        """;

    [Fact]
    public void Parse_reads_the_index()
    {
        var board = YamlIndex.Parse(Yaml);

        Assert.Equal("Acme App", board.Project);
        Assert.Equal(new[] { "1.0.0", "1.5.0" }, board.Roadmap);
        var epic = Assert.Single(board.Epics);
        Assert.Equal(0, epic.Number);
        Assert.Equal("Developer Tooling", epic.Title);
        var story = Assert.Single(epic.Stories);
        Assert.Equal("US-01", story.Code);
        Assert.Equal("Done", story.Status);
        Assert.Equal("backlog-board", story.Folder);
    }

    [Fact]
    public void Parse_reads_version_and_release_from_the_epic()
    {
        var board = YamlIndex.Parse(Yaml);

        var epic = Assert.Single(board.Epics);
        Assert.Equal(0, epic.Number);
        Assert.Equal("0.1.0", epic.Version);
        Assert.Equal("1.0.0", epic.Release);
        Assert.Equal("Developer Tooling", epic.Title);
        var story = Assert.Single(epic.Stories);
        Assert.Equal("US-01", story.Code);
        Assert.Equal("Done", story.Status);
        Assert.Equal("backlog-board", story.Folder);
    }

    [Fact]
    public void Write_omits_version_and_release_when_an_epic_has_neither()
    {
        var board = new Board("Acme App", new[] { "1.0.0" },
            new[] { new Epic(0, "", "", "Developer Tooling", new List<Story>()) });

        var yaml = YamlIndex.Write(board);

        Assert.DoesNotContain("version:", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("release:", yaml, StringComparison.Ordinal);
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
        Assert.Equal(new[] { "1.0.0", "1.5.0" }, again.Roadmap);
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

    private const string LegacyYaml = """
        project: Acme App
        roadmap: [V1, V1.5, V4]
        epics:
          - number: 1
            title: Core Application
            stories:
              - code: US-03
                title: Local Setup
                status: Done
                release: V1
                folder: local-setup
              - code: US-11
                title: Email Notifications
                status: Not Yet Started
                release: V1.5
                folder: email-notifications
              - code: US-12
                title: Reporting
                status: Not Yet Started
                release: V4
                folder: reporting
        """;

    [Fact]
    public void Parse_lifts_a_straddling_epic_to_its_earliest_release_in_roadmap_order()
    {
        var board = YamlIndex.Parse(LegacyYaml);

        var epic = Assert.Single(board.Epics);
        Assert.Equal("V1", epic.Release);
        Assert.True(board.Migrated);
    }

    [Fact]
    public void Parse_lifts_an_agreeing_epic_silently()
    {
        var yaml = """
            project: Acme App
            roadmap: [V1]
            epics:
              - number: 1
                title: Core Application
                stories:
                  - code: US-03
                    title: Local Setup
                    status: Done
                    release: V1
                    folder: local-setup
            """;

        var board = YamlIndex.Parse(yaml);

        Assert.Equal("V1", Assert.Single(board.Epics).Release);
    }

    [Fact]
    public void Parse_leaves_an_epic_with_no_story_releases_unscheduled()
    {
        var yaml = """
            project: Acme App
            roadmap: [V1]
            epics:
              - number: 1
                title: Core Application
                stories:
                  - code: US-03
                    title: Local Setup
                    status: Done
                    folder: local-setup
              - number: 2
                title: Later
                stories:
                  - code: US-04
                    title: Something
                    status: Done
                    release: V1
                    folder: something
            """;

        var board = YamlIndex.Parse(yaml);

        Assert.Equal("", board.Epics[0].Release);
        Assert.Equal("V1", board.Epics[1].Release);
    }

    [Fact]
    public void Parse_sorts_a_release_missing_from_the_roadmap_last()
    {
        var yaml = """
            project: Acme App
            roadmap: [V1]
            epics:
              - number: 1
                title: Core Application
                stories:
                  - code: US-03
                    title: Ghost
                    status: Done
                    release: V9
                    folder: ghost
                  - code: US-04
                    title: Real
                    status: Done
                    release: V1
                    folder: real
            """;

        var board = YamlIndex.Parse(yaml);

        Assert.Equal("V1", Assert.Single(board.Epics).Release);
    }

    [Fact]
    public void Parse_orders_by_roadmap_position_not_by_string_or_story_order()
    {
        // V10 sorts before V2 as a string, and is named first here — a fixture that only an actual
        // roadmap-index lookup, not ordinal comparison or "first story wins", can get right.
        var yaml = """
            project: Acme App
            roadmap: [V2, V10]
            epics:
              - number: 1
                title: Core Application
                stories:
                  - code: US-03
                    title: Later
                    status: Done
                    release: V10
                    folder: later
                  - code: US-04
                    title: Earlier
                    status: Done
                    release: V2
                    folder: earlier
            """;

        var board = YamlIndex.Parse(yaml);

        Assert.Equal("V2", Assert.Single(board.Epics).Release);
    }

    [Fact]
    public void Parse_keeps_an_epic_release_that_is_already_set()
    {
        var yaml = """
            project: Acme App
            roadmap: [V1, V2]
            epics:
              - number: 1
                release: V2
                title: Core Application
                stories:
                  - code: US-03
                    title: Local Setup
                    status: Done
                    release: V1
                    folder: local-setup
            """;

        var board = YamlIndex.Parse(yaml);

        Assert.Equal("V2", Assert.Single(board.Epics).Release);
    }

    [Fact]
    public void Parse_does_not_flag_a_file_that_is_already_in_the_new_shape()
    {
        var board = YamlIndex.Parse(Yaml);

        Assert.False(board.Migrated);
    }

    [Fact]
    public void Parse_is_idempotent_across_a_write()
    {
        var once = YamlIndex.Parse(LegacyYaml);
        var text = YamlIndex.Write(once);

        var twice = YamlIndex.Parse(text);

        Assert.False(twice.Migrated);
        Assert.Equal("V1", Assert.Single(twice.Epics).Release);
        Assert.DoesNotContain("release: V1.5", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("epics:")]
    [InlineData("epics:\n- number: 1\n  stories:\n")]
    [InlineData("epics:\n- number: 1\n  stories:\n  - code: X\n    release: \"\"\n")]
    [InlineData("roadmap:\nepics:\n- number: 1\n  stories:\n  - code: X\n    release: V1\n")]
    [InlineData("epics:\n- \n")]
    [InlineData("epics:\n- number: 1\n  stories:\n  -\n")]
    [InlineData("epics:\n- \n- number: 2\n  stories:\n  - code: X\n    release: V1\n")]
    public void Parse_never_throws_on_a_file_it_cannot_make_sense_of(string yaml)
    {
        var board = YamlIndex.Parse(yaml);

        Assert.NotNull(board);
    }
}
