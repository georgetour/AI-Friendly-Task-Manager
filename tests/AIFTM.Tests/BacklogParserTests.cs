using AIFTM.Api.Backlog.Legacy;

namespace AIFTM.Tests;

public class BacklogParserTests
{
    // A representative slice: one epic, one story with a status line (emoji+label+skill+release),
    // a Tasks table (done + not-done), and a Test Cases table (Not Run + Passed).
    private const string Sample =
        "# Epic 0: Local Backlog Tracker\n" +
        "\n" +
        "## US-21 · Backlog Board — Read & Render\n" +
        "\n" +
        "> **Status**: 🔍 Under Review · **Skill**: `skills/backlog-tracker/SKILL.md` · **Release**: V0.1\n" +
        "> **Depends on**: nothing\n" +
        "\n" +
        "### Tasks\n" +
        "\n" +
        "| # | Task | ✓ |\n" +
        "|---|------|---|\n" +
        "| 21.0 | Create detailed design | ✅ |\n" +
        "| 21.1 | Backlog parser | ⬜ |\n" +
        "\n" +
        "### Test Cases\n" +
        "\n" +
        "| ID | Description | Status | Notes |\n" +
        "|----|-------------|--------|-------|\n" +
        "| TC-21-01 | Board shows every epic | ⬜ Not Run | |\n" +
        "| TC-21-02 | Badge matches status | ✅ Passed | |\n";

    [Fact]
    public void Parses_epic_number_and_title()
    {
        var board = MarkdownBacklogParser.Parse(Sample);

        var epic = Assert.Single(board.Epics);
        Assert.Equal(0, epic.Number);
        Assert.Equal("Local Backlog Tracker", epic.Title);
    }

    [Fact]
    public void Parses_story_heading_status_skill_and_release()
    {
        var story = Assert.Single(MarkdownBacklogParser.Parse(Sample).Epics[0].Stories);

        Assert.Equal("US-21", story.Code);
        Assert.Equal("Backlog Board — Read & Render", story.Title);
        Assert.Equal("Under Review", story.StatusLabel);
        Assert.Equal("V0.1", story.Release);
        Assert.Equal("skills/backlog-tracker/SKILL.md", story.SkillPath);
    }

    [Fact]
    public void Parses_tasks_with_done_state()
    {
        var story = MarkdownBacklogParser.Parse(Sample).Epics[0].Stories[0];

        Assert.Equal(2, story.Tasks.Count);
        Assert.Equal("Create detailed design", story.Tasks[0].Text);
        Assert.True(story.Tasks[0].Done);
        Assert.False(story.Tasks[1].Done);
    }

    [Fact]
    public void Parses_test_cases_with_status_by_header_column()
    {
        var story = MarkdownBacklogParser.Parse(Sample).Epics[0].Stories[0];

        Assert.Equal(2, story.TestCases.Count);
        Assert.Equal("Not Run", story.TestCases[0].StatusLabel);
        Assert.Equal("Passed", story.TestCases[1].StatusLabel);
    }

    [Fact]
    public void The_emoji_is_dropped_from_the_status_because_it_was_only_presentation()
    {
        var story = MarkdownBacklogParser.Parse(Sample).Epics[0].Stories[0];

        Assert.DoesNotContain("⬜", story.StatusLabel);
        Assert.DoesNotContain("✅", story.TestCases[1].StatusLabel);
    }
}
