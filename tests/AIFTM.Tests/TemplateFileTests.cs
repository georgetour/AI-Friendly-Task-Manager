using AIFTM.Api.Backlog;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// Guards the bundled starter content, which is also the first-run demo. If any of this breaks,
/// a fresh clone shows a broken board on its very first screen.
/// </summary>
public class TemplateFileTests
{
    [Fact]
    public void TemplateLocator_finds_the_backlog_template()
    {
        var path = TemplateLocator.Find("BACKLOG.template.yaml");

        Assert.True(File.Exists(path));
        Assert.EndsWith(Path.Combine("templates", "BACKLOG.template.yaml"), path);
    }

    [Fact]
    public void TemplateLocator_finds_the_skill_template()
    {
        Assert.True(File.Exists(TemplateLocator.Find("SKILL.template.md")));
    }

    [Fact]
    public void TemplateLocator_throws_for_an_unknown_file()
    {
        Assert.Throws<FileNotFoundException>(() => TemplateLocator.Find("NOPE.md"));
    }

    private static readonly string[] AllStatuses =
    {
        "Not Yet Started", "Under Review", "Refined", "In Progress", "Vendor Test", "Done", "On Hold",
    };

    private static string TemplatesDir() =>
        Path.GetDirectoryName(TemplateLocator.Find("BACKLOG.template.yaml"))!;

    private static string SkillsDir() => Path.Combine(TemplatesDir(), "skills");

    private static Board Template() =>
        YamlIndex.Parse(File.ReadAllText(TemplateLocator.Find("BACKLOG.template.yaml")));

    [Fact]
    public void Backlog_template_has_eight_epics_with_expected_story_counts()
    {
        // Epic 1 split along its release lines into epics 5-7 (Notifications, Reporting,
        // Wishlist) once the release moved from the story to the epic — see YamlIndex.
        var byNum = Template().Epics.ToDictionary(e => e.Number, e => e.Stories.Count);

        Assert.Equal(8, byNum.Count);
        Assert.Equal(2, byNum[0]);   // Developer Tooling
        Assert.Equal(8, byNum[1]);   // Core Application
        Assert.Equal(4, byNum[2]);   // CI/CD and Deployment
        Assert.Equal(1, byNum[3]);   // Mobile Apps — the placeholder story
        Assert.Equal(6, byNum[4]);   // Scaling and Performance
        Assert.Equal(1, byNum[5]);   // Notifications
        Assert.Equal(1, byNum[6]);   // Reporting
        Assert.Equal(1, byNum[7]);   // Wishlist
    }

    [Fact]
    public void Backlog_template_has_24_uniquely_coded_stories()
    {
        // No longer in US-01..US-24 file order: US-11/12/13 now sit under the epics their own
        // release lines moved them to, after every other epic. Uniqueness and completeness are
        // what matters, not position.
        var codes = Template().Epics.SelectMany(e => e.Stories).Select(s => s.Code).ToList();

        Assert.Equal(24, codes.Count);
        Assert.Equal(Enumerable.Range(1, 24).Select(i => $"US-{i:D2}").OrderBy(c => c), codes.OrderBy(c => c));
    }

    [Fact]
    public void Backlog_template_exercises_all_seven_statuses()
    {
        var used = Template().Epics.SelectMany(e => e.Stories).Select(s => s.Status).ToHashSet();

        Assert.All(AllStatuses, label => Assert.Contains(label, used));
    }

    [Fact]
    public void Every_story_folder_exists_with_all_three_files()
    {
        Assert.All(Template().Epics.SelectMany(e => e.Stories), s =>
        {
            var dir = Path.Combine(SkillsDir(), s.Folder);
            Assert.True(Directory.Exists(dir), $"{s.Code} points at '{s.Folder}', which is not in templates/skills.");
            Assert.True(File.Exists(Path.Combine(dir, "SKILL.md")), $"{s.Folder} has no SKILL.md");
            Assert.True(File.Exists(Path.Combine(dir, "tasks.yaml")), $"{s.Folder} has no tasks.yaml");
            Assert.True(File.Exists(Path.Combine(dir, "test-cases.yaml")), $"{s.Folder} has no test-cases.yaml");
        });
    }

