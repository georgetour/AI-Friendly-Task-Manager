using System.Diagnostics;
using System.Text;
using AIFTM.Api.Backlog;
using AIFTM.Api.Backlog.Legacy;
using AIFTM.Api.Demo;
using AIFTM.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var appConfigService = new AppConfigService(
    Path.Combine(builder.Environment.ContentRootPath, "aiftm.config.json"));
var demoBacklogPath = Path.Combine(builder.Environment.ContentRootPath, "data", "BACKLOG.demo.yaml");
var overridePath = builder.Configuration["BacklogPath"];

string ResolveBacklogPath() => appConfigService.ResolveBacklogPath(overridePath, demoBacklogPath);

// Story folders sit beside the backlog unless Configure says otherwise, so a project that has
// never been configured still finds its own skills/ directory rather than the app's.
string ResolveSkillsPath() => appConfigService.Load().SkillsPath
    ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ResolveBacklogPath()))!, "skills");

// CLI mode: `dotnet run -- migrate <BACKLOG.md> [output.yaml]` imports an old markdown backlog.
// Pure C#, cross-platform — no external tooling required.
if (args.Length > 0 && args[0] == "migrate")
{
    var source = args.Length > 1 ? args[1] : "BACKLOG.md";
    var target = args.Length > 2 ? args[2] : Path.ChangeExtension(Path.GetFullPath(source), ".yaml");
    var skillsOut = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, "skills");

    try
    {
        var result = MarkdownMigrator.Run(source, target, skillsOut);
        Console.WriteLine($"Migrated {result.Stories} stories across {result.Epics} epics.");
        Console.WriteLine($"Wrote {target}");
        Console.WriteLine($"Wrote {result.FoldersCreated} story folders under {skillsOut}");
        foreach (var note in result.Notes) Console.WriteLine($"  note: {note}");
    }
    catch (BacklogValidationException e)
    {
        Console.Error.WriteLine(e.Message);
        Environment.ExitCode = 1;
    }
    return;
}

// CLI mode: `dotnet run -- export-demo <outDir> [basePath] [BACKLOG.yaml]` writes the static demo
// site — the real wwwroot plus every answer the API would have given. Pure C# for the same reason
// migrate is: CI runs it on a Linux runner with no shell dependency.
if (args.Length > 0 && args[0] == "export-demo")
{
    // Relative paths are relative to the repo, not to the working directory: `dotnet run --project`
    // runs the app from the project folder, so "templates/BACKLOG.template.yaml" typed at the repo
    // root would go looking in src/AIFTM.Api/. Absolute paths are used as given.
    var repo = DemoSite.RepoRoot(builder.Environment.ContentRootPath);

    var outDir = Path.GetFullPath(Path.Combine(repo, args.Length > 1 ? args[1] : "_site"));
    var basePath = args.Length > 2 ? args[2] : "/";
    var backlog = Path.GetFullPath(Path.Combine(repo, args.Length > 3
        ? args[3]
        : Path.Combine("templates", "BACKLOG.template.yaml")));
    var skills = Path.Combine(Path.GetDirectoryName(backlog)!, "skills");

    try
    {
        // WebRootPath, the same place the running app serves the front end from — so the export
        // cannot be given a different copy of the UI than `dotnet run` uses.
        var wwwroot = builder.Environment.WebRootPath
                   ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");

        var built = DemoSite.Build(new BacklogService(() => backlog, () => skills),
                                   wwwroot, outDir, basePath);

        Console.WriteLine($"Wrote {outDir} from {backlog}");
        Console.WriteLine($"  {built.Epics} epics, {built.Stories} stories, {built.Skills} descriptions");
        Console.WriteLine($"  {built.Versioned} assets named after their contents, so a cache cannot serve a stale one");
    }
    catch (Exception e) when (e is BacklogValidationException or IOException or DirectoryNotFoundException)
    {
        Console.Error.WriteLine(e.Message);
        Environment.ExitCode = 1;
    }
    return;
}

