using System.Text.Json;
using AIFTM.Api.Backlog;

namespace AIFTM.Api.Services;

/// <summary>One remembered project: where its backlog is, the folder its story folders sit in, and
/// its own logo. Nothing is copied or moved — a project is a set of paths, so each backlog stays
/// beside the code it describes.</summary>
public sealed record AppProject(string BacklogPath, string? SkillsPath, string? LogoPath = null);

/// <summary>
/// Logo/backlog/skills paths the UI edits at runtime. Nullable — an unset field just means
/// "not configured yet."
///
/// BacklogPath and SkillsPath remain *the current project*, unchanged, so everything that resolves
/// a path still reads exactly these two fields. Projects is a remembered list beside them, and a
/// config written before it existed simply has none — see <see cref="AppConfigService.Projects"/>.
/// </summary>
public sealed record AppConfig(string? BacklogPath, string? SkillsPath, string? LogoPath, bool IsDemo,
                                   IReadOnlyList<AppProject>? Projects = null);

/// <param name="Name">From the backlog's own `project:` field, so it can never drift from the file.</param>
/// <param name="Missing">The backlog file is not there — shown rather than hidden, because a moved
/// folder should be visible instead of silently producing an empty board.</param>
public sealed record ProjectView(string BacklogPath, string? SkillsPath, string Name, bool IsCurrent, bool Missing);

/// <param name="Pinned">A deploy-time BacklogPath override is set. It always wins over configuration,
/// so switching would change the config and change nothing you can see — the UI says so instead of
/// offering an action that does nothing.</param>
public sealed record ProjectList(IReadOnlyList<ProjectView> Projects, bool Pinned);

/// <summary>
/// Read/write gateway over the local, gitignored aiftm.config.json. Re-reads from disk on every
/// call (no in-memory cache) so external edits are always reflected, matching BacklogService's
/// "re-read fresh every call" philosophy. Also owns backlog-path resolution — the one place that
/// decides which file AI Friendly Task Manager is actually pointed at right now.
/// </summary>
public sealed class AppConfigService(string configPath)
{
    private static readonly JsonSerializerOptions SaveOpts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions LoadOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly object _lock = new();

    /// <summary>Reads the config, tolerating a missing or corrupt file by degrading to "nothing
    /// configured" — which self-heals via the demo path — rather than throwing. Accepts either
    /// PascalCase (what <see cref="Save"/> writes) or camelCase (what a hand-edit is likely to use).</summary>
    public AppConfig Load()
    {
        lock (_lock)
        {
            if (!File.Exists(configPath)) return new AppConfig(null, null, null, false);
            try
            {
                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<AppConfig>(json, LoadOpts) ?? new AppConfig(null, null, null, false);
            }
            catch (JsonException)
            {
                return new AppConfig(null, null, null, false);
            }
        }
    }

    private void Save(AppConfig config)
    {
        lock (_lock) File.WriteAllText(configPath, JsonSerializer.Serialize(config, SaveOpts));
    }

