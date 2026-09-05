using AIFTM.Api.Backlog.Legacy;

namespace AIFTM.Tests;

/// <summary>
/// Parses the bundled sample BACKLOG.md fixture — a small but structurally complete file covering
/// both test-case table shapes, the "### Validation" heading, an epic with zero stories, and all 7
/// status values. Structural counts are stable since the fixture is static, so they're asserted
/// directly.
/// </summary>
public class RealBacklogTests
{
    private static readonly string[] Vocabulary =
    {
        "Not Yet Started", "Under Review", "Refined", "In Progress", "Vendor Test", "Done", "On Hold"
    };

    private static string SampleBacklog() => File.ReadAllText(TestBacklogLocator.Resolve());

    [Fact]
    public void Finds_all_epics_with_expected_story_counts()
    {
        var board = MarkdownBacklogParser.Parse(SampleBacklog());

        var byNum = board.Epics.ToDictionary(e => e.Number, e => e.Stories.Count);
        Assert.Equal(2, byNum[0]);  // Epic 0 — Tracker Tooling (US-01, US-02)
        Assert.Equal(3, byNum[1]);  // Epic 1 — Sample Application (US-03 … US-05)
        Assert.Equal(2, byNum[2]);  // Epic 2 — Automation (US-06, US-07)
        Assert.Equal(0, byNum[3]);  // Epic 3 — Future Work (no stories yet)
        Assert.Equal(2, byNum[4]);  // Epic 4 — Scaling (US-08, US-09)
    }

    [Fact]
    public void Total_of_9_stories_all_well_formed()
    {
        var stories = MarkdownBacklogParser.Parse(SampleBacklog()).Epics.SelectMany(e => e.Stories).ToList();

        Assert.Equal(9, stories.Count);
        Assert.All(stories, s =>
        {
            Assert.Matches(@"^US-\d+$", s.Code);
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.Contains(s.StatusLabel, Vocabulary);
        });
    }

    [Fact]
    public void All_7_statuses_are_represented()
    {
        var stories = MarkdownBacklogParser.Parse(SampleBacklog()).Epics.SelectMany(e => e.Stories).ToList();

        Assert.All(Vocabulary, label => Assert.Contains(stories, s => s.StatusLabel == label));
    }

    [Fact]
    public void Parses_both_test_case_table_shapes_and_validation_heading()
    {
        var stories = MarkdownBacklogParser.Parse(SampleBacklog()).Epics.SelectMany(e => e.Stories)
            .ToDictionary(s => s.Code);

        // All three shapes must yield test cases with text and a recognised status. The old
        // identifiers are gone: nothing addresses a test case by id any more.
        Assert.NotEmpty(stories["US-01"].TestCases);   // "| ID | Description | Status | Notes |"
        Assert.NotEmpty(stories["US-03"].TestCases);   // "| TC | Verifies | Scenario | Status |"
        Assert.NotEmpty(stories["US-04"].TestCases);   // "### Validation" instead of "### Test Cases"

        Assert.All(stories.Values.SelectMany(s => s.TestCases),
            tc => Assert.False(string.IsNullOrWhiteSpace(tc.Text)));
    }
}
