using AIFTM.Api.Backlog;

namespace AIFTM.Tests;

public class StoryFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-folder-" + Guid.NewGuid().ToString("N"));

    public StoryFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void A_task_with_a_duplicate_key_is_rejected()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bills"));
        File.WriteAllText(Path.Combine(_root, "bills", "tasks.yaml"),
            "- text: Build the API\n  text: Build the page\n  done: false\n");

        Assert.Throws<YamlDotNet.Core.YamlException>(() => StoryFolder.ReadTasks(_root, "bills"));
    }

    [Fact]
    public void A_test_case_with_a_duplicate_key_is_rejected()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bills"));
        File.WriteAllText(Path.Combine(_root, "bills", "test-cases.yaml"),
            "- text: Check the total\n  status: Passed\n  status: Failed\n");

        Assert.Throws<YamlDotNet.Core.YamlException>(() => StoryFolder.ReadTestCases(_root, "bills"));
    }

    [Fact]
    public void Read_returns_empty_lists_when_the_folder_has_no_files()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bills"));

        var detail = StoryFolder.Read(_root, "bills");

        Assert.Empty(detail.Tasks);
        Assert.Empty(detail.TestCases);
    }

    [Fact]
    public void Read_returns_empty_lists_when_the_folder_does_not_exist()
    {
        var detail = StoryFolder.Read(_root, "nope");

        Assert.Empty(detail.Tasks);
        Assert.Empty(detail.TestCases);
    }

    [Fact]
    public void WriteTasks_then_Read_round_trips()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");

        StoryFolder.WriteTasks(_root, "bills", new[]
        {
            new TaskItem("Build the API", true),
            new TaskItem("Build the page", false),
        });

        var detail = StoryFolder.Read(_root, "bills");
        Assert.Equal(2, detail.Tasks.Count);
        Assert.Equal("Build the API", detail.Tasks[0].Text);
        Assert.True(detail.Tasks[0].Done);
        Assert.False(detail.Tasks[1].Done);
    }

    [Fact]
    public void WriteTestCases_then_Read_round_trips()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");

        StoryFolder.WriteTestCases(_root, "bills", new[]
        {
            new TestCase("A user cannot read another user's bill", "Passed"),
        });

        var detail = StoryFolder.Read(_root, "bills");
        var tc = Assert.Single(detail.TestCases);
        Assert.Equal("A user cannot read another user's bill", tc.Text);
        Assert.Equal("Passed", tc.Status);
    }

    [Fact]
    public void Writing_tasks_leaves_test_cases_untouched()
    {
        // The whole point of two files: ticking a task must not rewrite test data.
        StoryFolder.Create(_root, "bills", "US-05", "Bills");
        StoryFolder.WriteTestCases(_root, "bills", new[] { new TestCase("Verify", "Passed") });

        StoryFolder.WriteTasks(_root, "bills", new[] { new TaskItem("Do it", false) });

        var detail = StoryFolder.Read(_root, "bills");
        Assert.Single(detail.TestCases);
        Assert.Equal("Passed", detail.TestCases[0].Status);
        Assert.Single(detail.Tasks);
    }

    [Fact]
    public void An_empty_list_is_written_and_read_back_as_empty()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");
        StoryFolder.WriteTasks(_root, "bills", new[] { new TaskItem("One", false) });

        StoryFolder.WriteTasks(_root, "bills", Array.Empty<TaskItem>());

        Assert.Empty(StoryFolder.Read(_root, "bills").Tasks);
    }

    [Fact]
    public void Create_writes_a_SKILL_md_naming_the_story()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");

        var skill = File.ReadAllText(StoryFolder.SkillPath(_root, "bills"));
        Assert.Contains("US-05", skill);
        Assert.Contains("Bills", skill);
    }

    [Fact]
    public void Create_does_not_overwrite_an_existing_SKILL_md()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");
        File.WriteAllText(StoryFolder.SkillPath(_root, "bills"), "hand written");

        StoryFolder.Create(_root, "bills", "US-05", "Bills");

        Assert.Equal("hand written", File.ReadAllText(StoryFolder.SkillPath(_root, "bills")));
    }

    [Fact]
    public void Delete_removes_the_whole_folder()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");

        StoryFolder.Delete(_root, "bills");

        Assert.False(Directory.Exists(Path.Combine(_root, "bills")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("bills/../../outside")]
    [InlineData("..\\..\\outside")]
    [InlineData("bills\\..\\..\\outside")]
    public void A_folder_cannot_escape_the_skills_root(string folder)
    {
        // The backslash cases are the ones that only a Linux run catches: there "\" is an ordinary
        // filename character, so without normalising these resolve to a folder oddly named
        // "..\outside" rather than escaping — and the same value is a real traversal on Windows.
        // BACKLOG.yaml travels in git, so it has to mean the same thing on both.
        Assert.Throws<BacklogValidationException>(() => StoryFolder.Read(_root, folder));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("bills/..")]
    public void A_folder_cannot_BE_the_skills_root(string folder)
    {
        // Deleting a story whose folder resolved to the root would delete every other story's
        // folder along with it.
        Assert.Throws<BacklogValidationException>(() => StoryFolder.Delete(_root, folder));
    }

    [Fact]
    public void An_absolute_folder_path_is_rejected()
    {
        var absolute = OperatingSystem.IsWindows() ? @"C:\Windows\System32" : "/etc";

        Assert.Throws<BacklogValidationException>(() => StoryFolder.Read(_root, absolute));
    }

    [Fact]
    public void An_empty_folder_name_is_rejected()
    {
        Assert.Throws<BacklogValidationException>(() => StoryFolder.Read(_root, "  "));
    }

    [Fact]
    public void Text_is_stored_verbatim_even_when_it_looks_like_markup()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");

        StoryFolder.WriteTasks(_root, "bills", new[] { new TaskItem("<script>alert(1)</script>", false) });

        Assert.Equal("<script>alert(1)</script>", StoryFolder.Read(_root, "bills").Tasks[0].Text);
    }

    [Fact]
    public void Text_containing_yaml_punctuation_survives_a_round_trip()
    {
        StoryFolder.Create(_root, "bills", "US-05", "Bills");

        StoryFolder.WriteTasks(_root, "bills", new[]
        {
            new TaskItem("Handle: colons, #hashes and \"quotes\"", true),
            new TaskItem("- leading dash", false),
        });

        var tasks = StoryFolder.Read(_root, "bills").Tasks;
        Assert.Equal("Handle: colons, #hashes and \"quotes\"", tasks[0].Text);
        Assert.Equal("- leading dash", tasks[1].Text);
    }
}
