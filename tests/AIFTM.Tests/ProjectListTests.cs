using AIFTM.Api.Backlog;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// A project is a remembered pair of paths — adding one never copies or moves a backlog, and the
/// name always comes from the file rather than being stored beside it.
/// </summary>
public class ProjectListTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiftm-proj-" + Guid.NewGuid().ToString("N"));
    private readonly AppConfigService _cfg;

    public ProjectListTests()
    {
        Directory.CreateDirectory(_root);
        _cfg = new AppConfigService(Path.Combine(_root, "aiftm.config.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }

    private string MakeBacklog(string name, string project)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "skills"));
        var path = Path.Combine(dir, "BACKLOG.yaml");
        File.WriteAllText(path, $"project: {project}\nroadmap: []\nepics: []\n");
        return path;
    }

    [Fact]
    public void A_config_written_before_projects_existed_still_shows_its_project()
    {
        // The upgrade case. Old configs have no list at all, so without folding the configured
        // backlog in, everyone who updates opens Projects and sees nothing.
        var backlog = MakeBacklog("legacy", "Legacy App");
        File.WriteAllText(Path.Combine(_root, "aiftm.config.json"),
            $$"""{"BacklogPath":{{System.Text.Json.JsonSerializer.Serialize(backlog)}},"SkillsPath":null,"LogoPath":null,"IsDemo":false}""");

        var projects = _cfg.Projects();

        var only = Assert.Single(projects);
        Assert.Equal("Legacy App", only.Name);
        Assert.True(only.IsCurrent);
    }

    [Fact]
    public void The_name_comes_from_the_backlogs_own_project_field()
    {
        _cfg.AddProject(MakeBacklog("one", "Acme Billing"), null);

        Assert.Equal("Acme Billing", Assert.Single(_cfg.Projects()).Name);
    }

    [Fact]
    public void Adding_a_project_switches_to_it_and_keeps_the_previous_one_listed()
    {
        var first = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath;
        var second = _cfg.AddProject(MakeBacklog("two", "Second"), null).BacklogPath;

        var projects = _cfg.Projects();
        Assert.Equal(2, projects.Count);
        Assert.Equal(second, _cfg.Load().BacklogPath);
        Assert.Single(projects, p => p.IsCurrent);
        Assert.Contains(projects, p => p.BacklogPath == first);
    }

    [Fact]
    public void Adding_the_same_backlog_twice_does_not_list_it_twice()
    {
        var path = MakeBacklog("one", "First");
        _cfg.AddProject(path, null);
        _cfg.AddProject(path, null);

        Assert.Single(_cfg.Projects());
    }

    [Fact]
    public void Selecting_a_project_points_the_tracker_at_it()
    {
        var first = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath;
        _cfg.AddProject(MakeBacklog("two", "Second"), null);

        _cfg.SelectProject(first!);

        Assert.Equal(first, _cfg.Load().BacklogPath);
        Assert.True(_cfg.Projects().First(p => p.BacklogPath == first).IsCurrent);
    }

    [Fact]
    public void Selecting_a_project_that_was_never_added_is_refused()
    {
        // Otherwise this endpoint would point the app at any path a request names.
        _cfg.AddProject(MakeBacklog("one", "First"), null);

        Assert.Throws<BacklogValidationException>(
            () => _cfg.SelectProject(Path.Combine(_root, "elsewhere", "BACKLOG.yaml")));
    }



    [Fact]
    public void A_project_whose_file_has_gone_is_listed_as_missing_rather_than_hidden()
    {
        // Hiding it would leave a moved folder looking like a project you never added.
        var path = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath!;
        File.Delete(path);

        var only = Assert.Single(_cfg.Projects());
        Assert.True(only.Missing);
    }

    [Fact]
    public void Adding_a_project_whose_backlog_does_not_exist_creates_it_from_the_template()
    {
        var path = Path.Combine(_root, "fresh", "BACKLOG.yaml");

        _cfg.AddProject(path, null);

        Assert.True(File.Exists(path));
        Assert.NotEmpty(YamlIndex.Parse(File.ReadAllText(path)).Epics);
    }

    [Fact]
    public void A_project_created_from_the_template_is_named_after_its_folder()
    {
        // Otherwise every new project is called "Acme App" — the template's own name — and the list
        // becomes a column of identical labels distinguished only by path.
        _cfg.AddProject(Path.Combine(_root, "invoicing", "BACKLOG.yaml"), null);

        Assert.Equal("invoicing", Assert.Single(_cfg.Projects()).Name);
    }

    [Fact]
    public void Removing_a_project_leaves_every_one_of_its_files_alone()
    {
        // The whole promise of the confirmation note: this removes a row from a list.
        var path = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath!;
        var skills = Path.Combine(Path.GetDirectoryName(path)!, "skills");

        _cfg.RemoveProject(path, "First");

        Assert.Empty(_cfg.Projects());
        Assert.True(File.Exists(path), "Removing a project must never delete its backlog.");
        Assert.True(Directory.Exists(skills), "Removing a project must never delete its story folders.");
    }

    [Theory]
    [InlineData("first")]        // wrong case
    [InlineData("Firs")]         // truncated
    [InlineData("")]             // nothing typed
    [InlineData("Second")]       // another project's name
    public void Removing_without_typing_the_exact_name_is_refused(string typed)
    {
        // The browser asks for the name too, but this endpoint is reachable without it.
        var path = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath!;

        Assert.Throws<BacklogValidationException>(() => _cfg.RemoveProject(path, typed));
        Assert.Single(_cfg.Projects());
    }

    [Fact]
    public void Removing_a_project_that_was_never_added_is_refused()
    {
        _cfg.AddProject(MakeBacklog("one", "First"), null);

        Assert.Throws<BacklogValidationException>(
            () => _cfg.RemoveProject(Path.Combine(_root, "elsewhere", "BACKLOG.yaml"), "First"));
    }

    [Fact]
    public void Removing_the_current_project_opens_another_rather_than_leaving_nothing()
    {
        var first = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath!;
        var second = _cfg.AddProject(MakeBacklog("two", "Second"), null).BacklogPath!;

        _cfg.RemoveProject(second, "Second");

        Assert.Equal(first, _cfg.Load().BacklogPath);
    }

    [Fact]
    public void A_removed_project_comes_back_by_adding_the_same_path_again()
    {
        // What the note promises: "you can bring it back at any time by adding it again."
        var path = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath!;
        _cfg.RemoveProject(path, "First");

        _cfg.AddProject(path, null);

        var back = Assert.Single(_cfg.Projects());
        Assert.Equal("First", back.Name);
        Assert.False(back.Missing);
    }

    [Fact]
    public void A_logo_belongs_to_its_project_and_follows_you_when_you_switch()
    {
        // A logo is the project's branding. Shared across projects it would show the wrong one.
        var first = _cfg.AddProject(MakeBacklog("one", "First"), null).BacklogPath!;
        _cfg.SetLogoPath("/uploads/logo-first.png");

        var second = _cfg.AddProject(MakeBacklog("two", "Second"), null).BacklogPath!;
        Assert.Null(_cfg.Load().LogoPath);          // a new project starts with none

        _cfg.SelectProject(first);
        Assert.Equal("/uploads/logo-first.png", _cfg.Load().LogoPath);

        _cfg.SelectProject(second);
        Assert.Null(_cfg.Load().LogoPath);
    }

    [Fact]
    public void An_upgrade_keeps_the_logo_it_already_had()
    {
        // Old configs store one logo at the top level. It has to survive becoming the current
        // project's, or everyone loses their logo on first run after updating.
        var backlog = MakeBacklog("legacy", "Legacy App");
        File.WriteAllText(Path.Combine(_root, "aiftm.config.json"),
            $$"""
            {"BacklogPath":{{System.Text.Json.JsonSerializer.Serialize(backlog)}},"SkillsPath":null,
             "LogoPath":"/uploads/logo.png","IsDemo":false}
            """);

        _cfg.AddProject(MakeBacklog("two", "Second"), null);
        _cfg.SelectProject(backlog);

        Assert.Equal("/uploads/logo.png", _cfg.Load().LogoPath);
    }

    [Fact]
    public void Renaming_writes_the_backlogs_own_project_field()
    {
        // Not stored in config: one home for the name means it cannot disagree with the file.
        var path = MakeBacklog("one", "Old Name");
        _cfg.AddProject(path, null);

        _cfg.SetProjectName(path, "New Name");

        Assert.Equal("New Name", YamlIndex.Parse(File.ReadAllText(path)).Project);
        Assert.Equal("New Name", Assert.Single(_cfg.Projects()).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_project_name_is_refused(string name)
    {
        var path = MakeBacklog("one", "First");

        Assert.Throws<BacklogValidationException>(() => _cfg.SetProjectName(path, name));
    }

    [Fact]
    public void An_existing_backlog_keeps_the_name_it_already_had()
    {
        // The rename is only for a file we just created; adopting an existing project must not
        // rewrite its file.
        var path = MakeBacklog("mine", "My Own Name");

        _cfg.AddProject(path, null);

        Assert.Equal("My Own Name", Assert.Single(_cfg.Projects()).Name);
    }
}
