using AIFTM.Api.Backlog;

namespace AIFTM.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Checkout and Payment", "checkout-and-payment")]
    [InlineData("Core Application", "core-application")]
    [InlineData("Day 0 — Local Setup", "day-0-local-setup")]
    [InlineData("CI/CD and Deployment", "ci-cd-and-deployment")]
    [InlineData("  Leading and trailing  ", "leading-and-trailing")]
    [InlineData("Multiple   spaces", "multiple-spaces")]
    [InlineData("Already-hyphenated", "already-hyphenated")]
    [InlineData("Symbols !@#$ stripped", "symbols-stripped")]
    [InlineData("ÉLAN with áccents", "elan-with-accents")]
    [InlineData("V0.1", "v0-1")]
    public void From_makes_a_readable_url_segment(string title, string expected)
    {
        Assert.Equal(expected, Slugs.From(title));
    }

    [Fact]
    public void From_returns_empty_when_there_is_nothing_sluggable()
    {
        Assert.Equal("", Slugs.From("!!! ???"));
        Assert.Equal("", Slugs.From(""));
    }

    [Fact]
    public void Unique_leaves_distinct_titles_alone()
    {
        var slugs = Slugs.Unique(
            new[] { "Core Application", "Developer Tooling" },
            new[] { "epic-0", "epic-1" }, topLevel: true);

        Assert.Equal(new[] { "core-application", "developer-tooling" }, slugs);
    }

    [Fact]
    public void Unique_suffixes_duplicate_titles_rather_than_sharing_a_url()
    {
        var slugs = Slugs.Unique(
            new[] { "Reporting", "Reporting", "Reporting" },
            new[] { "epic-1", "epic-2", "epic-3" }, topLevel: true);

        Assert.Equal(new[] { "reporting", "reporting-2", "reporting-3" }, slugs);
        Assert.Equal(3, slugs.Distinct().Count());
    }

    [Fact]
    public void Unique_falls_back_when_a_title_slugs_to_nothing()
    {
        var slugs = Slugs.Unique(new[] { "!!!" }, new[] { "US-07" }, topLevel: false);

        Assert.Equal(new[] { "us-07" }, slugs);
    }

    [Theory]
    [InlineData("Configure")]
    [InlineData("Releases")]
    [InlineData("Add Epic")]
    [InlineData("API")]
    public void Unique_keeps_an_epic_off_the_apps_own_paths(string title)
    {
        var slugs = Slugs.Unique(new[] { title }, new[] { "epic-4" }, topLevel: true);

        Assert.NotEqual(Slugs.From(title), slugs[0]);
        Assert.StartsWith(Slugs.From(title), slugs[0]);
        Assert.Contains("epic-4", slugs[0]);
    }

    [Fact]
    public void Unique_lets_a_story_use_a_reserved_word_since_it_is_never_at_the_root()
    {
        // /core-application/configure is unambiguous — only epics sit at the top level.
        var slugs = Slugs.Unique(new[] { "Configure" }, new[] { "US-01" }, topLevel: false);

        Assert.Equal(new[] { "configure" }, slugs);
    }

    // ---------- as wired into the index ----------

    private const string Yaml = """
        project: Test
        epics:
          - number: 0
            title: Core Application
            stories:
              - code: US-01
                title: Checkout and Payment
                folder: checkout-and-payment
          - number: 1
            title: Core Application
            stories:
              - code: US-02
                title: Checkout and Payment
                folder: checkout-and-payment-2
        """;

    [Fact]
    public void The_index_gives_every_epic_and_story_a_slug()
    {
        var board = YamlIndex.Parse(Yaml);

        Assert.Equal("core-application", board.Epics[0].Slug);
        Assert.Equal("checkout-and-payment", board.Epics[0].Stories[0].Slug);
    }

    [Fact]
    public void Epic_slugs_are_unique_across_the_board()
    {
        var board = YamlIndex.Parse(Yaml);

        Assert.Equal("core-application", board.Epics[0].Slug);
        Assert.Equal("core-application-2", board.Epics[1].Slug);
    }

    [Fact]
    public void Story_slugs_are_scoped_to_their_epic()
    {
        // The same story title in two epics is not a clash: the paths differ by their first segment.
        var board = YamlIndex.Parse(Yaml);

        Assert.Equal("checkout-and-payment", board.Epics[0].Stories[0].Slug);
        Assert.Equal("checkout-and-payment", board.Epics[1].Stories[0].Slug);
    }

    [Fact]
    public void Every_story_is_reachable_by_exactly_one_path()
    {
        var board = YamlIndex.Parse(Yaml);

        Assert.All(board.Epics, e => Assert.NotEqual("", e.Slug));
        Assert.All(board.Epics.SelectMany(e => e.Stories), s => Assert.NotEqual("", s.Slug));

        var paths = board.Epics.SelectMany(e => e.Stories.Select(s => $"/{e.Slug}/{s.Slug}")).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }
}
