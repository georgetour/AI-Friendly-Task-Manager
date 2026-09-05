namespace AIFTM.Tests;

/// <summary>Locates the bundled sample BACKLOG.md fixture used by tests that need a full, realistic
/// file (multiple epics, both test-case table shapes, the "### Validation" heading, an epic with zero
/// stories, and all 7 status values).</summary>
internal static class TestBacklogLocator
{
    public static string Resolve()
    {
        var fixture = FindUp("tests/AIFTM.Tests/Fixtures/BACKLOG.sample.md");
        if (fixture is null)
            throw new FileNotFoundException("Bundled BACKLOG.sample.md fixture not found — this repo is not self-contained without it.");
        return fixture;
    }

    private static string? FindUp(string relativeSuffix)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativeSuffix);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
