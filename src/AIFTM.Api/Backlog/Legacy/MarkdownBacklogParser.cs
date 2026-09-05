using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AIFTM.Api.Backlog.Legacy;

/// <summary>What the markdown backlog held, in the shape the old file used. Only the migrator
/// consumes these — the running app never sees them.</summary>
public sealed record LegacyBoard(IReadOnlyList<LegacyEpic> Epics, IReadOnlyList<string> RoadmapVersions);

public sealed record LegacyEpic(int Number, string Title, IReadOnlyList<LegacyStory> Stories);

public sealed record LegacyStory(string Code, string Title, string StatusLabel, string Release,
                                string? SkillPath, IReadOnlyList<LegacyTask> Tasks,
                                IReadOnlyList<LegacyTestCase> TestCases);

public sealed record LegacyTask(string Text, bool Done);

public sealed record LegacyTestCase(string Text, string StatusLabel);

/// <summary>
/// The original BACKLOG.md reader, kept for one job only: importing a markdown backlog into the
/// YAML layout via `dotnet run -- migrate`. Nothing in the running app calls this. It keeps its
/// tests because the import has to stay trustworthy for anyone upgrading.
///
/// Line locators are gone — nothing writes markdown any more, so there is nothing to locate.
/// </summary>
public static partial class MarkdownBacklogParser
{
    public static LegacyBoard Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        var epics = new List<LegacyEpic>();
        var roadmapVersions = new List<string>();
        EpicBuilder? epic = null;
        StoryBuilder? story = null;
        Section section = Section.None;

        // Test-case table column discovery (shape varies: some tables lead with TC|Verifies|Scenario|Status,
        // others with ID|Description|Status|Notes, and Epic 2 uses a "### Validation" heading).
        int tcStatusCol = -1, tcDescCol = -1, tcIdCol = 0;
        bool tcHeaderSeen = false;

        void FlushStory()
        {
            if (story is not null) epic!.Stories.Add(story.Build());
            story = null;
        }
        void FlushEpic()
        {
            FlushStory();
            if (epic is not null) epics.Add(epic.Build());
            epic = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var mRoadmap = RoadmapRow().Match(line);
            if (mRoadmap.Success && !roadmapVersions.Contains(mRoadmap.Groups[1].Value))
                roadmapVersions.Add(mRoadmap.Groups[1].Value);

            var mEpic = EpicHeading().Match(line);
            if (mEpic.Success)
            {
                FlushEpic();
                // Invariant: the epic number came out of a regex on a file, not from a person
                // typing in their own locale.
                epic = new EpicBuilder(int.Parse(mEpic.Groups[1].Value, CultureInfo.InvariantCulture),
                                       mEpic.Groups[2].Value.Trim());
                section = Section.None;
                continue;
            }

            var mStory = StoryHeading().Match(line);
            if (mStory.Success && epic is not null)
            {
                FlushStory();
                story = new StoryBuilder(mStory.Groups[1].Value, mStory.Groups[2].Value.Trim());
                section = Section.None;
                continue;
            }

            if (story is not null && StatusLine().IsMatch(line))
            {
                var m = StatusLine().Match(line);
                story.StatusLabel = m.Groups[2].Value.Trim();
                var skill = SkillField().Match(line);
                if (skill.Success) story.SkillPath = skill.Groups[1].Value.Trim();
                var rel = ReleaseField().Match(line);
                if (rel.Success) story.Release = rel.Groups[1].Value;
                continue;
            }

            // Ordinal throughout: these are markdown keywords, not words in the user's language, and
            // a culture-aware comparison makes them behave differently on a Turkish machine.
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                var h = line[4..].Trim();
                section = h.StartsWith("Tasks", StringComparison.Ordinal) ? Section.Tasks
                    : (h.StartsWith("Test Cases", StringComparison.Ordinal)
                    || h.StartsWith("Validation", StringComparison.Ordinal)) ? Section.TestCases
                    : Section.None;
                tcHeaderSeen = false; tcStatusCol = -1; tcDescCol = -1; tcIdCol = 0;
                continue;
            }

            if (story is null || section == Section.None || !line.TrimStart().StartsWith('|')) continue;

