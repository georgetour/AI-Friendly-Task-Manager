using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AIFTM.Api.Backlog;

/// <summary>
/// Reads and writes BACKLOG.yaml.
///
/// Whole-file deserialize → mutate → serialize. The app is the only writer, so there is nothing to
/// preserve between writes and no surgical text editing to get wrong — which is what let
/// BacklogWriter, BacklogGenerator and SummaryWriter be deleted outright. Serialization is
/// deterministic, so changing one status still shows up as one line in a diff.
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
    }

    private sealed class EpicDto
    {
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public List<StoryDto> Stories { get; set; } = new();
    }

    private sealed class StoryDto
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "Not Yet Started";
        public string Release { get; set; } = "";
        public string Folder { get; set; } = "";
    }

    // Duplicate keys are rejected rather than silently letting the last one win. In a file that IS
    // the database, "epics:" appearing twice would drop a whole epic with no error anywhere — and
    // the integrity checks below could not catch it, because by then the data is already gone.
    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .WithDuplicateKeyChecking()
        .Build();

    // OmitNull, so a backlog nobody has chosen a current epic for gains no "currentEpic:" line at
    // all. Every other field is a non-null default, so nothing else in the output moves.
    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static Board Parse(string yaml)
    {
        var dto = (string.IsNullOrWhiteSpace(yaml) ? null : Reader.Deserialize<IndexDto>(yaml))
                  ?? new IndexDto();

        var epics = dto.Epics.Select(e => new Epic(
            e.Number,
            e.Title ?? "",
            (e.Stories ?? new List<StoryDto>())
                .Select(s => new Story(s.Code ?? "", s.Title ?? "",
                                       string.IsNullOrWhiteSpace(s.Status) ? "Not Yet Started" : s.Status,
                                       s.Release ?? "", s.Folder ?? ""))
                .ToList()
        )).ToList();

        // A number naming an epic that is not there is the same as not having chosen: the epic it
        // pointed at was deleted, and inferring one is better than badging nothing.
        var current = dto.CurrentEpic is int n && epics.Exists(e => e.Number == n) ? n : (int?)null;

        return new Board(dto.Project ?? "", dto.Roadmap ?? new List<string>(), AssignSlugs(epics), current);
    }

    public static string Write(Board board) => Writer.Serialize(new IndexDto
    {
        Project = board.Project,
        Roadmap = board.Roadmap.ToList(),
        CurrentEpic = board.CurrentEpic,
        Epics = board.Epics.Select(e => new EpicDto
        {
            Number = e.Number,
            Title = e.Title,
            Stories = e.Stories.Select(s => new StoryDto
            {
                Code = s.Code,
                Title = s.Title,
                Status = s.Status,
                Release = s.Release,
                Folder = s.Folder,
            }).ToList(),
        }).ToList(),
    });

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
