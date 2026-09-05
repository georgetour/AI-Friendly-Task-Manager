using System.Text;
using AIFTM.Api.Backlog;

namespace AIFTM.Api.Services;

/// <summary>
/// Read/write gateway over the backlog. Both paths resolve fresh on every call, so pointing the
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

    public Board GetBoard()
    {
        lock (_lock) return Read();
    }

    public StoryDetail GetStory(string code)
    {
        lock (_lock)
        {
            var (_, story) = Locate(Read(), code);
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
            var board = Read();
            var (epic, story) = Locate(board, code);
            return Save(Replace(board, epic, story with { Status = status }));
        }
    }

    /// <summary>Marks which epic you are working in. Stored, not inferred — it is a statement about
    /// intent, and the statuses cannot know that you are about to start on something.</summary>
    public Board SetCurrentEpic(int number)
    {
        lock (_lock)
        {
            var board = Read();
            if (!board.Epics.Any(e => e.Number == number))
                throw new BacklogValidationException($"There is no epic {number}.");

            return Save(board with { CurrentEpic = number });
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
            var (_, story) = Locate(Read(), code);
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
            var (_, story) = Locate(Read(), code);
            StoryFolder.WriteTestCases(resolveSkills(), story.Folder, clean);
        }
    }

    public Board AddEpic(int number, string title)
    {
        if (number < 0 || number > 999)
            throw new BacklogValidationException("Use an epic number between 0 and 999.");
        title = Require(title, "Give the epic a title.", 120);

        lock (_lock)
        {
            var board = Read();
            if (board.Epics.Any(e => e.Number == number))
                throw new BacklogValidationException($"Epic {number} already exists. Pick another number.");

            var epics = board.Epics.Append(new Epic(number, title, new List<Story>())).ToList();
            return Save(board with { Epics = epics });
        }
    }

    public Board RenameEpic(int number, string title)
    {
        title = Require(title, "Give the epic a title.", 120);

        lock (_lock)
        {
            var board = Read();
            if (board.Epics.All(e => e.Number != number))
                throw new BacklogValidationException($"There is no epic {number}.");

            return Save(board with
            {
                Epics = board.Epics.Select(e => e.Number == number ? e with { Title = title } : e).ToList(),
            });
        }
    }

    public Board DeleteEpic(int number)
    {
        lock (_lock)
        {
            var board = Read();
            var epic = board.Epics.FirstOrDefault(e => e.Number == number)
                ?? throw new BacklogValidationException($"There is no epic {number}.");

            var saved = Save(board with { Epics = board.Epics.Where(e => e.Number != number).ToList() });

            foreach (var story in epic.Stories) TryDeleteFolder(story.Folder);
            return saved;
        }
    }

    public Board AddStory(int epicNumber, string code, string title, string? release, string? description = null)
    {
        code = Require(code, "Give the story a code, for example US-25.", 20);
        title = Require(title, "Give the story a title.", 120);
        release = (release ?? "").Trim();

        lock (_lock)
        {
            var board = Read();
            var epic = board.Epics.FirstOrDefault(e => e.Number == epicNumber)
                ?? throw new BacklogValidationException($"There is no epic {epicNumber} to add this story to.");

            if (board.Epics.SelectMany(e => e.Stories).Any(s => s.Code == code))
                throw new BacklogValidationException($"{code} is already used. Pick another code.");

            var folder = FindFreeFolderName(board, title, code);
            StoryFolder.Create(resolveSkills(), folder, code, title, description);

            var story = new Story(code, title, "Not Yet Started", release, folder);
            return Save(Replace(board, epic with { Stories = epic.Stories.Append(story).ToList() }));
        }
    }

    /// <summary>Renames a story and sets its release. The folder is deliberately left where it is:
    /// it is recorded explicitly in the index, so a rename cannot orphan it — and moving a directory
    /// someone may have open is a far worse failure than a folder whose name has drifted from its
    /// title.</summary>
    public Board EditStory(string code, string title, string? release)
    {
        title = Require(title, "Give the story a title.", 120);
        release = (release ?? "").Trim();

        lock (_lock)
        {
            var board = Read();
            var (epic, story) = Locate(board, code);
            return Save(Replace(board, epic, story with { Title = title, Release = release }));
        }
    }

    public Board DeleteStory(string code)
    {
        lock (_lock)
        {
            var board = Read();
            var (epic, story) = Locate(board, code);

            var saved = Save(Replace(board,
                epic with { Stories = epic.Stories.Where(s => s.Code != code).ToList() }));

            TryDeleteFolder(story.Folder);
            return saved;
        }
    }

    // ------------------------------------------------------------------ helpers --

    private Board Read() => YamlIndex.Parse(File.ReadAllText(resolveBacklog()));

    private Board Save(Board board)
    {
        var yaml = YamlIndex.Write(board);
        File.WriteAllText(resolveBacklog(), yaml, Utf8NoBom);
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
}