builder.Services.AddSingleton(appConfigService);
builder.Services.AddSingleton(new BacklogService(ResolveBacklogPath, ResolveSkillsPath));

var app = builder.Build();
// Dynamic data must never be cached: a stale board would show statuses that are no longer in the
// file. Without an explicit header a browser may heuristically cache a GET, so we say it outright.
// A Content-Security-Policy with no 'unsafe-eval' and no 'unsafe-inline' for scripts. This is why
// the front end uses Alpine's CSP build: it evaluates its bindings without new Function(), so an
// injected string can never become executable code — the browser refuses to run it, rather than us
// relying on having escaped every value correctly.
// 'unsafe-inline' remains for styles only: x-show and x-bind:style write the style attribute, and
// there is no script-execution risk in a style. Fonts come from Google, so those two hosts are
// named explicitly rather than opening style-src and font-src to everything.
const string contentSecurityPolicy =
    "default-src 'self'; " +
    "script-src 'self'; " +
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
    "font-src https://fonts.gstatic.com; " +
    "img-src 'self' data:; " +
    "connect-src 'self'; " +
    "form-action 'self'; " +
    "base-uri 'none'; " +
    "object-src 'none'; " +
    "frame-ancestors 'none'";

app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers["Content-Security-Policy"] = contentSecurityPolicy;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";

    if (ctx.Request.Path.StartsWithSegments("/api"))
        headers.CacheControl = "no-store";
    await next();
});

app.UseDefaultFiles();
// "no-cache" means revalidate before use — NOT "don't cache". Combined with the ETag that
// StaticFiles already sends, an unchanged file costs a 304 instead of a full download, while an
// edited app.js is picked up immediately. ("no-store" would forbid caching entirely and make the
// ETag pointless, which is what a stale-looking UI after an update taught us the first time.)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

// UseRouting is placed here on purpose, AFTER the static files. Left implicit it runs at the very
// start of the pipeline, and the static-file middleware skips any request that has already matched
// an endpoint — so once "/{epicSlug}" existed, /app.css matched it and was served as HTML.
// Files first, then routes.
app.UseRouting();

// The index only — no tasks, no test cases. That is what keeps the board fast no matter how much
// detail the story folders hold. If the file will not parse, hand back the validation report so the
// UI can say which line is wrong instead of showing an empty board.
app.MapGet("/api/board", (BacklogService svc) =>
{
    try { return Results.Json(svc.GetBoard()); }
    catch (Exception) { return Results.Json(svc.Validate(), statusCode: 422); }
});

