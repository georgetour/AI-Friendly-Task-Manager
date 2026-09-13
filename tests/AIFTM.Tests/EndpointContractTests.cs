using AIFTM.Api.Backlog;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// Every rule the API enforces regardless of what the browser did. The confirmation dialogs, the
/// disabled buttons and the client-side checks are all courtesies — anyone can send these requests
/// with curl, so the rules have to live here.
/// </summary>
public class EndpointContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-ep-" + Guid.NewGuid().ToString("N"));
    private readonly BacklogService _svc;

    private string Skills => Path.Combine(_root, "skills");

    public EndpointContractTests()
    {
        Directory.CreateDirectory(Path.Combine(Skills, "board"));
        File.WriteAllText(Path.Combine(_root, "BACKLOG.yaml"), """
            project: Test
            roadmap: [V1]
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: Board
                    status: Done
                    release: V1
                    folder: board
            """);

        _svc = new BacklogService(() => Path.Combine(_root, "BACKLOG.yaml"), () => Skills);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // ---------- the current epic ----------

    [Fact]
    public void Setting_the_current_epic_writes_it_and_refuses_an_epic_that_is_not_there()
    {
        Assert.Equal(0, _svc.SetCurrentEpic(0).CurrentEpic);
        Assert.Equal(0, _svc.GetBoard().CurrentEpic);

        // Reachable with curl whatever the UI offers, so the number is checked here too.
        Assert.Throws<BacklogValidationException>(() => _svc.SetCurrentEpic(99));
        Assert.Equal(0, _svc.GetBoard().CurrentEpic);
    }

    // ---------- status vocabulary ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Finished")]
    [InlineData("done")]                                  // right word, wrong case
    [InlineData("<script>alert(1)</script>")]
    public void Status_rejects_anything_outside_the_vocabulary(string status)
    {
        Assert.Throws<BacklogValidationException>(() => _svc.SetStoryStatus("US-01", status));
    }

    [Theory]
    [InlineData("Maybe")]
    [InlineData("passed")]
    [InlineData("")]
    public void Test_case_status_rejects_anything_outside_its_vocabulary(string status)
    {
        Assert.Throws<BacklogValidationException>(
            () => _svc.SetTestCases("US-01", new[] { new TestCase("Check it", status) }));
    }

    // ---------- size bounds ----------

    [Fact]
    public void Task_text_has_an_upper_bound()
    {
        Assert.Throws<BacklogValidationException>(
            () => _svc.SetTasks("US-01", new[] { new TaskItem(new string('x', 501), false) }));
    }

    [Fact]
    public void A_story_cannot_hold_an_unbounded_number_of_tasks()
    {
        // The UI cannot produce this, so a request that does is a bug or an attack. Before the cap,
        // one call wrote a 168 KB tasks.yaml.
        var many = Enumerable.Range(0, 201).Select(i => new TaskItem("Task " + i, false)).ToList();

        Assert.Throws<BacklogValidationException>(() => _svc.SetTasks("US-01", many));
    }

    [Fact]
    public void The_cap_is_generous_enough_for_real_work()
    {
        var many = Enumerable.Range(0, 200).Select(i => new TaskItem("Task " + i, false)).ToList();

        _svc.SetTasks("US-01", many);

        Assert.Equal(200, _svc.GetStory("US-01").Tasks.Count);
    }

    [Fact]
    public void A_story_cannot_hold_an_unbounded_number_of_test_cases()
    {
        var many = Enumerable.Range(0, 201).Select(i => new TestCase("Check " + i, "Not Run")).ToList();

        Assert.Throws<BacklogValidationException>(() => _svc.SetTestCases("US-01", many));
    }

    // ---------- deliberate emptiness is allowed ----------

    [Fact]
    public void Tasks_may_be_emptied_deliberately()
    {
        _svc.SetTasks("US-01", new[] { new TaskItem("One", false) });

        _svc.SetTasks("US-01", Array.Empty<TaskItem>());

        Assert.Empty(_svc.GetStory("US-01").Tasks);
    }

    // ---------- text is data, never markup ----------

    [Fact]
    public void Task_text_is_stored_verbatim_and_never_interpreted()
    {
        _svc.SetTasks("US-01", new[] { new TaskItem("<script>alert(1)</script>", false) });

        Assert.Equal("<script>alert(1)</script>", _svc.GetStory("US-01").Tasks[0].Text);
    }

    [Fact]
    public void Text_that_looks_like_yaml_cannot_change_the_files_structure()
    {
        _svc.SetTasks("US-01", new[]
        {
            new TaskItem("done: true\n- text: injected", false),
            new TaskItem("- leading dash: and a colon", true),
        });

        var tasks = _svc.GetStory("US-01").Tasks;
        Assert.Equal(2, tasks.Count);
        Assert.Equal("done: true\n- text: injected", tasks[0].Text);
        Assert.False(tasks[0].Done);
    }

    // ---------- unknown targets ----------

    [Fact]
    public void An_unknown_story_is_rejected_everywhere_it_can_be_named()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.GetStory("US-99"));
        Assert.Throws<BacklogValidationException>(() => _svc.SetStoryStatus("US-99", "Done"));
        Assert.Throws<BacklogValidationException>(() => _svc.SetTasks("US-99", Array.Empty<TaskItem>()));
        Assert.Throws<BacklogValidationException>(() => _svc.SetTestCases("US-99", Array.Empty<TestCase>()));
        Assert.Throws<BacklogValidationException>(() => _svc.DeleteStory("US-99"));
    }

    [Fact]
    public void An_unknown_epic_is_rejected_everywhere_it_can_be_named()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.RenameEpic(99, "Nope"));
        Assert.Throws<BacklogValidationException>(() => _svc.DeleteEpic(99));
        Assert.Throws<BacklogValidationException>(() => _svc.AddStory(99, "US-02", "Nope"));
    }

    // ---------- editing a story ----------

    [Fact]
    public void EditStory_changes_the_title()
    {
        _svc.EditStory("US-01", "Renamed Board");

        var story = _svc.GetBoard().Epics[0].Stories.Single();
        Assert.Equal("Renamed Board", story.Title);
    }

    [Fact]
    public void EditStory_leaves_the_folder_alone_so_nothing_is_orphaned()
    {
        // The folder is recorded in the index, so it does not have to match the title. Moving a
        // directory someone may have open is a worse failure than a name that has drifted.
        _svc.SetTasks("US-01", new[] { new TaskItem("Keep me", true) });

        _svc.EditStory("US-01", "A Completely Different Title");

        var story = _svc.GetBoard().Epics[0].Stories.Single();
        Assert.Equal("board", story.Folder);
        Assert.Single(_svc.GetStory("US-01").Tasks);
        Assert.True(_svc.Validate().Ok);
    }

    [Fact]
    public void EditStory_changes_the_url_because_the_slug_follows_the_title()
    {
        _svc.EditStory("US-01", "Checkout and Payment");

        Assert.Equal("checkout-and-payment", _svc.GetBoard().Epics[0].Stories.Single().Slug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EditStory_rejects_an_empty_title(string title)
    {
        Assert.Throws<BacklogValidationException>(() => _svc.EditStory("US-01", title));
    }

    [Fact]
    public void EditStory_rejects_an_over_long_title()
    {
        Assert.Throws<BacklogValidationException>(
            () => _svc.EditStory("US-01", new string('x', 121)));
    }

    [Fact]
    public void EditStory_rejects_an_unknown_story()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.EditStory("US-99", "Nope"));
    }

    // ---------- description written at creation ----------

    [Fact]
    public void AddStory_writes_the_description_into_the_new_SKILL_md()
    {
        _svc.AddStory(0, "US-02", "Export to CSV",
            "As an analyst, I want to export the report, so that I can work on it offline.");

        var skill = File.ReadAllText(Path.Combine(Skills, "export-to-csv", "SKILL.md"));
        Assert.Contains("As an analyst, I want to export the report", skill);
        Assert.Contains("# US-02 · Export to CSV", skill);
    }

    [Fact]
    public void AddStory_keeps_the_rest_of_the_scaffold_around_the_description()
    {
        _svc.AddStory(0, "US-02", "Export to CSV", "Some prose.");

        var skill = File.ReadAllText(Path.Combine(Skills, "export-to-csv", "SKILL.md"));
        Assert.Contains("## Description", skill);
        Assert.Contains("## Tasks", skill);
        Assert.Contains("## Acceptance Criteria", skill);
        // The placeholder it replaced must be gone, or the file reads as unfinished.
        Assert.DoesNotContain("[Plain, precise description", skill);
    }

    [Fact]
    public void AddStory_without_a_description_still_gets_the_template()
    {
        _svc.AddStory(0, "US-02", "Export to CSV", null);

        var skill = File.ReadAllText(Path.Combine(Skills, "export-to-csv", "SKILL.md"));
        Assert.Contains("## Description", skill);
        Assert.Contains("## Acceptance Criteria", skill);
    }

    [Fact]
    public void A_description_containing_markup_is_stored_as_written()
    {
        _svc.AddStory(0, "US-02", "Export to CSV", "<script>alert(1)</script> and **bold**");

        var skill = File.ReadAllText(Path.Combine(Skills, "export-to-csv", "SKILL.md"));
        Assert.Contains("<script>alert(1)</script>", skill);   // it is a text file; escaping happens on render
    }

    // ---------- deleting is not gated by the dialog ----------

    [Fact]
    public void Deleting_works_without_any_confirmation_because_the_dialog_is_only_UI()
    {
        // Worth stating outright: the confirm dialog protects against a slip, not against a request.
        // What the server owes is that the target exists and that nothing else is touched.
        _svc.DeleteStory("US-01");

        Assert.Empty(_svc.GetBoard().Epics[0].Stories);
        Assert.False(Directory.Exists(Path.Combine(Skills, "board")));
    }
}
