using AIFTM.Api.Backlog;

namespace AIFTM.Tests;

/// <summary>
/// The trust boundary between a path fragment someone typed into a file and the filesystem.
/// Every case here is something a hand-edited BACKLOG.yaml could actually contain.
/// </summary>
public class PathSafetyTests
{
    private static string Root => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "aiftm-root"));
    private static string Under(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

    [Fact]
    public void A_child_is_inside()
    {
        Assert.True(PathSafety.IsInside(Root, Under("backlog-board")));
    }

    [Fact]
    public void A_nested_child_is_inside()
    {
        Assert.True(PathSafety.IsInside(Root, Under("a/b/c")));
    }

    [Fact]
    public void The_root_itself_is_NOT_inside()
    {
        // A story whose folder resolved to the skills root would take every other story's folder
        // with it when deleted. "." must not read as "inside".
        Assert.False(PathSafety.IsInside(Root, Root));
        Assert.False(PathSafety.IsInside(Root, Under(".")));
    }

    [Fact]
    public void The_parent_is_not_inside()
    {
        Assert.False(PathSafety.IsInside(Root, Under("..")));
    }

    [Fact]
    public void A_sibling_is_not_inside()
    {
        Assert.False(PathSafety.IsInside(Root, Under("../aiftm-root-evil")));
    }

    [Fact]
    public void A_traversal_that_climbs_out_and_back_down_is_not_inside()
    {
        Assert.False(PathSafety.IsInside(Root, Under("a/../../elsewhere")));
    }

    [Fact]
    public void A_prefix_neighbour_is_not_inside()
    {
        // "aiftm-rootx" shares a string prefix with "aiftm-root" but is a different directory. This is
        // what the separator in the old prefix check was guarding, and it must keep working.
        Assert.False(PathSafety.IsInside(Root, Root + "x"));
    }

    [Fact]
    public void A_path_on_another_root_is_not_inside()
    {
        var elsewhere = OperatingSystem.IsWindows() ? @"Z:\somewhere\else" : "/somewhere/else";

        Assert.False(PathSafety.IsInside(Root, Path.GetFullPath(elsewhere)));
    }

    [Fact]
    public void Case_is_treated_the_way_the_platform_treats_it()
    {
        var shouted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AIFTM-ROOT", "child"));

        // On Windows the filesystem is case-insensitive, so AIFTM-ROOT and aiftm-root are the same
        // directory and this genuinely is inside. On Linux they are two different directories, and
        // treating them as one is exactly the hole a fixed OrdinalIgnoreCase comparison opened.
        Assert.Equal(OperatingSystem.IsWindows(), PathSafety.IsInside(Root, shouted));
    }
}
