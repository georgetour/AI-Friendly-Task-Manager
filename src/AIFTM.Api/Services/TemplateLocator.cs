namespace AIFTM.Api.Services;

/// <summary>Finds the repo's templates/ folder by walking up from the working directory or app base
/// directory — mirrors BacklogLocator's search so `dotnet run` from the repo just works.</summary>
public static class TemplateLocator
{
    public static string Find(string fileName)
    {
        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(seed);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "templates", fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        throw new FileNotFoundException($"templates/{fileName} not found — expected at the repo root.");
    }
}
