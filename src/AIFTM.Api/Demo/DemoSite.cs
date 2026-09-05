using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIFTM.Api.Backlog;
using AIFTM.Api.Services;

namespace AIFTM.Api.Demo;

/// <summary>
/// Writes the browsable demo: the real front end, plus every answer the API would have given,
/// baked into one file.
///
/// The point is that there is no second copy of the app. `wwwroot` is copied verbatim — same
/// index.html, same app.js, same stylesheet — and one extra script in front of it answers `fetch`
/// from the baked data instead of from a server. What a visitor clicks is the application, not a
/// mock-up of it, and it cannot drift from the real thing because it *is* the real thing.
///
/// The data comes from the same BacklogService and SearchIndex the running app uses, so the demo
/// cannot disagree with the app about what a board or a search index looks like either.
/// </summary>
public static class DemoSite
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="basePath">The folder the site will be served from — "/AI-Friendly-Task-Manager/" for a
    /// GitHub Pages project site, "/" when it owns the root.</param>
    public static DemoResult Build(BacklogService svc, string wwwroot, string outDir, string basePath)
    {
        var prefix = "/" + basePath.Trim('/');
        if (prefix == "/") prefix = "";

        // Everything read before anything is written. A backlog that will not parse used to leave
        // half a site on disk — index.html and the assets, no data — which a deploy would happily
        // publish as a blank page.
        var board = svc.GetBoard();
        var stories = new Dictionary<string, object>(StringComparer.Ordinal);
        var skills = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var story in board.Epics.SelectMany(e => e.Stories))
        {
            // A story with an unreadable folder is left out of the detail map rather than aborting
            // the export: the demo should still build, and the board still lists it.
            try { stories[story.Code] = svc.GetStory(story.Code); }
            catch (Exception) { continue; }

            var skillPath = story.Folder + "/SKILL.md";
            var onDisk = StoryFolder.SkillPath(svc.SkillsRoot, story.Folder);
            if (File.Exists(onDisk)) skills[skillPath] = File.ReadAllText(onDisk);
        }

        var data = new
        {
            basePath = prefix + "/",
            board,
            stories,
            skills,
            search = SearchIndex.Build(svc),
            validate = svc.Validate(),
        };

        Directory.CreateDirectory(outDir);
        CopyTree(wwwroot, outDir, prefix);

        File.WriteAllText(Path.Combine(outDir, "demo-data.js"),
            "window.AIFTM_DEMO = " + JsonSerializer.Serialize(data, Json) + ";\n", Utf8NoBom);

        var fingerprinted = Fingerprint(outDir, prefix);

        // GitHub Pages has no router: a deep link like /core-application is a 404 as far as it is
        // concerned. Serving the app as the 404 body is the documented way to hand the URL to a
        // client-side router — the address bar keeps the path, and readUrl() takes it from there.
        //
        // Copied after the fingerprinting, so the two pages ask for the same files.
        File.Copy(Path.Combine(outDir, "index.html"), Path.Combine(outDir, "404.html"), overwrite: true);

        return new DemoResult(board.Epics.Count, stories.Count, skills.Count, fingerprinted);
    }

    /// <summary>Every file index.html asks for, in the order it asks for them.</summary>
    private static readonly string[] Assets =
        ["app.css", "vendor/alpine-csp.min.js", "demo-data.js", "demo-api.js", "app.js"];

    /// <summary>
    /// Renames each asset after a hash of its own contents and points the page at the new names.
    ///
    /// GitHub Pages sends `Cache-Control: max-age=600` on everything it serves, and that header is
    /// its own — there is no server of ours in front of it to say otherwise. So for ten minutes
    /// after a deploy a returning visitor keeps whatever they already had, and worse, they can end
    /// up holding a *mixture*: a fresh index.html asking for an app.js their browser answers from
    /// cache. A hashed name cannot be answered from cache unless the bytes are identical, so the
    /// page and the code it runs are always the same vintage.
    ///
    /// The hash is of the content, not a number anyone bumps: a release that changes nothing keeps
    /// the same URLs and stays cached, and one that changes a byte cannot be missed.
    /// </summary>
    /// <returns>How many files were renamed — reported by the CLI so a silent no-op is visible.</returns>
    private static int Fingerprint(string outDir, string prefix)
    {
        var index = Path.Combine(outDir, "index.html");
        var html = File.ReadAllText(index);
        var renamed = 0;

        foreach (var asset in Assets)
        {
            var full = Path.Combine(outDir, asset.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;

            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full)))[..8].ToLowerInvariant();
            var folder = Path.GetDirectoryName(asset)?.Replace('\\', '/') ?? "";
            var stem = Path.GetFileNameWithoutExtension(asset);
            var versioned = (folder.Length > 0 ? folder + "/" : "") + stem + "." + hash + Path.GetExtension(asset);

            File.Move(full, Path.Combine(outDir, versioned.Replace('/', Path.DirectorySeparatorChar)), overwrite: true);
            html = html.Replace($"\"{prefix}/{asset}\"", $"\"{prefix}/{versioned}\"", StringComparison.Ordinal);
            renamed++;
        }

        File.WriteAllText(index, html, Utf8NoBom);
        return renamed;
    }

    /// <summary>The repo, found by the folder that ships the templates. Relative paths on the
    /// command line resolve against this rather than against the working directory, which
    /// `dotnet run --project` has already moved into the project folder.</summary>
    public static string RepoRoot(string from)
    {
        var dir = new DirectoryInfo(from);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "templates"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static void CopyTree(string source, string destination, string prefix)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);
            var target = Path.Combine(destination, name);

            if (name == "index.html") File.WriteAllText(target, Rewrite(File.ReadAllText(file), prefix), Utf8NoBom);
            else File.Copy(file, target, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            // Whatever the developer happened to upload as a logo is not part of the demo.
            if (Path.GetFileName(dir) == "uploads") continue;
            CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)), prefix);
        }
    }

    /// <summary>The two edits index.html needs to live in a folder: where the app thinks it is, and
    /// the absolute asset paths that would otherwise point above it. Everything else is untouched —
    /// this is a move, not a fork.</summary>
    internal static string Rewrite(string html, string prefix)
    {
        html = html.Replace(@"<meta name=""app-base"" content=""/"">",
                            $@"<meta name=""app-base"" content=""{prefix}/"">", StringComparison.Ordinal);

        foreach (var asset in new[] { "/app.css", "/app.js", "/vendor/alpine-csp.min.js" })
            html = html.Replace($@"""{asset}""", $@"""{prefix}{asset}""", StringComparison.Ordinal);

        // Loaded before app.js, because app.js asks for the board as soon as Alpine starts.
        return html.Replace($@"<script src=""{prefix}/app.js""></script>",
                            $@"<script src=""{prefix}/demo-data.js""></script>" + "\n"
                          + $@"<script src=""{prefix}/demo-api.js""></script>" + "\n"
                          + $@"<script src=""{prefix}/app.js""></script>", StringComparison.Ordinal);
    }
}

public sealed record DemoResult(int Epics, int Stories, int Skills, int Versioned);
