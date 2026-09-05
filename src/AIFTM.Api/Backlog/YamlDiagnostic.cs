using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;

namespace AIFTM.Api.Backlog;

/// <summary>
/// Turns a YAML parser error into an edit someone can make.
///
/// YamlDotNet reports its own internal state — "While parsing a block mapping, did not find
/// expected key" — which is accurate and useless. Explaining the rule instead ("YAML doubles the
/// quote") is better but still leaves the reader transforming a 200-character line in their head.
/// So where the mistake is recognisable, this writes out the corrected line in full: the fix is to
/// copy it over the original.
/// </summary>
public static class YamlDiagnostic
{
    /// <param name="Detail">A monospace block showing the line as it is and, where we can work it
    /// out, the line as it should be.</param>
    public sealed record Explanation(string Message, string? Detail);

    public static Explanation Explain(string fileText, YamlException e)
    {
        var lines = fileText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        // YamlDotNet reports positions as long; files here are never near int range.
        var lineNo = (int)Math.Clamp(e.Start.Line, 0, int.MaxValue);
        var column = (int)Math.Clamp(e.Start.Column, 0, int.MaxValue);

        // Taken from the exception itself rather than guessed from the text, so it outranks the
        // pattern matches below.
        if (FindDuplicateKeyName(ParserMessage(e)) is { } key)
            return ExplainDuplicateKey(lines, lineNo, column, key);

        // A tab throws the parser off at a position that can be nowhere near the tab itself, so
        // this one is looked for across the whole file rather than on the reported line. Only
        // indentation counts: a tab inside a quoted value is perfectly legal YAML.
        var tabAt = Array.FindIndex(lines, IsIndentedWithTab);
        if (tabAt >= 0)
            return BuildExplanation(tabAt + 1, lines[tabAt],
                "is indented with a tab. YAML does not allow tabs for indentation — use spaces.",
                ReplaceTabsInIndent(lines[tabAt]), column: 0);

        var line = lineNo >= 1 && lineNo <= lines.Length ? lines[lineNo - 1] : "";
        if (line.Length == 0)
            return new Explanation($"Line {lineNo}, column {column}: {ParserMessage(e)}", null);

        // A shell's own quote escaping written into the file. By far the most common way these get
        // broken: an agent writing YAML through a bash heredoc.
        if (line.Contains(@"'\''"))
            return BuildExplanation(lineNo, line,
                @"uses shell quote escaping (\'') where YAML wants a doubled quote ('').",
                line.Replace(@"'\''", "''"), column);

        if (HasUnquotedColon(line))
            return BuildExplanation(lineNo, line,
                "has a value containing a colon and a space, which YAML reads as the start of "
              + "another key. Quoting the whole value fixes it.",
                QuoteValue(line), column);

        if (GetSingleQuotedBody(line) is { } body && HasUnpairedQuote(body))
            return BuildExplanation(lineNo, line,
                "has an apostrophe inside a single-quoted value that is not doubled. "
              + "Write '' for a literal apostrophe.",
                null, column);

        return BuildExplanation(lineNo, line, null, null, column, raw: ParserMessage(e));
    }

    private static Explanation BuildExplanation(int lineNo, string line, string? what, string? repaired,
                                     int column, string? raw = null)
    {
        var message = what is null
            ? $"Line {lineNo} could not be parsed: {raw}"
            : $"Line {lineNo} of this file {what}";

        var sb = new StringBuilder()
            .Append("Line ").Append(lineNo).Append(" is:\n").Append(line).Append('\n');

        if (repaired is not null && repaired != line)
            sb.Append("\nChange it to:\n").Append(repaired).Append('\n');
        else
        {
            // No repair we would stand behind, so at least point at the character the parser
            // stopped on rather than leaving the reader to count to it.
            var caretAt = Math.Clamp(column - 1, 0, line.Length);
            sb.Append(new string(' ', caretAt)).Append("^ the parser stopped here\n");
        }

        return new Explanation(message, sb.ToString().TrimEnd());
    }

    private static string ParserMessage(YamlException e) => (e.InnerException?.Message ?? e.Message).Trim();