    [Fact]
    public void Every_story_ships_tasks_and_test_cases()
    {
        Assert.All(Template().Epics.SelectMany(e => e.Stories), s =>
        {
            var detail = StoryFolder.Read(SkillsDir(), s.Folder);
            Assert.NotEmpty(detail.Tasks);
            Assert.NotEmpty(detail.TestCases);
        });
    }

    [Fact]
    public void Template_ships_no_orphaned_story_folders()
    {
        var referenced = Template().Epics.SelectMany(e => e.Stories).Select(s => s.Folder).ToHashSet();

        var onDisk = Directory.GetDirectories(SkillsDir())
            .Select(d => Path.GetFileName(d)!).ToHashSet();

        Assert.Equal(referenced.OrderBy(x => x), onDisk.OrderBy(x => x));
    }

    [Fact]
    public void The_shipped_template_validates_clean()
    {
        // This is the demo a fresh clone sees. If it does not validate, the first screen is an error.
        var report = BacklogValidation.Check(TemplateLocator.Find("BACKLOG.template.yaml"), SkillsDir());

        Assert.True(report.Ok, string.Join("\n", report.Issues.Select(i => $"{i.Severity}: {i.Message}")));
    }

    [Fact]
    public void The_template_has_a_readme_explaining_the_folder_layout()
    {
        var readme = Path.Combine(SkillsDir(), "README.md");

        Assert.True(File.Exists(readme), "skills/README.md is how an agent learns the structure.");
        var text = File.ReadAllText(readme);
        Assert.Contains("tasks.yaml", text);
        Assert.Contains("test-cases.yaml", text);
    }

    [Fact]
    public void Every_story_is_Description_Tasks_Acceptance_Criteria_and_nothing_else()
    {
        // A story is prose; anything with state you tick is YAML. A "## Test Cases" table in
        // SKILL.md is the same data as test-cases.yaml written twice, which is exactly the
        // duplication the split storage exists to avoid. The demo shipped with eleven of them.
        var expected = new[] { "## Description", "## Tasks", "## Acceptance Criteria" };

        foreach (var skill in Directory.GetFiles(SkillsDir(), "SKILL.md", SearchOption.AllDirectories))
        {
            var headings = File.ReadAllLines(skill)
                .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(expected, headings);
        }
    }

    [Fact]
    public void The_scaffold_a_new_story_is_created_from_has_the_same_three_sections()
    {
        var headings = File.ReadAllLines(TemplateLocator.Find("SKILL.template.md"))
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(new[] { "## Description", "## Tasks", "## Acceptance Criteria" }, headings);
    }

    [Fact]
    public void Every_release_used_by_an_epic_is_declared_in_the_roadmap()
    {
        var board = Template();

        var used = board.Epics.Select(e => e.Release).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct();

        Assert.All(used, r => Assert.Contains(r, board.Roadmap));
    }

    [Fact]
    public void The_README_documents_BACKLOG_yaml_in_the_shape_the_app_writes()
    {
        // Coding agents edit the backlog straight from this example. An example in the old shape
        // teaches them to write a file the app has to convert on its next read.
        var readme = File.ReadAllText(Path.Combine(Path.GetDirectoryName(TemplatesDir())!, "README.md"))
            .Replace("\r\n", "\n");
        var example = System.Text.RegularExpressions.Regex.Match(readme,
            @"\*\*`BACKLOG\.yaml`\*\*\s*```yaml\n(.*?)```", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(example.Success, "README.md has no BACKLOG.yaml example.");

        var board = YamlIndex.Parse(example.Groups[1].Value);

        Assert.False(board.Migrated);
        var epic = Assert.Single(board.Epics);
        Assert.Contains(epic.Release, board.Roadmap);
        Assert.NotEmpty(epic.Stories);
    }

    [Fact]
    public void The_template_is_already_in_the_new_shape_so_nothing_converts_it()
    {
        var text = File.ReadAllText(TemplateLocator.Find("BACKLOG.template.yaml"));

        var board = YamlIndex.Parse(text);

        Assert.False(board.Migrated);
        Assert.Equal(text.Replace("\r\n", "\n"), YamlIndex.Write(board).Replace("\r\n", "\n"));
        Assert.Equal(8, board.Epics.Count);
        Assert.Equal(24, board.Epics.Sum(e => e.Stories.Count));
        Assert.All(board.Epics, e => Assert.Contains(e.Release, board.Roadmap));
    }
}
