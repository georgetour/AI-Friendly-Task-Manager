using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AIFTM.Api.Backlog;

/// <summary>
/// Reads and writes BACKLOG.yaml.
///
/// Whole-file deserialize → mutate → serialize. The app is the only writer, so there is no surgical
/// text editing to get wrong — which is what let BacklogWriter, BacklogGenerator and SummaryWriter be
/// deleted outright. Serialization is deterministic, so changing one status still shows up as one
/// line in a diff.
///
/// Whole-file does not mean only-what-the-board-reads. A key the index has no field for is carried on
/// the record it sat on and written back after the modelled fields, in the order the file had it.
/// </summary>
public static class YamlIndex
{
    // Shapes YamlDotNet binds to, kept separate from the domain records so a file missing a field
    // is a default rather than an exception, and so the domain never has to model "absent".
    private sealed class IndexDto
    {
        public string Project { get; set; } = "";
        public List<string> Roadmap { get; set; } = new();

        // Nullable so "nobody has chosen one" is a real state and not epic 0, which is a legitimate
        // epic number. Written only once someone has chosen, so an untouched file gains no line.
        public int? CurrentEpic { get; set; }

        public List<EpicDto> Epics { get; set; } = new();

        [YamlIgnore] public UnmodelledKeys Extras { get; set; } = UnmodelledKeys.None;
    }

    private sealed class EpicDto
    {
        public int Number { get; set; }

        // Nullable and left null when empty, so the serializer's OmitNull keeps a backlog that uses
        // neither field byte-identical to what this wrote before they existed.
        public string? Version { get; set; }
        public string? Release { get; set; }

        public string Title { get; set; } = "";
        public List<StoryDto> Stories { get; set; } = new();

        [YamlIgnore] public UnmodelledKeys Extras { get; set; } = UnmodelledKeys.None;
    }

    private sealed class StoryDto
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "Not Yet Started";

        // Legacy input only. A file written before the release moved up to the epic still carries this,
        // and LiftReleasesToEpics reads it; Write never sets it, so OmitNull drops it from the output.
        public string? Release { get; set; }

        public string Folder { get; set; } = "";

