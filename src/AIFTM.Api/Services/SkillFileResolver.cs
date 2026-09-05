using AIFTM.Api.Backlog;

namespace AIFTM.Api.Services;

/// <summary>
/// Resolves a story's skill path (e.g. "backlog-board/SKILL.md", sent by the browser) to an absolute
/// path under the configured skills root. Returns null for anything that would escape that root.
/// </summary>
public static class SkillFileResolver
{
    public static string? Resolve(string skillsRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var trimmed = PathSafety.NormaliseSeparators(relativePath.Trim());
        if (!trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            trimmed = Path.Combine(trimmed, "SKILL.md");

        var rootFull = Path.GetFullPath(skillsRoot);

        string candidate;
        try { candidate = Path.GetFullPath(Path.Combine(rootFull, trimmed)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { return null; }

        // Shared with StoryFolder.DirectoryFor so the two cannot drift apart — see PathSafety for why this
        // is not a string prefix comparison.
        return PathSafety.IsInside(rootFull, candidate) ? candidate : null;
    }
}
