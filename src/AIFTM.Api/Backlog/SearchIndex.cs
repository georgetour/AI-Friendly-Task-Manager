using AIFTM.Api.Services;

namespace AIFTM.Api.Backlog;

/// <param name="Kind">What part of the story this text came from — shown beside the excerpt so a
/// hit in a test case does not read like a hit in the description.</param>
public sealed record SearchSection(string Kind, string Text);

/// <param name="Kind">"epic" or "story". An epic is a destination in its own right — it has a page
/// and a URL — so it is findable by name rather than only through the stories underneath it.</param>
/// <param name="StoryCount">Stories in this epic, for an epic entry. Zero for a story.</param>
public sealed record SearchEntry(
    string Kind,
    string Code,
    string Title,
    string EpicTitle,
    string EpicSlug,
    string StorySlug,
    string Status,
    int StoryCount,
    IReadOnlyList<SearchSection> Sections);

/// <summary>
/// Everything searchable, flattened, built fresh when someone opens search.
///
/// Built on demand rather than kept up to date: the files change on every click, so a stored index
/// would be wrong the moment a task is ticked — the same argument that removed the STATUS-SUMMARY
/// block. Reading every story folder is the cost, and it is the cost Sync already pays: 236 ms at
/// 1,000 stories across 3,000 files, single-digit milliseconds at the size a real backlog is.
///
/// The browser then filters this in memory, so typing costs nothing and hits no endpoint.
/// </summary>
public static class SearchIndex
{
    public static IReadOnlyList<SearchEntry> Build(BacklogService svc)
    {
        var board = svc.GetBoard();
        var skillsRoot = svc.SkillsRoot;
        var entries = new List<SearchEntry>();

        foreach (var epic in board.Epics)
        {
            // The epic first, then its stories — so results read in the order the board does.
            // Without this an epic was only reachable through a story that happened to be in it,
            // and an epic with no stories yet was not findable at all.
            entries.Add(new SearchEntry("epic", "", epic.Title, "", epic.Slug, "", "",
                                        epic.Stories.Count, []));

            foreach (var story in epic.Stories)
            {
                var sections = new List<SearchSection>();

                // A story whose folder is missing or unreadable still belongs in the index — you
                // should be able to find it by title in order to go and fix it.
                sections.AddRange(Safe(() => Description(skillsRoot, story.Folder)));
                sections.AddRange(Safe(() => StoryFolder.ReadTasks(skillsRoot, story.Folder)
                    .Select(t => new SearchSection("task", t.Text))));
                sections.AddRange(Safe(() => StoryFolder.ReadTestCases(skillsRoot, story.Folder)
                    .Select(t => new SearchSection("test case", t.Text))));

                entries.Add(new SearchEntry("story", story.Code, story.Title, epic.Title,
                                            epic.Slug, story.Slug, story.Status, 0, sections));
            }
        }

        return entries;
    }

    /// <summary>SKILL.md as paragraphs. Split rather than sent whole so an excerpt can be built
    /// around the hit instead of returning a wall of prose, and so headings stay with their text.</summary>
    private static IEnumerable<SearchSection> Description(string skillsRoot, string folder)
    {
        var path = StoryFolder.SkillPath(skillsRoot, folder);
        if (!File.Exists(path)) return [];

        var text = File.ReadAllText(path).Replace("\r\n", "\n");

        // Frontmatter is machine metadata, not something anyone searches for by name.
        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end > 0) text = text[(end + 4)..];
        }

        return text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                   .Select(p => p.Replace("\n", " ").Trim())
                   // Headings are the file's structure, not the story's content: SKILL.md is always
                   // Description, Tasks and Acceptance Criteria, so "# Backlog Board" and
                   // "## Description" are a title already on screen and a word every story has.
                   .Where(p => p.Length > 0 && !p.StartsWith('#'))
                   .Select(p => new SearchSection("description", p));
    }

    /// <summary>One unreadable folder must not empty the whole index — the story still gets listed
    /// by title, and Sync is where broken files are reported.</summary>
    private static List<SearchSection> Safe(Func<IEnumerable<SearchSection>> read)
    {
        try { return read().ToList(); }
        catch (Exception) { return []; }
    }
}