            var cells = SplitRow(line);
            if (cells.Count == 0 || IsSeparatorRow(cells)) continue;

            if (section == Section.Tasks)
            {
                // Data row = first cell is a task id like "21.0" / "5.1"; skips the "| # | Task | ✓ |" header.
                if (cells.Count >= 3 && TaskId().IsMatch(cells[0]))
                {
                    var done = cells[^1].Contains('✅');
                    story.Tasks.Add(new LegacyTask(cells[1], done));
                }
                continue;
            }

            // Section.TestCases — first pipe row is the header; find the Status/Description columns by name.
            if (!tcHeaderSeen)
            {
                for (var c = 0; c < cells.Count; c++)
                {
                    var name = cells[c].ToLowerInvariant();
                    if (name == "status") tcStatusCol = c;
                    else if (name is "description" or "scenario" or "scenario (do → expect)") tcDescCol = c;
                }
                if (tcDescCol < 0) tcDescCol = Math.Min(1, cells.Count - 1);
                tcHeaderSeen = true;
                continue;
            }

            if (tcStatusCol >= 0 && tcStatusCol < cells.Count && cells.Count > tcIdCol)
            {
                var desc = tcDescCol < cells.Count ? cells[tcDescCol] : "";
                story.TestCases.Add(new LegacyTestCase(desc, ParseStatusLabel(cells[tcStatusCol])));
            }
        }

        FlushEpic();
        return new LegacyBoard(epics, roadmapVersions);
    }

    /// <summary>Splits a markdown table row into trimmed cells, honoring escaped "\|" pipes.</summary>
    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        var cells = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < t.Length; i++)
        {
            var ch = t[i];
            if (ch == '\\' && i + 1 < t.Length && t[i + 1] == '|') { sb.Append('|'); i++; }
            else if (ch == '|') { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        if (sb.Length > 0) cells.Add(sb.ToString().Trim());
        // A row "| a | b |" yields ["", "a", "b", ""] -> drop leading/trailing empties from the pipe borders.
        if (cells.Count > 0 && cells[0].Length == 0) cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static bool IsSeparatorRow(List<string> cells) =>
        cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':' or ' '));

    /// <summary>"✅ Passed" -> "Passed". The emoji was presentation even in the old format.</summary>
    private static string ParseStatusLabel(string cell)
    {
        var t = cell.Trim();
        var sp = t.IndexOf(' ');
        return sp < 0 ? t : t[(sp + 1)..].Trim();
    }

    private enum Section { None, Tasks, TestCases }

    private sealed class EpicBuilder(int number, string title)
    {
        public List<LegacyStory> Stories { get; } = new();
        public LegacyEpic Build() => new(number, title, Stories);
    }

    private sealed class StoryBuilder(string code, string title)
    {
        public string StatusLabel { get; set; } = "";
        public string Release { get; set; } = "";
        public string? SkillPath { get; set; }
        public List<LegacyTask> Tasks { get; } = new();
        public List<LegacyTestCase> TestCases { get; } = new();
        public LegacyStory Build() => new(code, title, StatusLabel, Release, SkillPath, Tasks, TestCases);
    }

    [GeneratedRegex(@"^#\s+Epic\s+(\d+):\s*(.+?)\s*$")]
    private static partial Regex EpicHeading();

    [GeneratedRegex(@"^##\s+(US-\d+)\s+·\s+(.+?)\s*$")]
    private static partial Regex StoryHeading();

    [GeneratedRegex(@"^>\s*\*\*Status\*\*:\s*(\S+)\s+(.+?)\s*·")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"\*\*Skill\*\*:\s*`?([^`·\n]+?)`?\s*(?:·|$)")]
    private static partial Regex SkillField();

    // Matches "**Release**: V1" and "**When**: V3" — both carry a version in BACKLOG.md.
    [GeneratedRegex(@"\*\*(?:Release|When)\*\*:\s*(V[\d.]+)")]
    private static partial Regex ReleaseField();

    [GeneratedRegex(@"^\|\s*(V[\d.]+)\s*\|")]
    private static partial Regex RoadmapRow();

    [GeneratedRegex(@"^\d[\d.]*$")]
    private static partial Regex TaskId();
}
