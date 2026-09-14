using System.Text.Json.Serialization;

namespace AIFTM.Api.Backlog;

/// <summary>
/// The index: every epic and story, and nothing else. Tasks and test cases live in each story's own
/// folder and are loaded only when that story is opened — which is what keeps a 1000-story board at
/// ~35ms. Holding them here instead measured 270ms, because every board load parsed detail the
/// board never renders.
/// </summary>
/// <param name="CurrentEpic">The epic you said you are working in, by number. Null until someone
/// chooses one, and the board then falls back to inferring it from the statuses — so an existing
/// backlog keeps showing what it showed before anyone knew this field existed.
///
/// One number at the top rather than a flag on each epic: two epics cannot both claim to be current
/// if there is only one place to say it.</param>
/// <param name="Migrated">True when <see cref="YamlIndex.Parse"/> lifted a legacy story-level
/// release onto its epic. Not part of the file — it tells BacklogService the text on disk is now
/// one shape behind the board it just handed back.</param>
public sealed record Board(
    string Project,
    IReadOnlyList<string> Roadmap,
    IReadOnlyList<Epic> Epics,
    int? CurrentEpic = null)
{
    public bool Migrated { get; init; }

    /// <summary>Top-level keys the index does not model, carried through every write. Never sent to
    /// the browser: the board has no use for them, and the file stays the only place they live.</summary>
    [JsonIgnore]
    public UnmodelledKeys Extras { get; init; } = UnmodelledKeys.None;

    /// <summary>The stories whose plan <see cref="YamlIndex.Parse"/> changed when it lifted releases onto
    /// epics. Not part of the file and not kept anywhere: it exists so the one write that converts a
    /// file can say what it regrouped.</summary>
    [JsonIgnore]
    public IReadOnlyList<ReleaseMove> ReleaseMoves { get; init; } = [];
}

/// <summary>A story that now sits in a release it did not name, because its epic adopted that release.</summary>
/// <param name="From">The release the story named; empty when it named none.</param>
public sealed record ReleaseMove(string Code, string From, string To, int EpicNumber, string EpicTitle)
{
    public override string ToString() =>
        $"Moved {Code} from {(From.Length == 0 ? "unscheduled" : "release " + From)} into release {To} "
      + $"with epic {EpicNumber} ({EpicTitle}).";
}

/// <param name="Version">The epic's own version, e.g. "0.3.0". A label, never a key — nothing looks
/// an epic up by it, because <paramref name="Number"/> is the identity.</param>
/// <param name="Release">Which release in the roadmap this epic belongs to. Empty is legitimate:
/// work that is not scheduled yet.</param>
/// <param name="Slug">URL segment, e.g. "core-application". Derived from the title by
/// <see cref="Slugs"/> on every read — never stored in the file, so it can't drift from the title.</param>
public sealed record Epic(
    int Number,
    string Version,
    string Release,
    string Title,
    IReadOnlyList<Story> Stories)
{
    public string Slug { get; init; } = "";

    /// <summary>Keys on this epic the index does not model. See <see cref="Board.Extras"/>.</summary>
    [JsonIgnore]
    public UnmodelledKeys Extras { get; init; } = UnmodelledKeys.None;
}

/// <param name="Status">A plain word: "In Progress", "Done". No emoji — those are presentation and
/// live in the browser.</param>
/// <param name="Folder">Directory under the skills root holding this story's SKILL.md, tasks.yaml
/// and test-cases.yaml. Stored in the file, unlike the slug, because renaming a story must not
/// silently orphan its folder.</param>
public sealed record Story(
    string Code,
    string Title,
    string Status,
    string Folder)
{
    public string Slug { get; init; } = "";

    /// <summary>Keys on this story the index does not model — <c>doc:</c>, <c>blocked_by:</c>, anything.
    /// See <see cref="Board.Extras"/>.</summary>
    [JsonIgnore]
    public UnmodelledKeys Extras { get; init; } = UnmodelledKeys.None;
}

public sealed record TaskItem(string Text, bool Done);

public sealed record TestCase(string Text, string Status);

/// <summary>What a story's folder holds. Loaded on demand, never with the board.</summary>
public sealed record StoryDetail(
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyList<TestCase> TestCases);
