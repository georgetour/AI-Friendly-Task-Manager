using Microsoft.Playwright;

namespace AIFTM.Tests;

/// <summary>
/// Search, run against the words that are actually in a backlog rather than against three chosen
/// examples.
///
/// Terms are harvested from the shipped demo — every epic title, every story title and code, and
/// words and phrases lifted out of the SKILL.md files, tasks and test cases — and each one is typed
/// into the real panel in a real browser. A term that came out of the files and finds nothing is a
/// bug in search, and three hand-written examples will never catch it.
/// </summary>
[Collection("ui")]
public class SearchCoverageTests(UiFixture fx)
{
    [Fact]
    public async Task Every_word_and_phrase_taken_from_the_demo_finds_something()
    {
        var terms = TermsFromDemo(fx.DemoBacklogPath);
        Assert.True(terms.Count >= 100, $"Only {terms.Count} terms harvested — the demo should yield far more.");

        try
        {
            await fx.UseProjectAsync(fx.DemoBacklogPath);

            var (page, errors) = await fx.NewPageAsync();
            await page.GotoAsync(fx.BaseUrl);
            await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();

            await page.Locator("#btnSearch").ClickAsync();
            await Assertions.Expect(page.Locator("#searchBox")).ToBeFocusedAsync();
            // The index has to have arrived, or every term below would "find nothing" for a reason
            // that has nothing to do with the term.
            await Assertions.Expect(page.Locator(".searchnote:visible")).ToHaveTextAsync(
                "Search story titles and codes, epics, tasks and test cases.");

            // Driven through the component rather than by typing each term: this is the same
            // runSearch() the keyboard calls, with none of the per-keystroke latency of 100 terms.
            var counts = await page.EvaluateAsync<int[]>(
                """
                terms => {
                  const c = Alpine.$data(document.querySelector('[x-data]'));
                  return terms.map(t => { c.search.q = t; c.runSearch(); return c.search.count; });
                }
                """, terms);

            var missed = terms.Where((_, i) => counts[i] == 0).ToList();
            Assert.True(missed.Count == 0,
                $"{missed.Count} of {terms.Count} terms from the demo found nothing: "
              + string.Join(" | ", missed.Take(20)));

            UiFixture.AssertNoConsoleErrors(errors);
        }
        finally
        {
            await fx.UsePrimaryProjectAsync();
            await fx.ForgetProjectAsync(fx.DemoBacklogPath);
        }
    }

    /// <summary>Words and phrases that are genuinely in the demo files — so every one of them has a
    /// right to be found.</summary>
    private static List<string> TermsFromDemo(string backlogPath)
    {
        var root = Path.GetDirectoryName(backlogPath)!;
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadAllLines(backlogPath))
        {
            // Titles and codes as they appear on screen, whole: "Checkout and Payment", "US-07".
            var value = ValueAfter(line, "title:") ?? ValueAfter(line, "code:");
            if (value is not null && value.Length > 2) terms.Add(value);
        }

        // Files sitting directly in skills/ belong to no story — skills/README.md explains the
        // folder layout to an agent. Search indexes stories, so those words are not missing.
        var skillsRoot = Path.GetFullPath(Path.Combine(root, "skills"));

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name == "BACKLOG.yaml") continue;
            if (Path.GetFullPath(Path.GetDirectoryName(file)!) == skillsRoot) continue;

            foreach (var sentence in name == "SKILL.md" ? Prose(file) : TextValues(file))
                AddTerms(terms, sentence);
        }

        return [.. terms];
    }

    /// <summary>The `text:` of every task and test case — the part search actually indexes. The
    /// `done:` and `status:` lines beside them are not in the index, so a term taken from one would
    /// fail for the right reason and still look like a bug.</summary>
    private static IEnumerable<string> TextValues(string file) =>
        File.ReadAllLines(file)
            .Select(l => ValueAfter(l.TrimStart().TrimStart('-').Trim(), "text:"))
            .Where(v => v is { Length: > 0 })!;

    /// <summary>SKILL.md prose. Frontmatter and headings are skipped because SearchIndex drops
    /// them — the terms have to come from what is indexed, not from what is in the file.</summary>
    private static IEnumerable<string> Prose(string file)
    {
        var inFrontmatter = false;
        var first = true;

        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();

            if (line == "---" && (first || inFrontmatter)) { inFrontmatter = !inFrontmatter; first = false; continue; }
            first = false;
            if (inFrontmatter || line.StartsWith('#') || line.Length == 0) continue;

            // "- [ ] AC1: something" — the marker is in the index too, but stripping it only ever
            // makes the term a shorter substring of the same line.
            yield return line.TrimStart('-', ' ').Replace("[ ]", "").Replace("[x]", "").Trim();
        }
    }

    private static void AddTerms(HashSet<string> terms, string sentence)
    {
        var tokens = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < tokens.Length; i++)
        {
            var word = Clean(tokens[i]);
            if (!Usable(word)) continue;

            terms.Add(word);

            // The pair as it is written, not two words glued together: search matches a substring,
            // so "background job" has to work as literally as "background" does. Only when the two
            // are adjacent and neither was punctuated away, or the phrase would not be in the text.
            if (i + 1 < tokens.Length && tokens[i] == word)
            {
                var next = Clean(tokens[i + 1]);
                if (Usable(next) && tokens[i + 1] == next) terms.Add(word + " " + next);
            }
        }
    }

    private static string Clean(string word) => word.Trim('.', ',', ':', ';', '(', ')', '"', '\'', '`', '[', ']');

    private static bool Usable(string word) => word.Length >= 5 && word.All(char.IsLetter);

    private static string? ValueAfter(string line, string key)
    {
        var at = line.IndexOf(key, StringComparison.Ordinal);
        return at < 0 ? null : line[(at + key.Length)..].Trim().Trim('"', '\'');
    }
}
