using AIFTM.Api.Backlog;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

public class BacklogServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-svc-" + Guid.NewGuid().ToString("N"));
    private readonly BacklogService _svc;

    private string Skills => Path.Combine(_root, "skills");
    private string Backlog => Path.Combine(_root, "BACKLOG.yaml");

    public BacklogServiceTests()
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
                    title: Board
                    status: Not Yet Started
                    release: V1
                    folder: board
            """);

        _svc = new BacklogService(() => Backlog, () => Skills);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Reads_whatever_path_the_resolver_currently_returns()
    {
        // Configure can repoint the tracker with no restart, so nothing may be cached.
        var second = Path.Combine(_root, "other.yaml");
        File.WriteAllText(second, "project: Other\nepics: []\n");

        var current = Backlog;
        var svc = new BacklogService(() => current, () => Skills);
        Assert.Equal("Test", svc.GetBoard().Project);

        current = second;
        Assert.Equal("Other", svc.GetBoard().Project);
    }

    [Fact]
    public void GetBoard_returns_the_index()
    {
        var board = _svc.GetBoard();

        var story = board.Epics.Single().Stories.Single();
        Assert.Equal("US-01", story.Code);
        Assert.Equal("board", story.Folder);
        Assert.Equal("Not Yet Started", story.Status);
    }

    [Fact]
    public void SetStoryStatus_writes_and_reads_back()
    {
        _svc.SetStoryStatus("US-01", "Done");

        Assert.Equal("Done", _svc.GetBoard().Epics[0].Stories[0].Status);
    }

    [Fact]
    public void SetStoryStatus_rejects_an_unknown_status()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.SetStoryStatus("US-01", "Finished"));
    }

    [Fact]
    public void SetStoryStatus_rejects_an_unknown_story()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.SetStoryStatus("US-99", "Done"));
    }

    [Fact]
    public void SetTasks_replaces_the_whole_list()
    {
        _svc.SetTasks("US-01", new[] { new TaskItem("One", false), new TaskItem("Two", true) });

        _svc.SetTasks("US-01", new[] { new TaskItem("Only", true) });

        var task = Assert.Single(_svc.GetStory("US-01").Tasks);
        Assert.Equal("Only", task.Text);
        Assert.True(task.Done);
    }

    [Fact]
    public void SetTasks_trims_text()
    {
        _svc.SetTasks("US-01", new[] { new TaskItem("  padded  ", false) });

        Assert.Equal("padded", _svc.GetStory("US-01").Tasks[0].Text);
    }

    [Fact]
    public void SetTasks_rejects_empty_text()
    {
        Assert.Throws<BacklogValidationException>(
            () => _svc.SetTasks("US-01", new[] { new TaskItem("   ", false) }));
    }

    [Fact]
    public void SetTestCases_rejects_an_unknown_status()
    {
        Assert.Throws<BacklogValidationException>(
            () => _svc.SetTestCases("US-01", new[] { new TestCase("Check", "Maybe") }));
    }

    [Fact]
    public void SetTestCases_round_trips()
    {
        _svc.SetTestCases("US-01", new[] { new TestCase("Board lists every story", "Passed") });

        var tc = Assert.Single(_svc.GetStory("US-01").TestCases);
        Assert.Equal("Passed", tc.Status);
    }

    [Fact]
    public void A_status_change_does_not_disturb_the_story_files()
    {
        // The index and the story folder are written independently — that is the whole point of
        // splitting them, and it means a status click can never clobber task state.
        _svc.SetTasks("US-01", new[] { new TaskItem("Keep me", true) });

        _svc.SetStoryStatus("US-01", "Done");

        Assert.Single(_svc.GetStory("US-01").Tasks);
    }

    [Fact]
    public void AddStory_creates_a_slug_folder_with_a_SKILL_md()
    {
        _svc.AddStory(0, "US-02", "Checkout and Payment", "V1");

        var story = _svc.GetBoard().Epics[0].Stories.Single(s => s.Code == "US-02");
        Assert.Equal("checkout-and-payment", story.Folder);
        Assert.True(File.Exists(Path.Combine(Skills, "checkout-and-payment", "SKILL.md")));
    }

    [Fact]
    public void AddStory_gives_a_duplicate_title_its_own_folder()
    {
        _svc.AddStory(0, "US-02", "Board", "V1");

        var story = _svc.GetBoard().Epics[0].Stories.Single(s => s.Code == "US-02");
        Assert.Equal("board-2", story.Folder);
    }

    [Fact]
    public void AddStory_rejects_a_duplicate_code()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.AddStory(0, "US-01", "Another", "V1"));
    }

    [Fact]
    public void AddStory_rejects_an_unknown_epic()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.AddStory(9, "US-02", "Nope", "V1"));
    }

    [Fact]
    public void DeleteStory_removes_the_entry_and_the_folder()
    {
        _svc.DeleteStory("US-01");

        Assert.Empty(_svc.GetBoard().Epics[0].Stories);
        Assert.False(Directory.Exists(Path.Combine(Skills, "board")));
    }

    [Fact]
    public void DeleteEpic_takes_its_stories_folders_with_it()
    {
        _svc.DeleteEpic(0);

        Assert.Empty(_svc.GetBoard().Epics);
        Assert.False(Directory.Exists(Path.Combine(Skills, "board")));
    }

    [Fact]
    public void AddEpic_appends_and_rejects_a_duplicate_number()
    {
        _svc.AddEpic(1, "Core Application");
        Assert.Equal(2, _svc.GetBoard().Epics.Count);

        Assert.Throws<BacklogValidationException>(() => _svc.AddEpic(1, "Again"));
    }

    [Fact]
    public void RenameEpic_changes_only_the_title()
    {
        _svc.RenameEpic(0, "Delivery");

        var epic = _svc.GetBoard().Epics.Single();
        Assert.Equal("Delivery", epic.Title);
        Assert.Single(epic.Stories);
    }

    [Fact]
    public void RenameEpic_rejects_an_unknown_epic()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.RenameEpic(9, "Nope"));
    }

    [Fact]
    public void Validate_reports_a_story_whose_folder_is_gone()
    {
        Directory.Delete(Path.Combine(Skills, "board"), true);

        var report = _svc.Validate();

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "error" && i.Message.Contains("board"));
    }

    [Fact]
    public void Validate_is_clean_for_a_healthy_backlog()
    {
        Assert.True(_svc.Validate().Ok);
    }
}
