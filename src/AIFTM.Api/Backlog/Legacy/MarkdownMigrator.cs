namespace AIFTM.Api.Backlog.Legacy;

public sealed record MigrationResult(int Epics, int Stories, int FoldersCreated, IReadOnlyList<string> Notes);

/// <summary>
/// One-shot import of an old BACKLOG.md into the YAML layout: reads with the legacy markdown
/// parser, writes the index plus one folder per story.
///
/// Refuses to overwrite an existing BACKLOG.yaml, and never overwrites a SKILL.md — a description
/// already written is not ours to replace. Where the old file pointed at a skill file somewhere
/// else, that path comes back as a note rather than being copied blindly.
/// </summary>
public static class MarkdownMigrator
{
    public static MigrationResult Run(string markdownPath, string outputBacklog, string skillsRoot)
    {
        if (!File.Exists(markdownPath))
            throw new BacklogValidationException($"No markdown backlog at {markdownPath}.");
        if (File.Exists(outputBacklog))
            throw new BacklogValidationException(
                $"{outputBacklog} already exists. Move it aside first — migration will not overwrite it.");

        var legacy = MarkdownBacklogParser.Parse(File.ReadAllText(markdownPath));
        var notes = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = 0;
        var epics = new List<Epic>();

        foreach (var e in legacy.Epics)
        {
            var stories = new List<Story>();
            foreach (var s in e.Stories)
            {
                var baseSlug = Slugs.From(s.Title);
                if (baseSlug.Length == 0) baseSlug = Slugs.From(s.Code);
                if (baseSlug.Length == 0) baseSlug = "story";

                var folder = baseSlug;
                for (var n = 2; !used.Add(folder); n++) folder = $"{baseSlug}-{n}";

                StoryFolder.Create(skillsRoot, folder, s.Code, s.Title);
                folders++;

                StoryFolder.WriteTasks(skillsRoot, folder,
                    s.Tasks.Select(t => new TaskItem(t.Text, t.Done)).ToList());
                StoryFolder.WriteTestCases(skillsRoot, folder,
                    s.TestCases.Select(t => new TestCase(t.Text, NormaliseTestStatus(t.StatusLabel))).ToList());

                if (!string.IsNullOrWhiteSpace(s.SkillPath))
                    notes.Add($"{s.Code}: its old skill file was {s.SkillPath} — copy it over "
                            + $"skills/{folder}/SKILL.md if you want to keep it.");

                stories.Add(new Story(s.Code, s.Title, NormaliseStatus(s.StatusLabel), s.Release, folder));
            }
            epics.Add(new Epic(e.Number, e.Title, stories));
        }

        var board = new Board(
            Path.GetFileNameWithoutExtension(markdownPath),
            legacy.RoadmapVersions,
            epics);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputBacklog))!);
        File.WriteAllText(outputBacklog, YamlIndex.Write(board));

        return new MigrationResult(epics.Count, epics.Sum(x => x.Stories.Count), folders, notes);
    }

    private static string NormaliseStatus(string label) =>
        Match(label, BacklogValidation.Statuses) ?? "Not Yet Started";

    private static string NormaliseTestStatus(string label) =>
        Match(label, BacklogValidation.TestStatuses) ?? "Not Run";

    /// <summary>The old files stored "✅ Done"; the new ones store "Done". Anything unrecognisable
    /// falls back to the neutral value rather than writing a status the app would then reject.</summary>
    private static string? Match(string label, IReadOnlyList<string> allowed)
    {
        var text = new string((label ?? "").Where(c => char.IsLetter(c) || c == ' ').ToArray()).Trim();
        return allowed.FirstOrDefault(a => a.Equals(text, StringComparison.OrdinalIgnoreCase));
    }
}