    /// <summary>Points the tracker at <paramref name="path"/>. If nothing exists there yet, creates
    /// it from the bundled template first — this is what lets Configure start tracking a brand-new
    /// project. Always clears IsDemo, since this is a deliberate user choice.</summary>
    public AppConfig SetBacklogPath(string path)
    {
        var full = ValidateBacklogPath(path);
        if (!File.Exists(full))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.Copy(TemplateLocator.Find("BACKLOG.template.yaml"), full);
        }
        var next = Load() with { BacklogPath = full, IsDemo = false };
        Save(next);
        return next;
    }

    /// <summary>
    /// Checks a backlog path and returns its absolute form. The browser checks the same things,
    /// but this is the API's own guarantee — the endpoint is reachable without the UI.
    /// </summary>
    public static string ValidateBacklogPath(string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0)
            throw new BacklogValidationException("Enter the path to your BACKLOG.yaml file.");
        if (!path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            throw new BacklogValidationException(
                "Point this at a .yaml file, for example C:/projects/my-app/BACKLOG.yaml.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new BacklogValidationException("That doesn't look like a valid file path."); }

        if (Directory.Exists(full))
            throw new BacklogValidationException("That path is a folder. Include the file name, for example BACKLOG.md.");

        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent))
            throw new BacklogValidationException("Include the folder the file lives in, not just a file name.");

        return full;
    }

    /// <summary>Persists the folder story Skill paths (e.g. "skills/foo/") are relative to. No file
    /// creation — skills are authored per-story, not scaffolded up front.</summary>
    public AppConfig SetSkillsPath(string path)
    {
        var next = Load() with { SkillsPath = ValidateSkillsPath(path) };
        Save(next);
        return next;
    }

    /// <summary>Checks a skills folder and returns its absolute form. The folder must already
    /// exist — silently accepting a typo would make every skill link fail with no explanation.</summary>
    public static string ValidateSkillsPath(string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0)
            throw new BacklogValidationException("Enter the folder your skills/… paths start from.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new BacklogValidationException("That doesn't look like a valid folder path."); }

        if (File.Exists(full))
            throw new BacklogValidationException("That's a file. Give the folder your skills/… paths start from.");
        if (!Directory.Exists(full))
            throw new BacklogValidationException("That folder doesn't exist. Create it first, or leave this empty.");

        return full;
    }

    // ---------- projects ----------

    /// <summary>
    /// Every remembered project, with the one actually in use marked.
    ///
    /// Two things are folded in so the page always shows what you are looking at. A config written
    /// before projects existed has no list, so its configured backlog appears as a single entry —
    /// an upgrade shows the project you already had rather than nothing. And a deploy-time override
    /// is never persisted, so without including it the board would show a backlog the list did not
    /// contain.
    /// </summary>
    /// <param name="inUse">The path actually resolved for this request, override included.</param>
    public IReadOnlyList<ProjectView> Projects(string? inUse = null)
    {
        var cfg = Load();
        var current = string.IsNullOrWhiteSpace(inUse) ? cfg.BacklogPath : inUse;
        var known = (cfg.Projects ?? []).ToList();

        if (!string.IsNullOrWhiteSpace(cfg.BacklogPath) && !known.Any(p => SamePath(p.BacklogPath, cfg.BacklogPath)))
            known.Insert(0, new AppProject(cfg.BacklogPath, cfg.SkillsPath));

        if (!string.IsNullOrWhiteSpace(current) && !known.Any(p => SamePath(p.BacklogPath, current)))
            known.Insert(0, new AppProject(current, cfg.SkillsPath));

        return known.Select(p => new ProjectView(
            p.BacklogPath,
            p.SkillsPath,
            NameOf(p.BacklogPath),
            SamePath(p.BacklogPath, current),
            !File.Exists(p.BacklogPath))).ToList();
    }

    /// <summary>Remembers a project and switches to it, creating the backlog from the template if it
    /// is not there yet — the same rule Configure already follows for a single project.</summary>
    public AppConfig AddProject(string backlogPath, string? skillsPath)
    {
        lock (_lock)
        {
            var backlog = ValidateBacklogPath(backlogPath);
            var skills = string.IsNullOrWhiteSpace(skillsPath) ? null : ValidateSkillsPath(skillsPath);

            if (!File.Exists(backlog))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backlog)!);
                File.Copy(TemplateLocator.Find("BACKLOG.template.yaml"), backlog);
                NameAfterItsFolder(backlog);
            }

            var cfg = Load();
            var list = Existing(cfg).Where(p => !SamePath(p.BacklogPath, backlog)).ToList();
            list.Add(new AppProject(backlog, skills));

            // A new project starts with no logo — the previous project's would otherwise appear to
            // belong to it.
            var next = cfg with { Projects = list, BacklogPath = backlog, SkillsPath = skills,
                                  LogoPath = null, IsDemo = false };
            Save(next);
            return next;
        }
    }

    /// <summary>Switches to an already-remembered project. Refuses an unknown path rather than
    /// quietly adding it, so this endpoint cannot be used to point the app anywhere at all.</summary>
    public AppConfig SelectProject(string backlogPath)
    {
        lock (_lock)
        {
            var cfg = Load();
            var match = Existing(cfg).FirstOrDefault(p => SamePath(p.BacklogPath, backlogPath))
                ?? throw new BacklogValidationException("That project isn't in the list. Add it first.");

            // LogoPath follows the project, so the header shows the branding of the board you are
            // actually looking at rather than whichever logo was uploaded last.
            var next = cfg with { Projects = Existing(cfg), BacklogPath = match.BacklogPath,
                                  SkillsPath = match.SkillsPath, LogoPath = match.LogoPath, IsDemo = false };
            Save(next);
            return next;
        }
    }

    /// <summary>
    /// Removes a project from the list. Files are never touched — the backlog and its story folders
    /// stay exactly where they are, and adding the same path again brings it straight back.
    ///
    /// <paramref name="confirmName"/> must match the project's name. The browser asks for it too,
    /// but this is the guarantee: the endpoint is reachable without the UI, and this is the one
    /// operation here a person could regret.
    /// </summary>
    public AppConfig RemoveProject(string backlogPath, string confirmName)
    {
        lock (_lock)
        {
            var cfg = Load();
            var match = Existing(cfg).FirstOrDefault(p => SamePath(p.BacklogPath, backlogPath))
                ?? throw new BacklogValidationException("That project isn't in the list.");

            if (!string.Equals((confirmName ?? "").Trim(), NameOf(match.BacklogPath), StringComparison.Ordinal))
                throw new BacklogValidationException("The name you typed doesn't match this project.");

            var list = Existing(cfg).Where(p => !SamePath(p.BacklogPath, backlogPath)).ToList();

            // Removing the one you are looking at has to leave you somewhere: the next remaining
            // project, or nothing configured — which falls through to the demo on the next read.
            var wasCurrent = SamePath(cfg.BacklogPath, backlogPath);
            var fallback = list.FirstOrDefault();
            var next = wasCurrent
                ? cfg with { Projects = list, BacklogPath = fallback?.BacklogPath,
                             SkillsPath = fallback?.SkillsPath, LogoPath = fallback?.LogoPath }
                : cfg with { Projects = list };

            Save(next);
            return next;
        }
    }

    /// <summary>The stored list, with the current project folded in if it predates the list.</summary>
    private static List<AppProject> Existing(AppConfig cfg)
    {
        var list = (cfg.Projects ?? []).ToList();
        // Carries the top-level logo onto the synthesised entry, so a config written before projects
        // existed keeps the logo it already had rather than losing it on first upgrade.
        if (!string.IsNullOrWhiteSpace(cfg.BacklogPath) && !list.Any(p => SamePath(p.BacklogPath, cfg.BacklogPath)))
            list.Insert(0, new AppProject(cfg.BacklogPath, cfg.SkillsPath, cfg.LogoPath));
        return list;
    }

    /// <summary>
    /// Renames a freshly-created backlog after the folder it was created in.
    ///
    /// Without this, every project made from the template is called "Acme App" — so a list of them
    /// is a column of identical names distinguished only by path, which is exactly what the name is
    /// there to avoid. Only ever applied to a file this method just created.
    /// </summary>
    private static void NameAfterItsFolder(string backlogPath)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(backlogPath));
        if (string.IsNullOrWhiteSpace(folder)) return;

        try
        {
            var board = YamlIndex.Parse(File.ReadAllText(backlogPath));
            File.WriteAllText(backlogPath, YamlIndex.Write(board with { Project = folder }));
        }
        catch (Exception) { /* the template is still a valid backlog under its own name */ }
    }

    /// <summary>The backlog's own `project:` value, falling back to the folder name. Never throws —
    /// a project whose file is broken still has to appear in the list so it can be fixed.</summary>
    private static string NameOf(string backlogPath)
    {
        try
        {
            if (File.Exists(backlogPath))
            {
                var name = YamlIndex.Parse(File.ReadAllText(backlogPath)).Project;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch (Exception) { /* unparseable is not a reason to hide it */ }

        return Path.GetFileName(Path.GetDirectoryName(backlogPath)) ?? backlogPath;
    }

    /// <summary>Compares two paths the way the platform does — Windows ignores case, Linux does not,
    /// which is the same reason PathSafety uses GetRelativePath rather than a string prefix.</summary>
    private static bool SamePath(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                         OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Sets the current project's logo, or clears it when <paramref name="webPath"/> is null.
    ///
    /// Stored twice on purpose: on the project entry, because a logo belongs to the project rather
    /// than to the app, and at the top level as "the logo in use right now" so the browser can keep
    /// reading one field. Switching project refreshes the top-level copy from the entry.
    /// </summary>
    public AppConfig SetLogoPath(string? webPath)
    {
        lock (_lock)
        {
            var cfg = Load();
            var list = Existing(cfg)
                .Select(p => SamePath(p.BacklogPath, cfg.BacklogPath) ? p with { LogoPath = webPath } : p)
                .ToList();

            var next = cfg with { LogoPath = webPath, Projects = list };
            Save(next);
            return next;
        }
    }

    /// <summary>
    /// Renames the current project by writing the backlog's own `project:` field.
    ///
    /// Written to the file, never stored in config: the name has one home, so it cannot drift from
    /// what the board shows or from what another tool reading the backlog would see.
    /// </summary>
    public void SetProjectName(string backlogPath, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new BacklogValidationException("Give the project a name.");
        if (name.Length > 60) throw new BacklogValidationException("That name is too long (60 characters max).");
        if (!File.Exists(backlogPath)) throw new BacklogValidationException("That backlog file is not there.");

        lock (_lock)
        {
            var board = YamlIndex.Parse(File.ReadAllText(backlogPath));
            File.WriteAllText(backlogPath, YamlIndex.Write(board with { Project = name }));
        }
    }

    /// <summary>First-run bootstrap: materializes a live, writable demo backlog from the bundled
    /// template together with its skill files, and persists config pointing at both. Idempotent —
    /// existing files are never overwritten, so edits made to the demo survive a restart. Runs under
    /// <see cref="_lock"/> (re-entrant, so the nested Load/Save calls compose fine) so two concurrent
    /// first-run requests can't race the check-then-act file copies below.</summary>
    public AppConfig MaterializeDemo(string demoPath)
    {
        lock (_lock)
        {
            var demoDir = Path.GetDirectoryName(demoPath)!;
            Directory.CreateDirectory(demoDir);

            if (!File.Exists(demoPath))
                File.Copy(TemplateLocator.Find("BACKLOG.template.yaml"), demoPath);

            // A story's "folder" is resolved under SkillsPath, so the demo's skills directory is
            // that root: <demoDir>/skills/<folder>/{SKILL.md,tasks.yaml,test-cases.yaml}.
            var templatesDir = Path.GetDirectoryName(TemplateLocator.Find("BACKLOG.template.yaml"))!;
            CopyDirectory(Path.Combine(templatesDir, "skills"), Path.Combine(demoDir, "skills"));

            var current = Load();
            // Only adopt the demo directory as SkillsPath when the user hasn't configured a real one
            // (or we're already in demo mode) — otherwise a backlog that's temporarily missing (moved
            // folder, unmounted drive) would silently clobber a real SkillsPath on fall-through.
            // A story's folder is now a bare name ("backlog-board"), so the root is skills/ itself.
            var skillsPath = (string.IsNullOrWhiteSpace(current.SkillsPath) || current.IsDemo)
                ? Path.GetFullPath(Path.Combine(demoDir, "skills"))
                : current.SkillsPath;
            var next = current with { BacklogPath = demoPath, SkillsPath = skillsPath, IsDemo = true };
            Save(next);
            return next;
        }
    }

    /// <summary>Recursive copy that never clobbers an existing file — keeps MaterializeDemo idempotent
    /// so a restart preserves whatever the user changed in the demo.</summary>
    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    /// <summary>Resolves the backlog path AI Friendly Task Manager should use right now, freshly, every call: an
    /// explicit deploy-time override always wins (and is never persisted); otherwise the configured
    /// path if it still exists; otherwise whatever BacklogLocator finds by walking up from the working
    /// directory; otherwise a live demo materialized from the bundled template.</summary>
    public string ResolveBacklogPath(string? overridePath, string demoPath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var cfg = Load();
        if (!string.IsNullOrWhiteSpace(cfg.BacklogPath) && File.Exists(cfg.BacklogPath))
            return cfg.BacklogPath;

        var walked = BacklogLocator.FindOrNull();
        if (walked is not null) return walked;

        return MaterializeDemo(demoPath).BacklogPath!;
    }
}
