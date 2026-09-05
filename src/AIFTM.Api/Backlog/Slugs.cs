using System.Globalization;
using System.Text;

namespace AIFTM.Api.Backlog;

/// <summary>
/// Turns epic and story titles into the URL segments the app is addressed by, so that
/// "Core Application" → "Checkout and Payment" reads as /core-application/checkout-and-payment.
/// The URL is the breadcrumb; nothing else appears in the path.
///
/// Slugs are generated here, on the server, and travel to the browser as part of the board. There
/// is deliberately no second implementation in JavaScript: two slug algorithms that agree today
/// would eventually disagree, and the disagreement would show up as a dead link.
/// </summary>
public static class Slugs
{
    /// <summary>Paths the app already answers on. An epic titled "Configure" must not take a URL
    /// that belongs to the Configure page, so it is given its number instead.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        // Every literal route Program.cs answers on. "edit-story" was missing, so an epic with that
        // title would have shadowed the form page — the exact collision this list exists to stop.
        "api", "configure", "projects", "releases",
        "add-epic", "add-story", "add-project", "remove-project", "edit-epic", "edit-story",
        "uploads", "vendor", "index.html", "app.js", "app.css", "favicon.ico",
    };

    /// <summary>"Checkout and Payment" → "checkout-and-payment". Accented letters lose their marks
    /// so the path stays typeable; anything that isn't a letter or digit becomes a separator.</summary>
    public static string From(string text)
    {
        var normalized = (text ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    /// <summary>
    /// Assigns each title a slug that is unique within its list. Duplicate titles — which a backlog
    /// is free to contain — get a numeric suffix rather than silently sharing a URL.
    /// </summary>
    /// <param name="fallbacks">Used when a title slugs to nothing at all (a title of only
    /// punctuation, say), so every item still gets a usable path.</param>
    /// <param name="topLevel">True for epics, whose slugs sit at the root and so must avoid the
    /// app's own paths.</param>
    public static List<string> Unique(IReadOnlyList<string> titles, IReadOnlyList<string> fallbacks, bool topLevel)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(titles.Count);

        for (var i = 0; i < titles.Count; i++)
        {
            var slug = From(titles[i]);
            if (slug.Length == 0) slug = From(fallbacks[i]);
            if (slug.Length == 0) slug = "item";
            if (topLevel && Reserved.Contains(slug)) slug = $"{slug}-{From(fallbacks[i])}".Trim('-');

            var candidate = slug;
            for (var n = 2; !taken.Add(candidate); n++) candidate = $"{slug}-{n}";
            result.Add(candidate);
        }

        return result;
    }
}
