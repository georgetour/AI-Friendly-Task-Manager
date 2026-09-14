using AIFTM.Api.Backlog;

namespace AIFTM.Tests;

public class BacklogValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-val-" + Guid.NewGuid().ToString("N"));

    private string Backlog => Path.Combine(_root, "BACKLOG.yaml");
    private string Skills => Path.Combine(_root, "skills");

    public BacklogValidationTests() => Directory.CreateDirectory(Skills);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void WriteIndex(string yaml) => File.WriteAllText(Backlog, yaml);

    private ValidationReport CheckYaml(string yaml)
    {
        WriteIndex(yaml);
        return BacklogValidation.Check(Backlog, Skills);
    }

    private const string Good = """
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
        """;

    [Fact]
    public void A_correct_backlog_reports_ok()
    {
        WriteIndex(Good);
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.True(report.Ok);
        Assert.DoesNotContain(report.Issues, i => i.Severity == "error");
    }

    [Fact]
    public void A_bad_indent_reports_the_line_number()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                 title: Broken
            """);

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        var issue = report.Issues.First(i => i.Severity == "error");
        Assert.Contains("Line", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_file_is_an_error_not_a_crash()
    {
        var report = BacklogValidation.Check(Path.Combine(_root, "nope.yaml"), Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_story_pointing_at_a_missing_folder_is_an_error()
    {
        WriteIndex(Good);   // the "board" folder is never created

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "error" && i.Message.Contains("board"));
    }

    [Fact]
    public void A_folder_nobody_references_is_a_warning_not_an_error()
    {
        WriteIndex(Good);
        Directory.CreateDirectory(Path.Combine(Skills, "board"));
        Directory.CreateDirectory(Path.Combine(Skills, "orphan"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.True(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "warning" && i.Message.Contains("orphan"));
    }

    [Fact]
    public void An_unreferenced_folder_with_files_warns_that_it_is_not_a_story()
    {
        // The whole confusion this warning exists to clear up: a folder with a SKILL.md in it looks
        // like it should have appeared on the board, and nothing anywhere said why it did not.
        WriteIndex(Good);
        Directory.CreateDirectory(Path.Combine(Skills, "board"));
        var kept = Directory.CreateDirectory(Path.Combine(Skills, "template")).FullName;
        File.WriteAllText(Path.Combine(kept, "SKILL.md"), "# A scaffold I keep on purpose\n");

        var report = BacklogValidation.Check(Backlog, Skills);

        var issue = Assert.Single(report.Issues, i => i.Message.Contains("\"template\""));

        // The three things the first version left out, which is why it could not be acted on:
        // where the folder is, what is in it, and that the backlog was searched and came up empty.
        Assert.Contains(kept, issue.Detail);
        Assert.Contains("SKILL.md", issue.Detail);
        Assert.Contains("BACKLOG.yaml", issue.Detail);
        Assert.Contains("found none", issue.Detail);
        Assert.Contains("delete the folder", issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_unreferenced_folder_is_reported_as_empty()
    {
        WriteIndex(Good);
        Directory.CreateDirectory(Path.Combine(Skills, "board"));
        Directory.CreateDirectory(Path.Combine(Skills, "expense"));

        var report = BacklogValidation.Check(Backlog, Skills);

        var issue = Assert.Single(report.Issues, i => i.Message.Contains("\"expense\""));
        Assert.Contains("it is empty", issue.Message);
        Assert.Contains("the folder is empty", issue.Detail);
    }

    [Fact]
    public void Every_index_issue_names_the_file_and_the_line_it_is_on()
    {
        // "US-01 is not a status" without a line number means searching the file by hand, which is
        // the whole complaint this exists to answer.
        WriteIndex(Good.Replace("status: Done", "status: Finished"));
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = BacklogValidation.Check(Backlog, Skills);

        var issue = Assert.Single(report.Issues, i => i.Message.Contains("Finished"));
        Assert.Equal("BACKLOG.yaml line 7", issue.Where);   // the "code: US-01" line
    }

    [Fact]
    public void The_line_is_the_offending_story_not_simply_the_first_match()
    {
        WriteIndex("""
            project: Test
            roadmap: [V1]
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: One
                    status: Done
                    folder: one
                  - code: US-02
                    title: Two
                    status: Finished
                    folder: two
            """);
        Directory.CreateDirectory(Path.Combine(Skills, "one"));
        Directory.CreateDirectory(Path.Combine(Skills, "two"));

        var report = BacklogValidation.Check(Backlog, Skills);

        var issue = Assert.Single(report.Issues, i => i.Message.Contains("Finished"));
        Assert.Equal("BACKLOG.yaml line 11", issue.Where);   // US-02, not US-01
    }

    [Fact]
    public void A_duplicate_story_code_is_an_error()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: One
                    status: Done
                    folder: one
                  - code: US-01
                    title: Two
                    status: Done
                    folder: two
            """);
        Directory.CreateDirectory(Path.Combine(Skills, "one"));
        Directory.CreateDirectory(Path.Combine(Skills, "two"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "error" && i.Message.Contains("US-01"));
    }

    [Fact]
    public void A_duplicate_epic_number_is_an_error()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                title: One
              - number: 0
                title: Two
            """);

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("Epic 0"));
    }

    [Fact]
    public void An_unknown_status_word_is_an_error()
    {
        WriteIndex(Good.Replace("status: Done", "status: Finished"));
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("Finished"));
    }

    [Fact]
    public void An_epic_release_missing_from_the_roadmap_is_a_warning()
    {
        var report = CheckYaml("""
            project: Acme App
            roadmap: [1.0.0]
            epics:
            - number: 1
              release: 9.9.9
              title: Core Application
              stories: []
            """);

        var issue = Assert.Single(report.Issues, i => i.Message.Contains("9.9.9", StringComparison.Ordinal));
        Assert.Equal("warning", issue.Severity);
        Assert.True(report.Ok);
    }

    [Fact]
    public void An_epic_with_no_release_is_not_an_issue()
    {
        var report = CheckYaml("""
            project: Acme App
            roadmap: [1.0.0]
            epics:
            - number: 1
              title: Core Application
              stories: []
            """);

        Assert.DoesNotContain(report.Issues, i => i.Message.Contains("release", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Two_epics_sharing_an_unlisted_release_each_point_at_their_own_line()
    {
        var report = CheckYaml("""
            project: Acme App
            roadmap: [1.0.0]
            epics:
            - number: 1
              release: 9.9.9
              title: First
              stories: []
            - number: 2
              release: 9.9.9
              title: Second
              stories: []
            """);

        var warnings = report.Issues.Where(i => i.Message.Contains("9.9.9", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.NotEqual(warnings[0].Where, warnings[1].Where);
    }

    [Fact]
    public void An_unknown_test_case_status_names_the_file_it_is_in()
    {
        WriteIndex(Good);
        var dir = Path.Combine(Skills, "board");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test-cases.yaml"), "- text: Check it\n  status: Maybe\n");

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Where != null && i.Where.Contains("test-cases.yaml"));
    }

    [Fact]
    public void A_story_with_no_folder_is_an_error()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: Board
                    status: Done
            """);

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("no folder"));
    }

    [Fact]
    public void A_broken_test_cases_file_is_blamed_on_test_cases_not_tasks()
    {
        // The message has to name the file that actually failed. Reading both files behind one
        // call once reported a broken test-cases.yaml as a broken tasks.yaml, sending you to
        // inspect a file with nothing wrong with it.
        WriteIndex(Good);
        var dir = Path.Combine(Skills, "board");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tasks.yaml"), "- text: Perfectly fine\n  done: false\n");

        // Shell-style quote escaping ('\'') leaking into a YAML file — exactly what an agent
        // writing this through a bash heredoc produces, and what broke a real backlog.
        File.WriteAllText(Path.Combine(dir, "test-cases.yaml"),
            "- text: 'Run date_trunc('\\''year'\\'', paid_at) and check'\n  status: Not Run\n");

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        var issue = report.Issues.First(i => i.Severity == "error");
        Assert.Contains("test-cases.yaml", issue.Where);
        Assert.DoesNotContain("tasks.yaml", issue.Where);
    }

    private const string CommentedOld = """
        # Planning notes live in this file too.
        project: Test
        roadmap: [V1]
        epics:
          - number: 0
            title: Tooling
            stories:
              - code: US-01
                title: Board
                status: Done
                release: V1   # agreed in the kickoff
                folder: board
        """;

    [Fact]
    public void An_old_file_with_comments_warns_that_the_next_change_will_drop_them()
    {
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = CheckYaml(CommentedOld);

        Assert.True(report.Ok);
        var warning = Assert.Single(report.Issues, i => i.Message.Contains("comment", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("warning", warning.Severity);
        Assert.Contains("BACKLOG.yaml", warning.Where);
    }

    [Fact]
    public void A_commented_file_already_in_the_new_shape_has_nothing_to_warn_about()
    {
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = CheckYaml("""
            # Planning notes live in this file too.
            project: Test
            roadmap: [V1]
            epics:
              - number: 0
                release: V1   # agreed in the kickoff
                title: Tooling
                stories:
                  - code: US-01
                    title: Board
                    status: Done
                    folder: board
            """);

        Assert.DoesNotContain(report.Issues, i => i.Message.Contains("comment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_hash_inside_a_quoted_value_is_not_a_comment()
    {
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = CheckYaml(Good.Replace("title: Board", "title: 'Board #1'"));

        Assert.DoesNotContain(report.Issues, i => i.Message.Contains("comment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_folder_escaping_the_skills_root_is_an_error()
    {
        WriteIndex(Good.Replace("folder: board", "folder: ../../escape"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("outside"));
    }
}
