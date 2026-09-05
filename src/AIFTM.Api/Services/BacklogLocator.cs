namespace AIFTM.Api.Services;

/// <summary>Finds a BACKLOG.md by walking up from the working directory (so `dotnet run` from inside
/// a project that already has one at its root just works, no config needed).</summary>
public static class BacklogLocator
{
    public static string? FindOrNull()
    {
        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(seed);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "BACKLOG.md");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }
}
