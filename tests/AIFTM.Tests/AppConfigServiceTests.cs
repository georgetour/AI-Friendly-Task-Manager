using AIFTM.Api.Services;

namespace AIFTM.Tests;

public class AppConfigServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private string ConfigPath => Path.Combine(_dir, "aiftm.config.json");
    private AppConfigService Svc() => new(ConfigPath);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_returns_empty_config_when_file_missing()
    {
        var cfg = Svc().Load();

        Assert.Null(cfg.BacklogPath);
        Assert.Null(cfg.SkillsPath);
        Assert.Null(cfg.LogoPath);
        Assert.False(cfg.IsDemo);
    }

    [Fact]
    public void Load_returns_empty_config_when_file_is_corrupt_instead_of_throwing()
    {
        File.WriteAllText(ConfigPath, "{ not valid json ");

        var cfg = Svc().Load();

        Assert.Null(cfg.BacklogPath);
        Assert.Null(cfg.SkillsPath);
        Assert.Null(cfg.LogoPath);
        Assert.False(cfg.IsDemo);
    }

    [Fact]
    public void Load_accepts_a_hand_edited_camelCase_file()
    {
        File.WriteAllText(ConfigPath, """{"backlogPath":"C:/proj/BACKLOG.yaml","skillsPath":null,"logoPath":null,"isDemo":false}""");

        var cfg = Svc().Load();

        Assert.Equal("C:/proj/BACKLOG.yaml", cfg.BacklogPath);
    }

    [Fact]
    public void SetSkillsPath_persists_and_reloads()
    {
        var svc = Svc();

        svc.SetSkillsPath(_dir);

        Assert.Equal(Path.GetFullPath(_dir), svc.Load().SkillsPath);
    }

    [Fact]
    public void SetBacklogPath_to_a_missing_file_creates_it_from_the_template()
    {
        var svc = Svc();
        var target = Path.Combine(_dir, "sub", "BACKLOG.yaml");

        var result = svc.SetBacklogPath(target);

        Assert.True(File.Exists(target));
        Assert.Equal(Path.GetFullPath(target), result.BacklogPath);
        Assert.False(result.IsDemo);

        // It must be a usable backlog, not just a file that exists.
        var board = AIFTM.Api.Backlog.YamlIndex.Parse(File.ReadAllText(target));
        Assert.NotEmpty(board.Epics);
    }

    [Fact]
    public void SetBacklogPath_to_an_existing_file_does_not_overwrite_it()
    {
        var svc = Svc();
        var target = Path.Combine(_dir, "BACKLOG.yaml");
        File.WriteAllText(target, "# Epic 0: Custom\n");

        svc.SetBacklogPath(target);

        Assert.Equal("# Epic 0: Custom\n", File.ReadAllText(target));
    }

    [Fact]
    public void MaterializeDemo_creates_the_file_once_and_marks_IsDemo()
    {
        var svc = Svc();
        var demoPath = Path.Combine(_dir, "data", "BACKLOG.demo.yaml");

        var result = svc.MaterializeDemo(demoPath);

        Assert.True(File.Exists(demoPath));
        Assert.True(result.IsDemo);
        Assert.Equal(Path.GetFullPath(demoPath), result.BacklogPath);
    }

    [Fact]
    public void MaterializeDemo_copies_the_skill_files_and_points_SkillsPath_at_them()
    {
        var svc = Svc();
        var demoPath = Path.Combine(_dir, "data", "BACKLOG.demo.yaml");

        var result = svc.MaterializeDemo(demoPath);

        var demoDir = Path.GetDirectoryName(demoPath)!;
        var skillsDir = Path.Combine(demoDir, "skills");

        // A story's folder is a bare name now, so the skills directory itself is the root.
        Assert.Equal(Path.GetFullPath(skillsDir), result.SkillsPath);
        Assert.True(File.Exists(Path.Combine(skillsDir, "backlog-board", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(skillsDir, "backlog-board", "tasks.yaml")));
        Assert.True(File.Exists(Path.Combine(skillsDir, "backlog-board", "test-cases.yaml")));

        // One folder per story, not per shared skill file.
        Assert.Equal(24, Directory.GetFiles(skillsDir, "SKILL.md", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void MaterializeDemo_preserves_an_existing_user_configured_SkillsPath()
    {
        var svc = Svc();
        var realSkills = Path.Combine(_dir, "my-real-skills");
        Directory.CreateDirectory(realSkills);
        svc.SetSkillsPath(realSkills);
        var demoPath = Path.Combine(_dir, "data", "BACKLOG.demo.yaml");

        // Simulate ResolveBacklogPath falling through to MaterializeDemo because the configured
        // backlog is temporarily missing (moved folder, unmounted drive) — SkillsPath must survive.
        var result = svc.MaterializeDemo(demoPath);

        Assert.Equal(Path.GetFullPath(realSkills), result.SkillsPath);
        Assert.Equal(Path.GetFullPath(realSkills), svc.Load().SkillsPath);
    }

    [Fact]
    public void MaterializeDemo_does_not_overwrite_edited_demo_skills()
    {
        var svc = Svc();
        var demoPath = Path.Combine(_dir, "data", "BACKLOG.demo.yaml");
        svc.MaterializeDemo(demoPath);

        var skillFile = Path.Combine(Path.GetDirectoryName(demoPath)!, "skills", "backlog-board", "SKILL.md");
        File.WriteAllText(skillFile, "# Edited by the user\n");

        svc.MaterializeDemo(demoPath);

        Assert.Equal("# Edited by the user\n", File.ReadAllText(skillFile));
    }

    [Fact]
    public void ResolveBacklogPath_prefers_the_override_and_never_persists_it()
    {
        var svc = Svc();
        var overridePath = Path.Combine(_dir, "override.md");
        File.WriteAllText(overridePath, "# Epic 0: Override\n");

        var resolved = svc.ResolveBacklogPath(overridePath, Path.Combine(_dir, "data", "BACKLOG.demo.yaml"));

        Assert.Equal(overridePath, resolved);
        Assert.Null(svc.Load().BacklogPath);
    }

    [Fact]
    public void ResolveBacklogPath_falls_back_to_a_materialized_demo()
    {
        var svc = Svc();
        var demoPath = Path.Combine(_dir, "data", "BACKLOG.demo.yaml");

        var resolved = svc.ResolveBacklogPath(overridePath: null, demoPath);

        Assert.Equal(Path.GetFullPath(demoPath), resolved);
        Assert.True(svc.Load().IsDemo);
    }

    [Fact]
    public void ResolveBacklogPath_prefers_the_configured_path_once_set()
    {
        var svc = Svc();
        var target = Path.Combine(_dir, "BACKLOG.yaml");
        svc.SetBacklogPath(target);

        var resolved = svc.ResolveBacklogPath(overridePath: null, Path.Combine(_dir, "data", "BACKLOG.demo.yaml"));

        Assert.Equal(Path.GetFullPath(target), resolved);
    }
}