app.MapGet("/api/story/{code}", (string code, BacklogService svc) =>
{
    try { return Results.Json(svc.GetStory(code)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/story/{code}/status", (string code, StatusRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.SetStoryStatus(code, r.Status)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

// Which epic you are working in. A choice, not a calculation — see BacklogService.SetCurrentEpic.
app.MapPost("/api/epic/{number:int}/current", (int number, BacklogService svc) =>
{
    try { return Results.Json(svc.SetCurrentEpic(number)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

// Tasks and test cases are replaced wholesale: the client sends the list it wants the file to hold.
// Add, edit, delete, reorder and toggle are all this one call, so there is no per-item addressing
// to drift out of step when a stale tab posts an index that has since moved.
app.MapPut("/api/story/{code}/tasks", (string code, TaskListRequest r, BacklogService svc) =>
{
    try
    {
        svc.SetTasks(code, r.Tasks ?? new List<TaskItem>());
        return Results.Json(svc.GetStory(code));
    }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPut("/api/story/{code}/test-cases", (string code, TestCaseListRequest r, BacklogService svc) =>
{
    try
    {
        svc.SetTestCases(code, r.TestCases ?? new List<TestCase>());
        return Results.Json(svc.GetStory(code));
    }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

// What Sync reports: parse errors with a line number, plus the index-versus-folders integrity
// checks that splitting storage made possible in the first place.
app.MapGet("/api/validate", (BacklogService svc) => Results.Json(svc.Validate()));

// Optional convenience: `git add` the resolved backlog file. Never commits.
app.MapPost("/api/git/stage", (BacklogService svc) =>
{
    try
    {
        var backlogPath = svc.BacklogPath; // resolve once — avoids acting on two different paths
                                            // if Configure changes it mid-request
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path.GetDirectoryName(backlogPath)!,
            RedirectStandardError = true, UseShellExecute = false
        };
        psi.ArgumentList.Add("add");
        psi.ArgumentList.Add(Path.GetFileName(backlogPath));
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode == 0 ? Results.Ok(new { staged = true })
                               : Results.Problem(p.StandardError.ReadToEnd());
    }
    catch (Exception e) { return Results.Problem(e.Message); }
});

// Built on request, not held: the files change on every click, so a cached index would be stale by
// the time anyone searched it. The browser filters what this returns, so typing hits nothing.
// Same 422 as the board when the index will not parse: search cannot list stories the file does not
// yield, and the report says which line to fix rather than leaving the panel mysteriously empty.
app.MapGet("/api/search-index", (BacklogService svc) =>
{
    try { return Results.Json(SearchIndex.Build(svc)); }
    catch (Exception) { return Results.Json(svc.Validate(), statusCode: 422); }
});

app.MapGet("/api/config", (AppConfigService cfg) => Results.Json(cfg.Load()));

app.MapPost("/api/config/backlog", (PathRequest r, AppConfigService cfg) =>
{
    try { return Results.Json(cfg.SetBacklogPath(r.Path)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
    catch (IOException) { return Results.BadRequest("That file could not be created. Check the folder exists and is writable."); }
    catch (UnauthorizedAccessException) { return Results.BadRequest("That location is not writable."); }
});

app.MapPost("/api/config/skills", (PathRequest r, AppConfigService cfg) =>
{
    try { return Results.Json(cfg.SetSkillsPath(r.Path)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

// Projects are remembered pairs of paths, nothing more. Adding one never copies or moves a backlog,
// and removing one never deletes it — every one of these endpoints edits a list.
// Passes the path actually resolved for this request, so a deploy-time override — which is never
// written to config — still appears as the current project rather than leaving the page empty.
app.MapGet("/api/projects", (AppConfigService cfg, BacklogService svc) =>
    Results.Json(new ProjectList(cfg.Projects(svc.BacklogPath), !string.IsNullOrWhiteSpace(overridePath))));

app.MapPost("/api/projects", (AddProjectRequest r, AppConfigService cfg) =>
{
    try { return Results.Json(cfg.AddProject(r.BacklogPath, r.SkillsPath)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
    catch (IOException) { return Results.BadRequest("That file could not be created. Check the folder exists and is writable."); }
    catch (UnauthorizedAccessException) { return Results.BadRequest("That location is not writable."); }
});

app.MapPost("/api/projects/select", (PathRequest r, AppConfigService cfg) =>
{
    // Refused rather than silently ineffective: with an override set, this would write the config
    // and leave the board showing the pinned backlog, which reads as the app ignoring the click.
    if (!string.IsNullOrWhiteSpace(overridePath))
        return Results.BadRequest("This instance is pinned to one backlog by its deployment "
                                + "configuration (BacklogPath). Remove that setting to switch projects.");

    try { return Results.Json(cfg.SelectProject(r.Path)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/projects/remove", (RemoveProjectRequest r, AppConfigService cfg) =>
{
    try { return Results.Json(cfg.RemoveProject(r.Path, r.ConfirmName)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

// The name lives in the backlog's own `project:` field, so this rewrites the file rather than
// storing a copy that could disagree with it.
app.MapPost("/api/config/name", (NameRequest r, AppConfigService cfg, BacklogService svc) =>
{
    try
    {
        cfg.SetProjectName(svc.BacklogPath, r.Name);
        return Results.Json(cfg.Load());
    }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
    catch (IOException) { return Results.BadRequest("That backlog file could not be written."); }
    catch (UnauthorizedAccessException) { return Results.BadRequest("That backlog file is not writable."); }
});

/// <summary>A filename-safe stamp for one project, so two projects cannot share a logo file.</summary>
static string LogoSlug(string? backlogPath)
{
    var full = Path.GetFullPath(backlogPath ?? "demo");
    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full));
    return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
}

app.MapPost("/api/config/logo", async (HttpRequest req, AppConfigService cfg, IWebHostEnvironment env) =>
{
    if (!req.HasFormContentType) return Results.BadRequest("Expected multipart/form-data");
    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("logo");
    if (file is null || file.Length == 0) return Results.BadRequest("No file uploaded");
    if (file.Length > 2 * 1024 * 1024) return Results.BadRequest("Logo must be under 2 MB");

    // A pattern rather than an array: this ran on every upload and allocated the list each time.
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (ext is not (".png" or ".jpg" or ".jpeg" or ".svg" or ".webp"))
        return Results.BadRequest("Logo must be PNG, JPG, SVG, or WebP");

    var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
    Directory.CreateDirectory(uploadsDir);

    // Named after the project, not "logo.png" — with several projects a fixed name meant each
    // upload silently replaced the last one's image while both configs still pointed at it.
    var savedName = $"logo-{LogoSlug(cfg.Load().BacklogPath)}{ext}";
    await using (var stream = File.Create(Path.Combine(uploadsDir, savedName)))
        await file.CopyToAsync(stream);

    return Results.Json(cfg.SetLogoPath($"/uploads/{savedName}"));
});

// Both skill endpoints resolve the skills root the same way BacklogService does. Reading it
// straight from config instead meant a backlog configured without an explicit skills folder could
// load its tasks and test cases but not its descriptions — two answers to one question.
app.MapGet("/api/skill", (string path) =>
{
    var skillsRoot = ResolveSkillsPath();
    if (string.IsNullOrWhiteSpace(skillsRoot)) return Results.NotFound("No skills folder configured.");

    var resolved = SkillFileResolver.Resolve(skillsRoot, path);
    if (resolved is null || !File.Exists(resolved)) return Results.NotFound("SKILL.md not found.");

    return Results.Text(File.ReadAllText(resolved), "text/plain");
});

// Saves an edited SKILL.md. Only writes inside the configured skills folder, and only over a file
// that already exists — this endpoint edits specs, it does not create arbitrary files on disk.
app.MapPost("/api/skill", (SaveSkillRequest r) =>
{
    var skillsRoot = ResolveSkillsPath();
    if (string.IsNullOrWhiteSpace(skillsRoot)) return Results.BadRequest("No skills folder is configured yet.");

    var resolved = SkillFileResolver.Resolve(skillsRoot, r.Path);
    if (resolved is null) return Results.BadRequest("That path is outside the skills folder.");
    if (!File.Exists(resolved)) return Results.BadRequest("That SKILL.md does not exist.");

    File.WriteAllText(resolved, r.Content ?? "", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    return Results.Ok(new { saved = true });
});

// Clears the logo. The uploaded file is left on disk — harmless, gitignored, and it means an
// accidental removal can be undone by re-uploading rather than losing the original.
app.MapDelete("/api/config/logo", (AppConfigService cfg) => Results.Json(cfg.SetLogoPath(null)));

// Creating epics and stories. Validation failures come back as 400 with a message written for
// the person using the app, so the form can show it verbatim.
app.MapPost("/api/epic", (AddEpicRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.AddEpic(r.Number, r.Title, r.Version, r.Release)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/story", (AddStoryRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.AddStory(r.EpicNumber, r.Code, r.Title, r.Description)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/epic/{number:int}", (int number, RenameRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.RenameEpic(number, r.Title)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapDelete("/api/epic/{number:int}", (int number, BacklogService svc) =>
{
    try { return Results.Json(svc.DeleteEpic(number)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/story/{code}", (string code, EditStoryRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.EditStory(code, r.Title)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapDelete("/api/story/{code}", (string code, BacklogService svc) =>
{
    try { return Results.Json(svc.DeleteStory(code)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

// There is no "create a skill file" endpoint any more: adding a story creates its folder and its
// SKILL.md from the template in the same call, so there is never a story without one.

// ---------------------------------------------------------------------------------------------
// Page routes. The server owns the route table: which URLs exist, and whether the thing a URL
// names actually exists. A blanket fallback would answer 200 to every typo, so a stale bookmark
// or a mistyped path would look like a working page.
//
// What it deliberately does NOT do is render each view. The browser already holds the whole board
// and swaps views without a request, which is what makes navigation instant; re-rendering server
// side would trade that for a round trip per click. So each of these returns the same shell and
// the client router picks the view — but only for routes declared here, and only when the epic,
// story or release is real.
// ---------------------------------------------------------------------------------------------
var shellPath = Path.Combine(app.Environment.WebRootPath, "index.html");
IResult Shell(int status = StatusCodes.Status200OK) =>
    Results.Text(File.ReadAllText(shellPath), "text/html; charset=utf-8", statusCode: status);

// A route that exists but names something gone — a bookmark to a deleted story — still returns the
// app so you land somewhere useful, but says 404 rather than pretending it found it.
IResult ShellOr404(bool exists) => Shell(exists ? StatusCodes.Status200OK : StatusCodes.Status404NotFound);

app.MapGet("/", () => Shell());
app.MapGet("/releases", () => Shell());
app.MapGet("/configure", () => Shell());
app.MapGet("/projects", () => Shell());
app.MapGet("/add-project", () => Shell());
app.MapGet("/remove-project", () => Shell());
app.MapGet("/add-epic", () => Shell());
app.MapGet("/add-story", () => Shell());
app.MapGet("/edit-epic", () => Shell());
app.MapGet("/edit-story", () => Shell());

app.MapGet("/releases/{tag}", (string tag, BacklogService svc) =>
{
    var board = svc.GetBoard();
    var used = board.Epics
        .Select(e => string.IsNullOrWhiteSpace(e.Release) ? "Unscheduled" : e.Release);
    return ShellOr404(used.Contains(tag) || board.Roadmap.Contains(tag));
});

// The hierarchy itself is the path: /core-application, then /core-application/checkout-and-payment.
// Nothing else appears in it — no "epic/" or "story/" filler — so the URL always reads back as the
// breadcrumb. The literal routes above are more specific, so they are matched first.
app.MapGet("/{epicSlug}", (string epicSlug, BacklogService svc) =>
    ShellOr404(svc.GetBoard().Epics.Any(e => e.Slug == epicSlug)));

app.MapGet("/{epicSlug}/{storySlug}", (string epicSlug, string storySlug, BacklogService svc) =>
{
    var epic = svc.GetBoard().Epics.FirstOrDefault(e => e.Slug == epicSlug);
    return ShellOr404(epic is not null && epic.Stories.Any(s => s.Slug == storySlug));
});

// Anything not declared above is a 404, as it would be for any other web server.

app.Run();

// The request bodies each endpoint binds. Sealed because nothing derives from them and the JIT
// devirtualizes their members once it knows there is no subtype.
sealed record StatusRequest(string Status);
sealed record TaskListRequest(List<TaskItem> Tasks);
sealed record TestCaseListRequest(List<TestCase> TestCases);
sealed record PathRequest(string Path);
sealed record AddProjectRequest(string BacklogPath, string? SkillsPath);
sealed record NameRequest(string Name);
sealed record RemoveProjectRequest(string Path, string ConfirmName);
sealed record AddEpicRequest(int Number, string Title, string? Version, string? Release);
sealed record AddStoryRequest(int EpicNumber, string Code, string Title, string? Description);
sealed record SaveSkillRequest(string Path, string Content);
sealed record RenameRequest(string Title);
sealed record EditStoryRequest(string Title);
