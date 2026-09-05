using AIFTM.Api.Backlog;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace AIFTM.Tests;

/// <summary>
/// A parse error has to name the mistake and show the edit. YamlDotNet's own text — "While parsing
/// a block mapping, did not find expected key" — is accurate and useless. Explaining the rule is
/// better but still leaves the reader transforming a 200-character line in their head, so where the
/// mistake is recognisable the corrected line is written out to be copied over the original.
/// </summary>
public class YamlDiagnosticTests
{
    // Shell-escaped quotes, exactly as a bash heredoc writes them into a file.
    private const string ShellEscaped =
        "- text: 'Run date_trunc('\\''year'\\'', paid_at) and check'\n  status: Not Run\n";

    // Duplicate key checking matches how the app's own readers are built — without it a repeated
    // key parses quietly and the diagnostic never sees it.
    private static readonly IDeserializer Parser =
        new DeserializerBuilder().WithDuplicateKeyChecking().Build();

    private static YamlDiagnostic.Explanation Explain(string yaml)
    {
        try
        {
            Parser.Deserialize<object>(yaml);
            throw new InvalidOperationException("Expected this YAML to fail, but it parsed.");
        }
        catch (YamlException e)
        {
            return YamlDiagnostic.Explain(yaml, e);
        }
    }

    private static string Replacement(YamlDiagnostic.Explanation e) =>
        e.Detail!.Split("Change it to:")[1].Trim();

