using AIFTM.Api.Backlog;
using AIFTM.Api.Backlog.Legacy;

namespace AIFTM.Tests;

/// <summary>
/// The upgrade path for anyone holding a markdown backlog. If these break, an existing project
/// cannot move to the YAML layout without hand-editing.
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-mig-" + Guid.NewGuid().ToString("N"));

    private string OutYaml => Path.Combine(_root, "BACKLOG.yaml");
    private string Skills => Path.Combine(_root, "skills");

    public MigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private MigrationResult Migrate() =>
        MarkdownMigrator.Run(TestBacklogLocator.Resolve(), OutYaml, Skills);

    [Fact]
    public void Converts_the_bundled_sample_backlog()
    {
        var result = Migrate();

        Assert.True(result.Stories > 0);
        Assert.True(File.Exists(OutYaml));

        var board = YamlIndex.Parse(File.ReadAllText(OutYaml));
        Assert.Equal(result.Epics, board.Epics.Count);
        Assert.Equal(result.Stories, board.Epics.Sum(e => e.Stories.Count));
    }

    [Fact]
    public void Every_story_gets_a_folder_with_its_files()
    {
        Migrate();

        var board = YamlIndex.Parse(File.ReadAllText(OutYaml));
        Assert.All(board.Epics.SelectMany(e => e.Stories), story =>
        {
            var dir = Path.Combine(Skills, story.Folder);
            Assert.True(Directory.Exists(dir), $"{story.Code} has no folder");
            Assert.True(File.Exists(Path.Combine(dir, "tasks.yaml")), $"{story.Code} has no tasks.yaml");
            Assert.True(File.Exists(Path.Combine(dir, "test-cases.yaml")), $"{story.Code} has no test-cases.yaml");
            Assert.True(File.Exists(Path.Combine(dir, "SKILL.md")), $"{story.Code} has no SKILL.md");
        });
    }

    [Fact]
    public void Tasks_and_test_cases_survive_the_move()
    {
        Migrate();

        var board = YamlIndex.Parse(File.ReadAllText(OutYaml));
        var details = board.Epics.SelectMany(e => e.Stories)
            .Select(s => StoryFolder.Read(Skills, s.Folder)).ToList();

        Assert.True(details.Any(d => d.Tasks.Count > 0), "no story kept its tasks");
        Assert.True(details.Any(d => d.TestCases.Count > 0), "no story kept its test cases");
    }

    [Fact]
    public void Statuses_lose_their_emoji_and_land_in_the_vocabulary()
    {
        Migrate();

        var board = YamlIndex.Parse(File.ReadAllText(OutYaml));
        Assert.All(board.Epics.SelectMany(e => e.Stories),
            s => Assert.Contains(s.Status, BacklogValidation.Statuses));

        var statuses = board.Epics.SelectMany(e => e.Stories)
            .Select(s => StoryFolder.Read(Skills, s.Folder)).SelectMany(d => d.TestCases)
            .Select(t => t.Status);
        Assert.All(statuses, s => Assert.Contains(s, BacklogValidation.TestStatuses));
    }

    [Fact]
    public void Folders_are_unique_even_when_two_stories_share_a_title()
    {
        Migrate();

        var folders = YamlIndex.Parse(File.ReadAllText(OutYaml))
            .Epics.SelectMany(e => e.Stories).Select(s => s.Folder).ToList();

        Assert.Equal(folders.Count, folders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_migrated_backlog_validates_clean()
    {
        Migrate();

        var report = BacklogValidation.Check(OutYaml, Skills);

        Assert.True(report.Ok, string.Join("\n", report.Issues.Select(i => $"{i.Severity}: {i.Message}")));
    }

    [Fact]
    public void Refuses_to_overwrite_an_existing_yaml()
    {
        File.WriteAllText(OutYaml, "project: Existing\n");

        var e = Assert.Throws<BacklogValidationException>(() => Migrate());

        Assert.Contains("already exists", e.Message);
        Assert.Equal("project: Existing\n", File.ReadAllText(OutYaml));
    }

    [Fact]
    public void Refuses_a_source_that_is_not_there()
    {
        Assert.Throws<BacklogValidationException>(
            () => MarkdownMigrator.Run(Path.Combine(_root, "nope.md"), OutYaml, Skills));
    }

    [Fact]
    public void Reports_where_an_old_skill_file_lived_instead_of_copying_it_blindly()
    {
        var result = Migrate();

        // The sample references skill files; migration should tell you rather than guess.
        Assert.All(result.Notes, n => Assert.Contains("SKILL.md", n));
    }

    [Fact]
    public void An_epic_straddling_two_releases_lands_on_the_earlier_one_with_no_release_left_on_a_story()
    {
        Migrate();

        // Epic 1 in the bundled sample has stories in V1 and V1.5; the epic should carry V1.
        var text = File.ReadAllText(OutYaml);
        var board = YamlIndex.Parse(text);

        var epic = board.Epics.Single(e => e.Number == 1);
        Assert.Equal("V1", epic.Release);
        Assert.False(board.Migrated);
        Assert.DoesNotContain("release: V1.5", text, StringComparison.Ordinal);
    }
}
