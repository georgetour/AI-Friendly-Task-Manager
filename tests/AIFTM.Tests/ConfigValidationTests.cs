using AIFTM.Api.Backlog;
using AIFTM.Api.Services;

namespace AIFTM.Tests;

/// <summary>
/// The browser checks these too, but the API is reachable without the UI — so every rule has to
/// hold on the server. These tests are what stop the frontend being the only thing enforcing them.
/// </summary>
public class ConfigValidationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ---------- backlog path ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Backlog_path_cannot_be_empty(string? path)
    {
        var ex = Assert.Throws<BacklogValidationException>(() => AppConfigService.ValidateBacklogPath(path));
        Assert.Contains("Enter the path", ex.Message);
    }

    [Theory]
    [InlineData("C:/projects/app/BACKLOG.txt")]
    [InlineData("C:/projects/app/BACKLOG")]
    [InlineData("C:/projects/app/notes.markdown")]
    [InlineData("C:/projects/app/BACKLOG.md")]   // the old format is no longer accepted
    public void Backlog_path_must_be_a_yaml_file(string path)
    {
        var ex = Assert.Throws<BacklogValidationException>(() => AppConfigService.ValidateBacklogPath(path));
        Assert.Contains(".yaml file", ex.Message);
    }

    [Theory]
    [InlineData("C:/projects/app/BACKLOG.yaml")]
    [InlineData("C:/projects/app/backlog.yml")]
    public void Backlog_path_accepts_either_yaml_extension(string path)
    {
        Assert.Equal(Path.GetFullPath(path), AppConfigService.ValidateBacklogPath(path));
    }

    [Fact]
    public void Backlog_path_rejects_an_existing_folder()
    {
        // A folder named like a file is the realistic mistake this guards against.
        var folder = Path.Combine(_dir, "BACKLOG.yaml");
        Directory.CreateDirectory(folder);

        var ex = Assert.Throws<BacklogValidationException>(() => AppConfigService.ValidateBacklogPath(folder));
        Assert.Contains("folder", ex.Message);
    }

    [Fact]
    public void Backlog_path_accepts_a_valid_file_and_returns_it_absolute()
    {
        var path = Path.Combine(_dir, "sub", "BACKLOG.yaml");

        var result = AppConfigService.ValidateBacklogPath(path);

        Assert.Equal(Path.GetFullPath(path), result);
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void Backlog_path_accepts_uppercase_extension()
    {
        var path = Path.Combine(_dir, "BACKLOG.YAML");
        Assert.Equal(Path.GetFullPath(path), AppConfigService.ValidateBacklogPath(path));
    }

    // ---------- skills path ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Skills_path_cannot_be_empty(string? path)
    {
        Assert.Throws<BacklogValidationException>(() => AppConfigService.ValidateSkillsPath(path));
    }

    [Fact]
    public void Skills_path_must_exist()
    {
        var missing = Path.Combine(_dir, "not-created-yet");

        var ex = Assert.Throws<BacklogValidationException>(() => AppConfigService.ValidateSkillsPath(missing));
        Assert.Contains("doesn't exist", ex.Message);
    }

    [Fact]
    public void Skills_path_rejects_a_file()
    {
        var file = Path.Combine(_dir, "notes.yaml");
        File.WriteAllText(file, "x");

        var ex = Assert.Throws<BacklogValidationException>(() => AppConfigService.ValidateSkillsPath(file));
        Assert.Contains("file", ex.Message);
    }

    [Fact]
    public void Skills_path_accepts_an_existing_folder()
    {
        var result = AppConfigService.ValidateSkillsPath(_dir);

        Assert.Equal(Path.GetFullPath(_dir), result);
    }

    // ---------- the service applies them ----------

    [Fact]
    public void SetBacklogPath_refuses_an_invalid_path_and_writes_nothing()
    {
        var configPath = Path.Combine(_dir, "aiftm.config.json");
        var svc = new AppConfigService(configPath);

        Assert.Throws<BacklogValidationException>(() => svc.SetBacklogPath("not-a-yaml-file.txt"));
        Assert.False(File.Exists(configPath));
    }

    [Fact]
    public void SetSkillsPath_refuses_a_missing_folder_and_writes_nothing()
    {
        var configPath = Path.Combine(_dir, "aiftm.config.json");
        var svc = new AppConfigService(configPath);

        Assert.Throws<BacklogValidationException>(() => svc.SetSkillsPath(Path.Combine(_dir, "nope")));
        Assert.False(File.Exists(configPath));
    }
}
