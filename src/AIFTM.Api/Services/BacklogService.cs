using System.Text;
using AIFTM.Api.Backlog;

namespace AIFTM.Api.Services;

/// <summary>
/// Read/write gateway over the backlog. Both paths resolve fresh for every operation, so pointing the
/// tracker somewhere else through Configure takes effect with no restart. Writes are serialized
/// behind a lock and are whole-file: deserialize, mutate, serialize. There is no surgical text
/// editing because the app is the only writer.
///
/// A status write touches the index; a task write touches that story's tasks.yaml. Never both — so
/// there is no cross-file transaction to get wrong. Deleting is the one exception, and it removes
/// the index entry first: that is the source of truth, and a leftover folder is a warning Sync
/// reports rather than a story pointing at nothing.
/// </summary>
public sealed class BacklogService(Func<string> resolveBacklog, Func<string> resolveSkills)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _lock = new();

    public string BacklogPath => resolveBacklog();
    public string SkillsRoot => resolveSkills();

    /// <summary>
    /// The board, converting the file first if it is still in the shape that kept the release on each
    /// story. Written back rather than lifted only in memory: otherwise every read would redo the work
    /// and the file would stay one shape behind for good.
    ///
    /// The write is the same whole-file save every click already performs, and it happens once — the
    /// converted file parses with nothing left to lift.
    ///
    /// Except when the file has comments. A write cannot keep them, and before conversion existed a read
    /// never wrote, so a read never deleted one. The board is shown converted all the same, Sync says
    /// why the file is still in the old shape, and the next click writes it as every click always has.
    /// </summary>
    public Board GetBoard()
    {
        lock (_lock)
        {
            var path = resolveBacklog();
            var text = File.ReadAllText(path);
            var board = YamlIndex.Parse(text);
            if (!board.Migrated || YamlIndex.HasComments(text)) return board;

            // A backlog this process cannot write — a read-only checkout, a file another program has
            // locked — must still open. Before conversion existed a read never wrote, so failing here
            // would turn a board that opened yesterday into a 422 today. The lifted board is correct in
            // memory; the conversion is simply tried again on the next read or the next click.
            Board saved;
            try { saved = Save(path, board); }
            catch (IOException) { return board; }
            catch (UnauthorizedAccessException) { return board; }

            // Said once, by the write that did it, and kept nowhere: once the file is converted there is
            // no longer any record of which release a story named, so this is the only moment it can
            // be said. A story whose own release differs from its epic's is a plan that just changed.
            foreach (var move in board.ReleaseMoves) Console.WriteLine(move);
            return saved;
        }
    }

    public StoryDetail GetStory(string code)
    {
        lock (_lock)
        {
            var (_, story) = Locate(Read(resolveBacklog()), code);
            return StoryFolder.Read(resolveSkills(), story.Folder);
        }
    }

    public ValidationReport Validate() => BacklogValidation.Check(resolveBacklog(), resolveSkills());

    public Board SetStoryStatus(string code, string status)
    {
        status = (status ?? "").Trim();
        if (!BacklogValidation.Statuses.Contains(status))
            throw new BacklogValidationException(
                $"\"{status}\" is not a status. Use one of: {string.Join(", ", BacklogValidation.Statuses)}.");

        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            var (epic, story) = Locate(board, code);
            return Save(path, Replace(board, epic, story with { Status = status }));
        }
    }

    /// <summary>Marks which epic you are working in. Stored, not inferred — it is a statement about
    /// intent, and the statuses cannot know that you are about to start on something.</summary>
    public Board SetCurrentEpic(int number)
    {
        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            if (!board.Epics.Any(e => e.Number == number))
                throw new BacklogValidationException($"There is no epic {number}.");

            return Save(path, board with { CurrentEpic = number });
        }
    }

    /// <summary>A story with hundreds of tasks is a story that should have been split. The cap is
    /// far above anything the UI can produce, so hitting it means something is wrong rather than
    /// someone being thorough — and it stops one request writing a megabyte of YAML.</summary>
    private const int MaxItems = 200;

    public void SetTasks(string code, IReadOnlyList<TaskItem> tasks)
    {
        if (tasks.Count > MaxItems)
            throw new BacklogValidationException(
                $"A story can hold up to {MaxItems} tasks. Split it into more than one story.");

        var clean = tasks.Select(t => new TaskItem(Require(t.Text, "A task needs some text.", 500), t.Done)).ToList();

        lock (_lock)
        {
            var (_, story) = Locate(Read(resolveBacklog()), code);
            StoryFolder.WriteTasks(resolveSkills(), story.Folder, clean);
        }
    }

    public void SetTestCases(string code, IReadOnlyList<TestCase> cases)
    {
        if (cases.Count > MaxItems)
            throw new BacklogValidationException(
                $"A story can hold up to {MaxItems} test cases. Split it into more than one story.");

        var clean = cases.Select(c =>
        {
            var status = (c.Status ?? "").Trim();
            if (!BacklogValidation.TestStatuses.Contains(status))
                throw new BacklogValidationException(
                    $"\"{status}\" is not a test-case status. Use {string.Join(", ", BacklogValidation.TestStatuses)}.");
            return new TestCase(Require(c.Text, "A test case needs some text.", 500), status);
        }).ToList();

        lock (_lock)
        {
            var (_, story) = Locate(Read(resolveBacklog()), code);
            StoryFolder.WriteTestCases(resolveSkills(), story.Folder, clean);
        }
    }

    public Board AddEpic(int number, string title, string? version, string? release)
    {
        if (number < 0 || number > 999)
            throw new BacklogValidationException("Use an epic number between 0 and 999.");
        title = Require(title, "Give the epic a title.", 120);
        version = Optional(version, 20);
        release = Optional(release, 20);

        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            if (board.Epics.Any(e => e.Number == number))
                throw new BacklogValidationException($"Epic {number} already exists. Pick another number.");

            var epics = board.Epics.Append(new Epic(number, version, release, title, new List<Story>())).ToList();
            return Save(path, board with { Epics = epics });
        }
    }

    /// <summary>Changes an epic's title, version and release. The number, its stories and their
    /// order are never touched — an edit here has no way to reach them.</summary>
    public Board EditEpic(int number, string title, string? version, string? release)
    {
        title = Require(title, "Give the epic a title.", 120);
        version = Optional(version, 20);
        release = Optional(release, 20);

        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            if (board.Epics.All(e => e.Number != number))
                throw new BacklogValidationException($"There is no epic {number}.");

            return Save(path, board with
            {
                Epics = board.Epics.Select(e => e.Number == number
                    ? e with { Title = title, Version = version, Release = release }
                    : e).ToList(),
            });
        }
    }

    public Board DeleteEpic(int number)
    {
        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            var epic = board.Epics.FirstOrDefault(e => e.Number == number)
                ?? throw new BacklogValidationException($"There is no epic {number}.");

            var saved = Save(path, board with { Epics = board.Epics.Where(e => e.Number != number).ToList() });

            foreach (var story in epic.Stories) TryDeleteFolder(story.Folder);
            return saved;
        }
    }

    public Board AddStory(int epicNumber, string code, string title, string? description = null)
    {
        code = Require(code, "Give the story a code, for example US-25.", 20);
        title = Require(title, "Give the story a title.", 120);

        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            var epic = board.Epics.FirstOrDefault(e => e.Number == epicNumber)
                ?? throw new BacklogValidationException($"There is no epic {epicNumber} to add this story to.");

            if (board.Epics.SelectMany(e => e.Stories).Any(s => s.Code == code))
                throw new BacklogValidationException($"{code} is already used. Pick another code.");

            var folder = FindFreeFolderName(board, title, code);
            StoryFolder.Create(resolveSkills(), folder, code, title, description);

            var story = new Story(code, title, "Not Yet Started", folder);
            return Save(path, Replace(board, epic with { Stories = epic.Stories.Append(story).ToList() }));
        }
    }

    /// <summary>Renames a story. The folder is deliberately left where it is: it is recorded
    /// explicitly in the index, so a rename cannot orphan it — and moving a directory someone may
    /// have open is a far worse failure than a folder whose name has drifted from its title.</summary>
    public Board EditStory(string code, string title)
    {
        title = Require(title, "Give the story a title.", 120);

        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            var (epic, story) = Locate(board, code);
            return Save(path, Replace(board, epic, story with { Title = title }));
        }
    }

    public Board DeleteStory(string code)
    {
        lock (_lock)
        {
            var path = resolveBacklog();
            var board = Read(path);
            var (epic, story) = Locate(board, code);

            var saved = Save(path, Replace(board,
                epic with { Stories = epic.Stories.Where(s => s.Code != code).ToList() }));

            TryDeleteFolder(story.Folder);
            return saved;
        }
    }

    // ------------------------------------------------------------------ helpers --

    // Both take the path rather than asking the resolver, so an operation reads and saves the same file.
    // Switching project changes the resolver's answer under AppConfigService's lock, not this one; a
    // second ask landing after a switch would write one project's board over another project's file.
    private static Board Read(string path) => YamlIndex.Parse(File.ReadAllText(path));

    private static Board Save(string path, Board board)
    {
        var yaml = YamlIndex.Write(board);
        File.WriteAllText(path, yaml, Utf8NoBom);
        return YamlIndex.Parse(yaml);   // re-parse so slugs are assigned from the saved titles
    }

    /// <summary>A folder name nothing else is using. Derived from the title, so it reads like the
    /// story, with a numeric suffix only when two stories would collide.</summary>
    private static string FindFreeFolderName(Board board, string title, string code)
    {
        var used = new HashSet<string>(
            board.Epics.SelectMany(e => e.Stories).Select(s => s.Folder), StringComparer.OrdinalIgnoreCase);

        var baseSlug = Slugs.Unique(new[] { title }, new[] { code }, topLevel: false)[0];
        var candidate = baseSlug;
        for (var n = 2; used.Contains(candidate); n++) candidate = $"{baseSlug}-{n}";
        return candidate;
    }

    private static (Epic, Story) Locate(Board board, string code)
    {
        foreach (var epic in board.Epics)
        {
            var story = epic.Stories.FirstOrDefault(s => s.Code == code);
            if (story is not null) return (epic, story);
        }
        throw new BacklogValidationException($"There is no story {code}.");
    }

    private static Board Replace(Board board, Epic updated) => board with
    {
        Epics = board.Epics.Select(e => e.Number == updated.Number ? updated : e).ToList(),
    };

    private static Board Replace(Board board, Epic epic, Story updated) => Replace(board, epic with
    {
        Stories = epic.Stories.Select(s => s.Code == updated.Code ? updated : s).ToList(),
    });

    private void TryDeleteFolder(string folder)
    {
        // The index entry is already gone, so a failure here leaves an unreferenced folder — which
        // Sync reports as a warning — rather than a story pointing at nothing.
        try { StoryFolder.Delete(resolveSkills(), folder); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (BacklogValidationException) { }
    }

    private static string Require(string? value, string message, int max)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) throw new BacklogValidationException(message);
        if (value.Length > max) throw new BacklogValidationException($"Keep this under {max} characters.");
        return value;
    }

    /// <summary>An optional short label — an epic's version or release. Blank is a real value (not
    /// scheduled, no version), so it is never rejected, only capped.</summary>
    private static string Optional(string? value, int max)
    {
        value = (value ?? "").Trim();
        if (value.Length > max) throw new BacklogValidationException($"Keep this under {max} characters.");
        return value;
    }
}
