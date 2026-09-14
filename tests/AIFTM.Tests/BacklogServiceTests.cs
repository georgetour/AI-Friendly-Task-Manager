using AIFTM.Api.Backlog;
using AIFTM.Api.Services;
using YamlDotNet.RepresentationModel;

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
    public void A_change_is_written_to_the_file_it_was_read_from_even_if_the_project_switches_mid_way()
    {
        // Switching project changes what the resolver answers, under another service's lock. Asking
        // it again to save would write this project's board over the other project's file.
        var other = Path.Combine(_root, "other.yaml");
        const string otherText = "project: Other\nepics: []\n";
        File.WriteAllText(other, otherText);

        var calls = 0;
        var svc = new BacklogService(() => calls++ == 0 ? Backlog : other, () => Skills);

        svc.SetStoryStatus("US-01", "Done");

        Assert.Equal(otherText, File.ReadAllText(other));
        Assert.Equal("Done", YamlIndex.Parse(File.ReadAllText(Backlog)).Epics[0].Stories[0].Status);
    }

    [Fact]
    public void Opening_an_old_file_converts_the_file_it_read_even_if_the_project_switches_mid_way()
    {
        var other = Path.Combine(_root, "other.yaml");
        const string otherText = "project: Other\nepics: []\n";
        File.WriteAllText(other, otherText);

        var calls = 0;
        var svc = new BacklogService(() => calls++ == 0 ? Backlog : other, () => Skills);

        svc.GetBoard();

        Assert.Equal(otherText, File.ReadAllText(other));
        Assert.False(YamlIndex.Parse(File.ReadAllText(Backlog)).Migrated);
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
        _svc.AddStory(0, "US-02", "Checkout and Payment");

        var story = _svc.GetBoard().Epics[0].Stories.Single(s => s.Code == "US-02");
        Assert.Equal("checkout-and-payment", story.Folder);
        Assert.True(File.Exists(Path.Combine(Skills, "checkout-and-payment", "SKILL.md")));
    }

    [Fact]
    public void AddStory_gives_a_duplicate_title_its_own_folder()
    {
        _svc.AddStory(0, "US-02", "Board");

        var story = _svc.GetBoard().Epics[0].Stories.Single(s => s.Code == "US-02");
        Assert.Equal("board-2", story.Folder);
    }

    [Fact]
    public void AddStory_rejects_a_duplicate_code()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.AddStory(0, "US-01", "Another"));
    }

    [Fact]
    public void AddStory_rejects_an_unknown_epic()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.AddStory(9, "US-02", "Nope"));
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
        _svc.AddEpic(1, "Core Application", null, null);
        Assert.Equal(2, _svc.GetBoard().Epics.Count);

        Assert.Throws<BacklogValidationException>(() => _svc.AddEpic(1, "Again", null, null));
    }

    [Fact]
    public void AddEpic_rejects_an_over_long_version_or_release()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.AddEpic(1, "Core", new string('x', 21), null));
        Assert.Throws<BacklogValidationException>(() => _svc.AddEpic(1, "Core", null, new string('x', 21)));
    }

    [Fact]
    public void EditEpic_sets_title_version_and_release_and_leaves_the_rest_alone()
    {
        _svc.EditEpic(0, "Delivery", "0.2.0", "V2");

        var epic = _svc.GetBoard().Epics.Single();
        Assert.Equal(0, epic.Number);
        Assert.Equal("Delivery", epic.Title);
        Assert.Equal("0.2.0", epic.Version);
        Assert.Equal("V2", epic.Release);
        Assert.Single(epic.Stories);
        Assert.Equal("US-01", epic.Stories[0].Code);
    }

    [Fact]
    public void EditEpic_moves_an_epic_from_one_release_to_a_different_one()
    {
        File.WriteAllText(Backlog, """
            project: Test
            roadmap: [V1, V2]
            epics:
              - number: 0
                title: Tooling
                release: V1
                stories:
                  - code: US-01
                    title: Board
                    status: Not Yet Started
                    folder: board
            """);

        _svc.EditEpic(0, "Tooling", null, "V2");

        var epic = _svc.GetBoard().Epics.Single();
        Assert.Equal(0, epic.Number);
        Assert.Equal("V2", epic.Release);
        Assert.Single(epic.Stories);
        Assert.Equal("US-01", epic.Stories[0].Code);

        var yaml = File.ReadAllText(Backlog);
        Assert.Contains("release: V2", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EditEpic_with_blank_version_and_release_clears_both_from_the_file()
    {
        _svc.EditEpic(0, "Delivery", "0.2.0", "V2");
        _svc.EditEpic(0, "Delivery", "  ", " ");

        var epic = _svc.GetBoard().Epics.Single();
        Assert.Equal("", epic.Version);
        Assert.Equal("", epic.Release);

        var yaml = File.ReadAllText(Backlog);
        Assert.DoesNotContain("version:", yaml);
        Assert.DoesNotContain("release:", yaml);
    }

    [Fact]
    public void EditEpic_rejects_an_over_long_version_or_release()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.EditEpic(0, "Delivery", new string('x', 21), null));
        Assert.Throws<BacklogValidationException>(() => _svc.EditEpic(0, "Delivery", null, new string('x', 21)));
    }

    [Fact]
    public void EditEpic_rejects_an_unknown_epic()
    {
        var ex = Assert.Throws<BacklogValidationException>(() => _svc.EditEpic(9, "Nope", null, null));
        Assert.Equal("There is no epic 9.", ex.Message);
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

    [Fact]
    public void GetBoard_converts_an_old_file_once_and_leaves_it_alone_after()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "BACKLOG.yaml");
        File.WriteAllText(path, """
            project: Acme App
            roadmap: [V1, V1.5]
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
                title: Email
                status: Not Yet Started
                release: V1.5
                folder: email
            """);

        var svc = new BacklogService(() => path, () => Path.Combine(dir, "skills"));

        var board = svc.GetBoard();
        var afterFirst = File.ReadAllText(path);

        Assert.Equal("V1", Assert.Single(board.Epics).Release);
        Assert.Contains("release: V1", afterFirst, StringComparison.Ordinal);
        Assert.DoesNotContain("release: V1.5", afterFirst, StringComparison.Ordinal);

        svc.GetBoard();

        Assert.Equal(afterFirst, File.ReadAllText(path));
    }

    private (BacklogService Svc, string Path) OwnerShaped(bool converted = false)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(dir, "BACKLOG.yaml");
        File.WriteAllText(path, converted ? OwnerShapedBacklog.ConvertedYaml : OwnerShapedBacklog.Yaml);
        return (new BacklogService(() => path, () => Path.Combine(dir, "skills")), path);
    }

    [Fact]
    public void Opening_an_old_file_converts_it_without_losing_any_key_the_index_does_not_model()
    {
        var (svc, path) = OwnerShaped();
        var before = OwnerShapedBacklog.StoryExtras(OwnerShapedBacklog.Yaml);

        svc.GetBoard();
        var written = File.ReadAllText(path);

        Assert.NotEqual(OwnerShapedBacklog.Yaml, written);
        Assert.DoesNotContain(OwnerShapedBacklog.Stories(written),
            s => s.Children.ContainsKey(new YamlScalarNode("release")));
        OwnerShapedBacklog.AssertStoryExtrasEqual(before, OwnerShapedBacklog.StoryExtras(written));
        Assert.Equal(new[] { "0.1.0", "0.2.0", "0.3.0" },
            OwnerShapedBacklog.Epics(written).Select(e => e.Children[new YamlScalarNode("version")].ToString()));
    }

    [Fact]
    public void A_status_change_keeps_every_unmodelled_key()
    {
        var (svc, path) = OwnerShaped(converted: true);
        var before = OwnerShapedBacklog.StoryExtras(File.ReadAllText(path));
        Assert.NotEmpty(before["US-22"]);

        svc.SetStoryStatus("US-22", "In Progress");

        Assert.Equal("In Progress", svc.GetBoard().Epics[1].Stories[0].Status);
        OwnerShapedBacklog.AssertStoryExtrasEqual(before, OwnerShapedBacklog.StoryExtras(File.ReadAllText(path)));
    }

    [Fact]
    public void Editing_an_epic_keeps_the_unmodelled_keys_on_its_stories()
    {
        var (svc, path) = OwnerShaped(converted: true);
        var before = OwnerShapedBacklog.StoryExtras(File.ReadAllText(path));
        Assert.NotEmpty(before["US-22"]);

        svc.EditEpic(2, "Delivery", "0.2.1", "V2");

        OwnerShapedBacklog.AssertStoryExtrasEqual(before, OwnerShapedBacklog.StoryExtras(File.ReadAllText(path)));
    }

    [Fact]
    public void Every_change_to_the_index_keeps_the_unmodelled_keys_it_does_not_remove()
    {
        var changes = new (string Name, Action<BacklogService> Apply, string[] Removed)[]
        {
            ("current epic", s => s.SetCurrentEpic(2), []),
            ("add epic", s => s.AddEpic(9, "Later", "0.9.0", "V3"), []),
            ("edit story", s => s.EditStory("US-18", "CD Backend Pipeline"), []),
            ("add story", s => s.AddStory(1, "US-40", "Audit Log"), []),
            ("tasks", s => s.SetTasks("US-01", [new TaskItem("One", true)]), []),
            ("test cases", s => s.SetTestCases("US-01", [new TestCase("Check", "Passed")]), []),
            ("delete story", s => s.DeleteStory("US-22"), ["US-22"]),
            ("delete epic", s => s.DeleteEpic(3), ["US-30"]),
        };

        foreach (var (name, apply, removed) in changes)
        {
            var (svc, path) = OwnerShaped(converted: true);
            var expected = OwnerShapedBacklog.StoryExtras(File.ReadAllText(path));
            Assert.NotEmpty(expected.Values.SelectMany(v => v.Keys));
            foreach (var code in removed) expected.Remove(code);

            apply(svc);

            var actual = OwnerShapedBacklog.StoryExtras(File.ReadAllText(path))
                .Where(kv => kv.Value.Count > 0 || expected.ContainsKey(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            try { OwnerShapedBacklog.AssertStoryExtrasEqual(expected, actual); }
            catch (Exception e) { throw new InvalidOperationException($"\"{name}\" lost an unmodelled key.", e); }
        }
    }

    private const string CommentedOld = """
        # Planning notes live in this file too.
        project: Acme App
        roadmap: [V1, V2]
        epics:
        - number: 1
          title: Core Application
          stories:
          - code: US-03
            title: Local Setup
            status: Done
            release: V2   # moved after the kickoff
            folder: local-setup
        """;

    [Fact]
    public void Opening_an_old_file_with_comments_leaves_the_file_alone()
    {
        // A write cannot keep a comment, and before conversion existed a read never wrote — so a read
        // must not be what deletes one.
        var path = Path.Combine(_root, "commented.yaml");
        File.WriteAllText(path, CommentedOld);
        var svc = new BacklogService(() => path, () => Skills);

        var board = svc.GetBoard();

        Assert.Equal("V2", Assert.Single(board.Epics).Release);
        Assert.Equal(CommentedOld, File.ReadAllText(path));
    }

    [Fact]
    public void A_change_to_an_old_file_with_comments_still_writes_the_new_shape()
    {
        var path = Path.Combine(_root, "commented.yaml");
        File.WriteAllText(path, CommentedOld);
        var svc = new BacklogService(() => path, () => Skills);

        svc.SetStoryStatus("US-03", "In Progress");

        var written = File.ReadAllText(path);
        Assert.DoesNotContain(OwnerShapedBacklog.Stories(written), s => s.Children.ContainsKey(new YamlScalarNode("release")));
        Assert.Equal("V2", Assert.Single(YamlIndex.Parse(written).Epics).Release);
    }

    [Fact]
    public void A_hash_inside_a_quoted_value_does_not_stop_an_old_file_converting()
    {
        var path = Path.Combine(_root, "hash.yaml");
        var text = CommentedOld
            .Replace("# Planning notes live in this file too.\n", "", StringComparison.Ordinal)
            .Replace("   # moved after the kickoff", "", StringComparison.Ordinal)
            .Replace("title: Local Setup", "title: 'Local Setup #2'", StringComparison.Ordinal);
        Assert.DoesNotContain("kickoff", text, StringComparison.Ordinal);
        File.WriteAllText(path, text);

        new BacklogService(() => path, () => Skills).GetBoard();

        Assert.NotEqual(text, File.ReadAllText(path));
        Assert.Contains("'Local Setup #2'", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void GetBoard_still_opens_an_old_file_it_cannot_write()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "BACKLOG.yaml");
        const string legacy = """
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
        File.WriteAllText(path, legacy);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            var board = new BacklogService(() => path, () => Path.Combine(dir, "skills")).GetBoard();

            Assert.Equal("V1", Assert.Single(board.Epics).Release);
            Assert.Equal(legacy, File.ReadAllText(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