    private static readonly Regex DuplicateKeyPattern =
        new(@"duplicate key\s+'?""?([^'""\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? FindDuplicateKeyName(string raw) =>
        DuplicateKeyPattern.Match(raw) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>
    /// Knowing a key is repeated is only half of it — the fix needs the line the first one is on,
    /// and in a long file that is a hunt. So find both and say so.
    ///
    /// The exception's own position is not usable here: it marks where the containing mapping
    /// starts, not the offending key, and where that lands changes with how the file was
    /// deserialized. So the mapping is walked in the text and both occurrences located directly.
    ///
    /// No repair is offered: which of the two to drop is a judgement about content, and guessing
    /// wrong would delete someone's work.
    /// </summary>
    private static Explanation ExplainDuplicateKey(string[] lines, int reported, int column, string key)
    {
        var hits = FindKeyOccurrences(lines, key, reported);

        if (hits.Count < 2)
        {
            // Quoted or flow-style keys ({a: 1, a: 2}) are not found by the walk above. Say the
            // true thing rather than an invented line number.
            var fallback = reported >= 1 && reported <= lines.Length ? lines[reported - 1] : "";
            return BuildExplanation(reported, fallback,
                $"repeats the key \"{key}\", which is already set earlier in this file. YAML keeps "
              + "only the last one. Delete or rename one of them.", null, column);
        }

        var at = hits[^1];
        var first = hits[0];

        return BuildExplanation(at, lines[at - 1],
            $"repeats the key \"{key}\", which is already set on line {first}. YAML keeps only the "
          + "last one, so everything under the earlier one would be dropped without an error. "
          + "Delete or rename one of them.",
            null, GetKeyColumn(lines[at - 1]) + 1);
    }

    /// <summary>Where a key starts, counting past the "- " of a list item. The first key of an item
    /// sits on the dash line and the rest sit below it, so "- text:" and "  text:" are the same
    /// depth despite different indentation.</summary>
    private static int GetKeyColumn(string line)
    {
        var i = GetIndentWidth(line);
        while (i + 1 < line.Length && line[i] == '-' && line[i + 1] == ' ') i += 2;
        return i;
    }

    /// <summary>
    /// Every line in one mapping that sets the given key, as 1-based line numbers.
    ///
    /// <paramref name="reported"/> is only used as "a line somewhere in the offending mapping".
    /// That is deliberate: deserializing into a type puts the position on the duplicate key, while
    /// deserializing into object puts it on the line the mapping starts. Both land inside the same
    /// mapping, so the mapping is found from the line rather than assumed about it.
    ///
    /// Scoping to that one mapping is what keeps a sibling from being mistaken for a duplicate —
    /// every story has its own "title" lined up in the same column as the last one's.
    /// </summary>
    private static List<int> FindKeyOccurrences(string[] lines, string key, int reported)
    {
        var hits = new List<int>();
        if (reported < 1 || reported > lines.Length) return hits;

        var column = GetKeyColumn(lines[reported - 1]);
        var start = FindMappingStart(lines, reported, column);

        for (var i = start - 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;

            if (i > start - 1)
            {
                if (GetKeyColumn(line) < column) break;                     // out of the mapping
                if (GetKeyColumn(line) == column && GetIndentWidth(line) < column) break;  // next "- " item
            }

            if (IsAssignmentOf(line, key, column)) hits.Add(i + 1);
        }
        return hits;
    }

    /// <summary>Walks up to the line the mapping begins on: either its own "- " item marker, or the
    /// line after the parent key that introduced it.</summary>
    private static int FindMappingStart(string[] lines, int reported, int column)
    {
        for (var i = reported - 2; i >= 0; i--)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;

            var kc = GetKeyColumn(line);
            if (kc == column && GetIndentWidth(line) < column) return i + 1;  // the "- " starting this item
            if (kc < column) return i + 2;                                 // the parent key, so we start below it
        }
        return 1;
    }

    private static bool IsAssignmentOf(string line, string key, int column)
    {
        if (GetKeyColumn(line) != column || line.Length <= column + key.Length) return false;
        var rest = line.AsSpan(column);
        return rest.StartsWith(key) && rest[key.Length] == ':';
    }

    private static int GetIndentWidth(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }

    private static bool IsIndentedWithTab(string line) =>
        line.AsSpan(0, GetIndentWidth(line)).IndexOf('\t') >= 0;

    /// <summary>Two spaces per tab, which is what this app's own writer emits.</summary>
    private static string ReplaceTabsInIndent(string line)
    {
        var width = GetIndentWidth(line);
        return line[..width].Replace("\t", "  ") + line[width..];
    }

    /// <summary>The body of a single-quoted scalar, i.e. what follows <c>: '</c>.</summary>
    private static string? GetSingleQuotedBody(string line)
    {
        var at = line.IndexOf(": '", StringComparison.Ordinal);
        return at < 0 ? null : line[(at + 3)..];
    }

    /// <summary>True when the scalar holds a lone quote. Doubled quotes ('') are the escape and do
    /// not count; a single trailing quote is the closing delimiter.</summary>
    private static bool HasUnpairedQuote(string value)
    {
        var body = value.TrimEnd();
        if (body.EndsWith('\'')) body = body[..^1];

        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '\'') continue;
            if (i + 1 < body.Length && body[i + 1] == '\'') { i++; continue; }
            return true;
        }
        return false;
    }

    private static bool HasUnquotedColon(string line)
    {
        var at = line.IndexOf(": ", StringComparison.Ordinal);
        if (at < 0) return false;

        var value = line[(at + 2)..].Trim();
        if (value.StartsWith('\'') || value.StartsWith('"')) return false;   // already quoted
        return value.Contains(": ", StringComparison.Ordinal);
    }

    private static string QuoteValue(string line)
    {
        var at = line.IndexOf(": ", StringComparison.Ordinal);
        var value = line[(at + 2)..].Trim();
        return line[..(at + 2)] + "'" + value.Replace("'", "''") + "'";
    }
}