        [YamlIgnore] public UnmodelledKeys Extras { get; set; } = UnmodelledKeys.None;
    }

    // What each mapping models. Anything else on it is somebody's data and is carried, not dropped.
    // A story's "release" counts as modelled although nothing writes it: it is lifted onto the epic,
    // and carrying it as an extra would write the old shape straight back out.
    private static readonly HashSet<string> IndexKeys = ["project", "roadmap", "currentEpic", "epics"];
    private static readonly HashSet<string> EpicKeys = ["number", "version", "release", "title", "stories"];
    private static readonly HashSet<string> StoryKeys = ["code", "title", "status", "release", "folder"];

    // Duplicate keys are rejected rather than silently letting the last one win. In a file that IS
    // the database, "epics:" appearing twice would drop a whole epic with no error anywhere — and
    // the integrity checks below could not catch it, because by then the data is already gone.
    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .WithDuplicateKeyChecking()
        .Build();

    // Handed ordered mappings rather than the DTOs, because only a mapping can hold modelled fields and
    // keys nobody declared side by side, in an order chosen here. Modelled values keep their C# types,
    // so they are quoted exactly as the typed DTOs were and a backlog with no extra keys writes the
    // same bytes either way. Extras are parsed YAML nodes, which emit themselves with their original
    // style and tag — "estimate: 5" stays a number and "estimate: '5'" stays a string.
    //
    // No naming convention: every modelled key is spelled out in Write, and a convention would also
    // rewrite an extra such as "blocked_by" into "blockedBy".
    private static readonly ISerializer Writer = new SerializerBuilder().Build();

    public static Board Parse(string yaml)
    {
        var dto = (string.IsNullOrWhiteSpace(yaml) ? null : Reader.Deserialize<IndexDto>(yaml))
                  ?? new IndexDto();

        AttachUnmodelledKeys(dto, yaml);
        var migrated = LiftReleasesToEpics(dto);

        var epics = (dto.Epics ?? new List<EpicDto>()).Where(e => e is not null).Select(e => new Epic(
            e.Number,
            e.Version ?? "",
            e.Release ?? "",
            e.Title ?? "",
            (e.Stories ?? new List<StoryDto>())
                .Where(s => s is not null)
                .Select(s => new Story(s.Code ?? "", s.Title ?? "",
                                       string.IsNullOrWhiteSpace(s.Status) ? "Not Yet Started" : s.Status,
                                       s.Folder ?? "") { Extras = s.Extras })
                .ToList()
        ) { Extras = e.Extras }).ToList();

        // A number naming an epic that is not there is the same as not having chosen: the epic it
        // pointed at was deleted, and inferring one is better than badging nothing.
        var current = dto.CurrentEpic is int n && epics.Exists(e => e.Number == n) ? n : (int?)null;

        return new Board(dto.Project ?? "", dto.Roadmap ?? new List<string>(), AssignSlugs(epics), current)
        {
            Migrated = migrated,
            Extras = dto.Extras,
        };
    }

    /// <summary>
    /// Reads the same text a second time as plain YAML nodes, and gives every mapping the keys its DTO
    /// had no property for.
    ///
    /// Nodes pair with DTOs by position, and that is exact rather than hopeful: a list item binds to a
    /// non-null DTO only when it is a mapping, and every other item — a bare "-", a "~" — binds to null.
    /// Keeping only the mappings on one side and only the non-null DTOs on the other leaves the same
    /// items in the same order. Anything that would break the pairing, such as a plain string where an
    /// epic belongs, has already failed the typed read.
    /// </summary>
    private static void AttachUnmodelledKeys(IndexDto dto, string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return;

        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) return;

        dto.Extras = Unmodelled(root, IndexKeys);

        var epics = (dto.Epics ?? new List<EpicDto>()).Where(e => e is not null);
        foreach (var (epicNode, epic) in MappingsUnder(root, "epics").Zip(epics))
        {
            epic.Extras = Unmodelled(epicNode, EpicKeys);

            var stories = (epic.Stories ?? new List<StoryDto>()).Where(s => s is not null);
            foreach (var (storyNode, story) in MappingsUnder(epicNode, "stories").Zip(stories))
                story.Extras = Unmodelled(storyNode, StoryKeys);
        }
    }

    private static IEnumerable<YamlMappingNode> MappingsUnder(YamlMappingNode node, string key) =>
        node.Children.FirstOrDefault(kv => kv.Key is YamlScalarNode { Value: var k } && k == key).Value
            is YamlSequenceNode list
            ? list.Children.OfType<YamlMappingNode>()
            : [];

    private static UnmodelledKeys Unmodelled(YamlMappingNode node, HashSet<string> modelled)
    {
        var pairs = node.Children
            .Where(kv => !(kv.Key is YamlScalarNode { Value: { } k } && modelled.Contains(k)))
            .ToList();
        return pairs.Count == 0 ? UnmodelledKeys.None : new UnmodelledKeys(pairs);
    }

    /// <summary>
    /// Moves a story-level release up to the epic that owns the story — the shape every backlog written
    /// before the release belonged to the epic is still in.
    ///
    /// An epic adopts the earliest release its stories name, in roadmap order, because that is where the
    /// work starts; a release the roadmap does not list sorts last. Release names are never rewritten:
    /// "V1" stays "V1", since only the file's author knows whether that meant 1.0.0 or 0.1.0.
    ///
    /// Never throws. An exception here would reach /api/board as a 422, and the devcontainer's start
    /// guard reads any HTTP failure as "not running" — so it would start a second instance, which dies
    /// unable to bind the port. A file this cannot make sense of yields epics with no release, which is
    /// a legitimate state that renders as Unscheduled.
    /// </summary>
    /// <returns>True when anything was lifted, so the caller knows the text on disk is a shape behind.</returns>
    private static bool LiftReleasesToEpics(IndexDto dto)
    {
        // A bare "-" list item deserializes to a null element — valid YAML, and one that carries no
        // code, title, status or folder, so skipping it loses nothing.
        var epics = (dto.Epics ?? new List<EpicDto>()).Where(e => e is not null).ToList();
        var roadmap = dto.Roadmap ?? new List<string>();

        var carriesLegacy = epics.Any(e =>
            (e.Stories ?? new List<StoryDto>()).Where(s => s is not null).Any(s => !string.IsNullOrWhiteSpace(s.Release)));

        if (!carriesLegacy) return false;

        foreach (var epic in epics)
        {
            var stories = (epic.Stories ?? new List<StoryDto>()).Where(s => s is not null).ToList();

            if (string.IsNullOrWhiteSpace(epic.Release))
            {
                var adopted = EarliestRelease(stories.Select(s => s.Release), roadmap);
                if (adopted.Length > 0) epic.Release = adopted;
            }

            // Cleared whether or not this epic adopted one: the field does not exist on a story any
            // more, and leaving it would write it straight back out on the next save.
            foreach (var story in stories) story.Release = null;
        }

        return true;
    }

    /// <summary>
    /// The release an epic belongs to, given the releases its stories name: the earliest in roadmap
    /// order, with any the roadmap does not list sorting last. Empty when none are named. The one place
    /// this rule lives — the YAML lift and the markdown migrator both call it, so they cannot disagree.
    /// </summary>
    internal static string EarliestRelease(IEnumerable<string?> releases, IReadOnlyList<string> roadmap) =>
        releases.Select(r => (r ?? "").Trim())
                .Where(r => r.Length > 0)
                .OrderBy(r => IndexIn(roadmap, r))
                .FirstOrDefault() ?? "";

    private static int IndexIn(IReadOnlyList<string> roadmap, string release)
    {
        for (var i = 0; i < roadmap.Count; i++)
            if (roadmap[i] == release) return i;
        return int.MaxValue;
    }

    /// <summary>
    /// The file for a board. Each mapping is its modelled fields in a fixed order, then its extras in
    /// the order they were read: on an epic after the title and before the stories, on a story after
    /// the folder, at the top after the epics. A null field is left out rather than written blank, so a
    /// backlog nobody has chosen a current epic for gains no "currentEpic:" line, and an epic with no
    /// version or release gains no empty ones.
    /// </summary>
    public static string Write(Board board) => Writer.Serialize(Mapping(
        [
            ("project", board.Project),
            ("roadmap", board.Roadmap.ToList()),
            ("currentEpic", board.CurrentEpic),
            ("epics", board.Epics.Select(e => Mapping(
                [
                    ("number", e.Number),
                    ("version", string.IsNullOrWhiteSpace(e.Version) ? null : e.Version),
                    ("release", string.IsNullOrWhiteSpace(e.Release) ? null : e.Release),
                    ("title", e.Title),
                ],
                e.Extras,
                // After the extras, so an epic's own keys stay next to its title rather than below a
                // list that can run to dozens of stories.
                ("stories", e.Stories.Select(s => Mapping(
                    [("code", s.Code), ("title", s.Title), ("status", s.Status), ("folder", s.Folder)],
                    s.Extras)).ToList()))).ToList()),
        ],
        board.Extras));

    // An ordered dictionary writes in insertion order, which is what makes the placement of extras a
    // rule rather than an accident of hashing.
    private static OrderedDictionary<object, object?> Mapping(
        IEnumerable<(string Key, object? Value)> fields, UnmodelledKeys extras, (string Key, object? Value)? last = null)
    {
        var mapping = new OrderedDictionary<object, object?>();
        foreach (var (key, value) in fields)
            if (value is not null) mapping.Add(key, value);
        foreach (var (key, value) in extras.Pairs)
            mapping.Add(key, value);
        if (last is { Value: not null } tail) mapping.Add(tail.Key, tail.Value);
        return mapping;
    }

    /// <summary>
    /// Epic slugs are unique board-wide and steer clear of the app's own paths; story slugs only
    /// need to be unique inside their epic, because a story URL is always reached through one —
    /// /core-application/checkout-and-payment.
    /// </summary>
    private static List<Epic> AssignSlugs(List<Epic> epics)
    {
        var epicSlugs = Slugs.Unique(
            epics.Select(e => e.Title).ToList(),
            epics.Select(e => $"epic-{e.Number}").ToList(),
            topLevel: true);

        return epics.Select((epic, i) =>
        {
            var storySlugs = Slugs.Unique(
                epic.Stories.Select(s => s.Title).ToList(),
                epic.Stories.Select(s => s.Code).ToList(),
                topLevel: false);

            var stories = epic.Stories.Select((s, j) => s with { Slug = storySlugs[j] }).ToList();
            return epic with { Slug = epicSlugs[i], Stories = stories };
        }).ToList();
    }
}
