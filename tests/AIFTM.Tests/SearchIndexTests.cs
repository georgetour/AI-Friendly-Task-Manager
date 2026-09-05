using AIFTM.Api.Backlog;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// What search can find. The browser filters this list in memory, so anything missing here is
/// unfindable no matter what is typed — which makes the index's contents the whole feature.
/// </summary>
public class SearchIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-search-" + Guid.NewGuid().ToString("N"));
    private readonly BacklogService _svc;

    private string Skills => Path.Combine(_root, "skills");
    private string Backlog => Path.Combine(_root, "BACKLOG.yaml");

    public SearchIndexTests()
    {
        Directory.CreateDirectory(Path.Combine(Skills, "board"));
        File.WriteAllText(Backlog, """
            project: Test
            roadmap: [V1]
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: Backlog Board
                    status: Done
                    release: V1
                    folder: board
                  - code: US-02
                    title: Missing Folder
                    status: Not Yet Started
                    folder: gone
            """);

        File.WriteAllText(Path.Combine(Skills, "board", "SKILL.md"), """
            ---
            name: board
            ---

            # Backlog Board

            ## Description

            Draws epics and stories from the index.

            ## Acceptance Criteria

            The board loads without opening a story folder.
            """);

        File.WriteAllText(Path.Combine(Skills, "board", "tasks.yaml"),
            "- text: Render the epic header\n  done: false\n");
        File.WriteAllText(Path.Combine(Skills, "board", "test-cases.yaml"),
            "- text: A status click writes through\n  status: Passed\n");

        _svc = new BacklogService(() => Backlog, () => Skills);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Every_epic_is_listed_in_its_own_right()
    {
        // Otherwise an epic is only reachable through a story that happens to be inside it, and an
        // epic with no stories yet cannot be found at all — which is when you most want it.
        var index = SearchIndex.Build(_svc);

        var epic = Assert.Single(index, e => e.Kind == "epic");
        Assert.Equal("Tooling", epic.Title);
        Assert.Equal(2, epic.StoryCount);
        Assert.False(string.IsNullOrWhiteSpace(epic.EpicSlug));

        // And it comes before the stories in it, so results read in the order the board does.
        Assert.Equal(0, index.ToList().FindIndex(e => e.Kind == "epic"));
    }

    [Fact]
    public void Every_story_is_listed_with_the_slugs_that_open_it()
    {
        var index = SearchIndex.Build(_svc);

        var story = Assert.Single(index, e => e.Code == "US-01");
        Assert.Equal("story", story.Kind);
        Assert.Equal("Backlog Board", story.Title);
        Assert.Equal("Tooling", story.EpicTitle);
        Assert.Equal("Done", story.Status);
        // The link comes from the index rather than being rebuilt in JavaScript, which is the same
        // reason slugs travel in the board JSON.
        Assert.False(string.IsNullOrWhiteSpace(story.EpicSlug));
        Assert.False(string.IsNullOrWhiteSpace(story.StorySlug));
    }

    [Fact]
    public void Tasks_and_test_cases_are_searchable_and_say_which_they_are()
    {
        var story = Assert.Single(SearchIndex.Build(_svc), e => e.Code == "US-01");

        Assert.Contains(story.Sections, s => s.Kind == "task" && s.Text.Contains("epic header"));
        Assert.Contains(story.Sections, s => s.Kind == "test case" && s.Text.Contains("writes through"));
    }

    [Fact]
    public void The_description_arrives_as_paragraphs_without_its_frontmatter()
    {
        var story = Assert.Single(SearchIndex.Build(_svc), e => e.Code == "US-01");
        var prose = story.Sections.Where(s => s.Kind == "description").ToList();

        Assert.Contains(prose, s => s.Text.Contains("Draws epics and stories"));

        // Paragraphs, not the whole file: an excerpt is built around the hit, and a single blob
        // would put the match 400 characters into a wall of text.
        Assert.All(prose, s => Assert.DoesNotContain("\n", s.Text));

        // name: board is machine metadata. Nobody searches for it, and it would match every story.
        Assert.DoesNotContain(prose, s => s.Text.Contains("name: board"));

        // And no headings: "# Backlog Board" is the title shown one line above the excerpt, and
        // "## Description" is a word every story in the backlog contains.
        Assert.DoesNotContain(prose, s => s.Text.StartsWith('#'));
    }

    [Fact]
    public void An_index_that_will_not_parse_fails_rather_than_returning_nothing()
    {
        // What the endpoint's 422 is for. Silently returning an empty list would say "nothing
        // matched" to every search, which is a lie about a file that has a fixable typo in it.
        File.WriteAllText(Backlog, "project: Test\nepics:\n  - number: 0\n   title: Bad indent\n");

        Assert.ThrowsAny<Exception>(() => SearchIndex.Build(_svc));
    }

    [Fact]
    public void A_story_whose_folder_is_missing_is_still_findable_by_title()
    {
        // This is exactly when you need to find it — to go and fix the folder Sync is complaining
        // about. One unreadable story must not take the rest of the index with it.
        var index = SearchIndex.Build(_svc);

        var broken = Assert.Single(index, e => e.Code == "US-02");
        Assert.Equal("Missing Folder", broken.Title);
        Assert.Empty(broken.Sections);
        Assert.Contains(index, e => e.Code == "US-01" && e.Sections.Count > 0);
    }
}
