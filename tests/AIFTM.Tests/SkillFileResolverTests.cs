using AIFTM.Api.Services;

namespace AIFTM.Tests;

public class SkillFileResolverTests
{
    [Fact]
    public void Resolves_a_folder_path_by_appending_SKILL_md()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");

        var result = SkillFileResolver.Resolve(root, "skills/backlog-tooling/");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "skills/backlog-tooling/SKILL.md")), result);
    }

    [Fact]
    public void Leaves_a_path_that_already_ends_in_dot_md_alone()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");

        var result = SkillFileResolver.Resolve(root, "skills/backlog-tooling/SKILL.md");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "skills/backlog-tooling/SKILL.md")), result);
    }

    [Theory]
    [InlineData("../../secrets.txt")]
    [InlineData("..\\..\\secrets.md")]
    [InlineData("ok/../../../elsewhere/SKILL.md")]
    public void Rejects_paths_that_escape_the_skills_root(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");

        Assert.Null(SkillFileResolver.Resolve(root, path));
    }

    [Fact]
    public void Rejects_an_absolute_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");
        var absolute = OperatingSystem.IsWindows() ? @"C:\Windows\win.md" : "/etc/passwd.md";

        Assert.Null(SkillFileResolver.Resolve(root, absolute));
    }

    [Fact]
    public void Rejects_a_neighbour_that_merely_shares_a_prefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");

        Assert.Null(SkillFileResolver.Resolve(root, "../projX/SKILL.md"));
    }

    [Fact]
    public void A_bare_dot_means_the_roots_own_SKILL_md()
    {
        // Unlike StoryFolder.DirectoryFor, this always resolves to a *file* — "." becomes root/SKILL.md,
        // which is legitimately inside the root. There is no directory here to delete by mistake.
        var root = Path.Combine(Path.GetTempPath(), "proj");

        var result = SkillFileResolver.Resolve(root, ".");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "SKILL.md")), result);
    }

    [Fact]
    public void Returns_null_for_an_empty_path()
    {
        Assert.Null(SkillFileResolver.Resolve(Path.GetTempPath(), ""));
    }
}