    [Fact]
    public void Shell_quote_escaping_is_named_in_the_message()
    {
        var result = Explain(ShellEscaped);

        Assert.Contains("shell", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("block mapping", result.Message);
    }

    [Fact]
    public void The_corrected_line_is_written_out_in_full()
    {
        var result = Explain(ShellEscaped);

        Assert.NotNull(result.Detail);
        Assert.Contains("Change it to:", result.Detail);
        Assert.Contains("date_trunc(''year'', paid_at)", Replacement(result));
        Assert.DoesNotContain(@"'\''", Replacement(result));
    }

    [Fact]
    public void The_corrected_line_parses_and_keeps_its_meaning()
    {
        // A suggested fix that does not itself parse would be worse than no suggestion at all.
        var replacement = Replacement(Explain(ShellEscaped));

        var parsed = new DeserializerBuilder().Build()
            .Deserialize<List<Dictionary<string, string>>>(replacement + "\n  status: Not Run\n");

        Assert.Equal("Run date_trunc('year', paid_at) and check", parsed[0]["text"]);
    }

    [Fact]
    public void A_long_line_is_shown_whole_so_it_can_be_copied()
    {
        var pad = new string('x', 300);
        var yaml = $"- text: 'Run {pad} date_trunc('\\''year'\\'', paid_at)'\n  status: Not Run\n";

        var result = Explain(yaml);

        // Excerpting would defeat the purpose: a truncated line cannot be pasted over the original.
        Assert.Contains(pad, result.Detail);
        Assert.Contains("Change it to:", result.Detail);
    }

    [Fact]
    public void A_tab_is_named_and_replaced_with_spaces()
    {
        // The parser reports this one at a position nowhere near the tab, so the tab is looked for
        // across the file rather than on the line it blamed.
        var result = Explain("tasks:\n\t- text: Indented with a tab\n");

        Assert.Contains("tab", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\t", Replacement(result));
    }

    [Fact]
    public void A_tab_inside_a_value_is_not_mistaken_for_bad_indentation()
    {
        // Searching the whole file for a tab is what makes the case above work, and it would blame
        // the wrong thing here: a tab within a scalar is legal, so this file's real fault is the
        // shell escaping.
        var result = Explain("- text: 'a\ttab inside date_trunc('\\''year'\\'')'\n  status: Not Run\n");

        Assert.Contains("shell", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("indented with a tab", result.Message);
        Assert.Contains("\t", Replacement(result));   // the legal tab survives the repair
    }

    [Fact]
    public void An_unquoted_colon_in_a_value_is_quoted_for_you()
    {
        var result = Explain("- text: Add index: on the orders table\n  done: false\n");

        Assert.Contains("colon", result.Message, StringComparison.OrdinalIgnoreCase);

        var parsed = new DeserializerBuilder().Build()
            .Deserialize<List<Dictionary<string, string>>>(Replacement(result) + "\n  done: false\n");
        Assert.Equal("Add index: on the orders table", parsed[0]["text"]);
    }

    [Fact]
    public void The_line_number_is_always_reported()
    {
        var result = Explain("- text: 'unclosed\n  status: Not Run\n");

        Assert.Matches(@"Line \d+", result.Message);
    }

    [Fact]
    public void An_unrecognised_problem_falls_back_to_the_parsers_own_words()
    {
        // Better the parser's wording than a confident guess at the wrong cause.
        var result = Explain("a: [1, 2\nb: 3\n");

        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Matches(@"Line \d+", result.Message);
    }

    [Fact]
    public void A_duplicate_key_names_the_line_the_first_one_is_on()
    {
        // "Encountered duplicate key epics" leaves you hunting for the other one in a long file,
        // which is the only thing you actually need to know.
        var result = Explain(
            "project: Demo\nroadmap: []\nepics:\n  - number: 1\n    title: One\n" +
            "epics:\n  - number: 2\n    title: Two\n");

        Assert.Contains("line 3", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("epics", result.Message);
        Assert.Contains("Line 6", result.Message);
    }

    [Fact]
    public void A_duplicate_in_a_list_item_matches_the_dash_line_key()
    {
        // "- text:" and "  text:" are the same depth despite different indentation.
        var result = Explain("- text: one\n  text: two\n  done: false\n");

        Assert.Contains("line 1", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_key_at_a_different_depth_is_not_the_first_use()
    {
        // A story's own "title" is not a duplicate of its epic's, so the epic's line must not be
        // pointed at as though it were.
        var result = Explain(
            "epics:\n  - number: 1\n    title: Epic title\n    stories:\n      - code: US-01\n" +
            "        title: A\n        title: B\n");

        Assert.Contains("Line 7", result.Message);                                        // the duplicate
        Assert.Contains("line 6", result.Message, StringComparison.OrdinalIgnoreCase);    // its own earlier title
        Assert.DoesNotContain("line 3", result.Message, StringComparison.OrdinalIgnoreCase); // the epic's title
    }

    [Fact]
    public void The_first_use_is_found_through_the_apps_own_readers()
    {
        // Deserializing into a type puts the error position on the duplicate key; deserializing
        // into object puts it on the line the mapping starts. The tests above take the second path
        // and the app takes the first, so this pins the one that ships.
        var yaml = "project: Demo\nroadmap: []\nepics:\n  - number: 1\n    title: One\n"
                 + "epics:\n  - number: 2\n    title: Two\n";

        var e = Assert.Throws<YamlException>(() => YamlIndex.Parse(yaml));
        var result = YamlDiagnostic.Explain(yaml, e);

        Assert.Contains("Line 6", result.Message);
        Assert.Contains("line 3", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_siblings_matching_key_is_not_the_first_use()
    {
        // Two stories each have a "title" in the same column. Only the one in the same story counts.
        var yaml = "project: Demo\nepics:\n  - number: 1\n    title: Epic\n    stories:\n"
                 + "      - code: US-01\n        title: First story\n"
                 + "      - code: US-02\n        title: A\n        title: B\n";

        var e = Assert.Throws<YamlException>(() => YamlIndex.Parse(yaml));
        var result = YamlDiagnostic.Explain(yaml, e);

        Assert.Contains("Line 10", result.Message);                                    // the duplicate
        Assert.Contains("line 9", result.Message, StringComparison.OrdinalIgnoreCase); // same story
        Assert.DoesNotContain("line 7", result.Message, StringComparison.OrdinalIgnoreCase); // other story
    }

    [Fact]
    public void A_duplicate_key_is_not_given_a_guessed_repair()
    {
        // Which of the two to drop is a judgement about content; guessing would delete real work.
        var result = Explain("a: 1\na: 2\n");

        Assert.DoesNotContain("Change it to:", result.Detail);
        Assert.Contains("^", result.Detail);
    }

    [Fact]
    public void With_no_repair_the_column_is_pointed_at()
    {
        var result = Explain("- text: 'has an ' unescaped quote inside'\n  status: Not Run\n");

        Assert.NotNull(result.Detail);
        Assert.Contains("^", result.Detail);
    }
}
