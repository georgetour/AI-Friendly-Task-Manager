# AI Friendly Task Manager

AI Friendly Jira/AzureDevOps like backlog board over plain YAML and markdown files in your repo. No database, no accounts, no build step.

It's a small web board over plain files in your project. Click a status and it's saved straight
away — no save button, no database, no account.

## Try it right now

**[georgetour.github.io/AI-Friendly-Task-Manager](https://georgetour.github.io/AI-Friendly-Task-Manager/)** — the whole
board, on a demo backlog of 5 epics and 24 stories. No sign-in, no install, nothing to accept.

Click statuses, tick tasks, search, edit a description. It is the real application, not a video or a
mock-up: the same HTML, CSS and JavaScript this repo serves, with every answer the API would give
baked in beside it. Your clicks last as long as the tab does and are saved nowhere — there are no
files on a web page. That is the only thing the demo cannot do.

## Run it without installing anything

GitHub can give you a cloud machine with .NET already on it, running AI Friendly Task Manager on the same demo
backlog — with your changes actually written to files. You need a GitHub account, and it uses your
Codespaces quota.

**1.** Click the button:

[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/georgetour/AI-Friendly-Task-Manager)

**2.** On the *Create a new codespace* screen, leave everything as it is and press **Create
codespace**. VS Code opens in your browser and starts building — a few minutes the first time.

**3.** If you are asked whether you trust the authors of this repository, say yes. The Codespace
runs the project's build, which is what starts the app.

**4.** A notification appears: *"Your application running on port 5249 is available."* Click
**Open in Browser**. That is the board.

> If you miss the notification, the terminal prints the address — or open the **PORTS** tab and use
> the globe icon on port 5249. Don't use the in-editor preview: the app forbids being shown in a
> frame, so that pane stays blank.

This is a development environment, not a preview — you get an editor with the app running beside it.

## Run it on your own machine

**One thing to install:** [.NET 9 or higher](https://dotnet.microsoft.com/download). Windows, Linux
and macOS all work. Check with `dotnet --version` — if it prints `9.` or higher, you're ready.

Get the code, either way:

```bash
git clone https://github.com/georgetour/AI-Friendly-Task-Manager.git
cd AI-Friendly-Task-Manager
```

<sub>No git? Use **Code ▾ → Download ZIP** on this page, unzip it, and open a terminal in the
folder — nothing here needs a repository.</sub>

Then:

```bash
dotnet run --project src/AIFTM.Api
```

Open **http://localhost:5249**. It's the same address every time. `Ctrl+C` stops it.

> **Already using port 5249?** You'll see
> `Failed to bind to address http://127.0.0.1:5249: address already in use`.
> Either close whatever is on it — often an earlier copy of AI Friendly Task Manager still running in another
> terminal — or pick a different port:
>
> ```bash
> dotnet run --project src/AIFTM.Api --urls http://localhost:5300
> ```

That's the whole setup. Nothing to install, configure or sign up for — the first run shows a demo
project with 5 epics and 24 stories so you can click around before pointing it at anything of yours.
The demo is a real, editable copy, and it's gitignored, so nothing you do to it is at stake.

## Where everything is stored

There are only three kinds of file. No database, no hidden state.

```
BACKLOG.yaml                     the index — every epic and story, and its status
skills/
  README.md                      explains the layout to anyone (or anything) reading the folder
  checkout-and-payment/          one folder per story, named after the story
    SKILL.md                     what the story IS — description, tasks, acceptance criteria
    tasks.yaml                   what has to be BUILT, and whether it's done
    test-cases.yaml              what has to be VERIFIED, and whether it passed
```

**`BACKLOG.yaml` is an index, deliberately.** It holds epics, stories, their status and which folder
each story owns — and nothing else. That's what lets the board open instantly no matter how much
detail your stories carry: drawing the board never opens a single story folder.

**Each story owns a folder.** Everything about that one story lives in it. Point a coding agent at
`skills/checkout-and-payment/` and it has the whole picture — the prose, the work, the checks —
without reading anything else.

**Why two files for tasks and test cases?** They're different questions. Tasks answer *is it built*;
test cases answer *does it work*. They're written at different times by different people, so ticking
a task never rewrites your test results.

## What each action writes

Every button maps to exactly one file. Nothing writes to two places at once.

| What you do | What changes on disk |
|---|---|
| Add an epic | `BACKLOG.yaml` — a new epic, numbered for you |
| Add a story | `BACKLOG.yaml` — a new story, plus a new `skills/<story>/` folder with a starter `SKILL.md` |
| Rename an epic | `BACKLOG.yaml` — the title only; its stories are untouched |
| Set the current epic | `BACKLOG.yaml` — one line, `currentEpic:` |
| Change a story's status | `BACKLOG.yaml` — one line. Marking it **Done** with tasks unticked or test cases unpassed asks first |
| Add / edit / delete / tick / reorder a task | `skills/<story>/tasks.yaml` |
| Add / delete / reorder a test case, or change its result | `skills/<story>/test-cases.yaml` |
| Edit a description | `skills/<story>/SKILL.md` |
| Delete a story | `BACKLOG.yaml` — the entry — and then its whole folder |
| Delete an epic | `BACKLOG.yaml` — the epic — and the folders of every story in it |
| Upload a logo, change paths | `aiftm.config.json` (settings only — never your backlog) |
| **Search** | **nothing** — it reads your stories to find your words |
| **Reload from file** | **nothing** — it re-reads and checks your files |
| **Stage in git** | **nothing** — it runs `git add` on your backlog |

Notice what's *not* there: changing a status never touches a story's tasks, and ticking a task never
touches the backlog. That's why two people editing different things don't collide.

**Until you point it at your own project**, all of that happens inside
`src/AIFTM.Api/data/`, which is gitignored. Your scribbling on the demo can't end up in a commit.

## Point it at your own project

1. Click **Projects**, then **Add a project**.
2. Give it the path to your `BACKLOG.yaml` — for example `C:/projects/my-app/BACKLOG.yaml`.
   If the file isn't there yet, it's created from a template.
3. **Add project.** Your board loads.

Add as many as you like and switch between them from the same page. Each backlog stays where it is,
beside the code it describes — nothing is moved or copied. Your list lives in `aiftm.config.json`
next to the app, and is never committed.

<sub>Coming from a single-file `BACKLOG.md`? `dotnet run --project src/AIFTM.Api -- migrate
path/to/BACKLOG.md` converts it, and never overwrites anything you already have.</sub>

That writes `BACKLOG.yaml` beside it and one folder per story. It never overwrites an existing
`BACKLOG.yaml` or a `SKILL.md` you already wrote, and your `BACKLOG.md` is left exactly as it was.

## Writing the files by hand

They're plain text and they're yours — edit them in any editor, review them in a pull request, grep
them. The quickest start is to copy `templates/BACKLOG.template.yaml` and `templates/skills/`.

<details>
<summary>The whole format, in full</summary>

**`BACKLOG.yaml`**

```yaml
project: My Project
roadmap: [V0.1, V1]
currentEpic: 0          # optional — the epic you're working in, by number

epics:
  - number: 0
    title: Your Epic Title
    stories:
      - code: US-01
        title: Your Story Title
        status: In Progress
        release: V1
        folder: your-story-title
```

**`skills/your-story-title/tasks.yaml`**

```yaml
- text: Something to build
  done: false
```

**`skills/your-story-title/test-cases.yaml`**

```yaml
- text: Something to check
  status: Not Run
```

**`skills/your-story-title/SKILL.md`** is markdown, and a story is three sections — nothing else:

```markdown
# Your Story Title

## Description

What happens and why, in plain words. "As a [actor], I want to [action], so that [benefit]" when
there's a real person driving it.

## Tasks

1. **Something to build** — what it delivers. *(→ AC1)*

## Acceptance Criteria

- [ ] AC1: An observable, checkable outcome.
```

That's the whole shape. Tasks here are the refinement narrative — one deliverable per line, each
naming the criterion it satisfies; the tickable list the board counts is `tasks.yaml` beside it.
Test cases don't belong in this file at all: they're `test-cases.yaml`, because they carry a result.
Technical detail — flows, data model, gotchas — belongs in your own design docs, not here.

**Statuses:** Not Yet Started · Under Review · Refined · In Progress · Vendor Test · Done · On Hold

**Test-case results:** Not Run · Passed · Failed

Write them as plain words. The emoji you see on screen are added by the app, not stored in the file.

If you get the YAML wrong, **Reload from file** tells you the file and the line, rather than showing
you an empty board.

</details>

## Why it's built this way

- **No database.** Your files are the data. They're already in git, with full history.
- **No cost.** No licences, no seats, no hosting.
- **No setup.** One command. No Node, no npm, no build step.
- **Readable diffs.** A click changes one value and leaves the rest of the file alone.
- **Still just files.** Edit them by hand, review them in a pull request, grep them.
- **AI Friendly** AI can easily understand the structure and files needed.

> AI Friendly Task Manager runs on localhost and has no login. Don't expose it on a public network.

## How it works

```
Browser                                C# (ASP.NET Core)
────────────────────────────           ──────────────────────────────────────────
index.html, app.css, app.js  ◄──       served as static files
      │
      ├─ GET  /api/board          ──►  BACKLOG.yaml — epics and stories only
      ├─ GET  /api/story/US-01    ──►  that story folder's tasks and test cases
      ├─ POST /api/story/…/status ──►  one line in BACKLOG.yaml
      ├─ PUT  /api/story/…/tasks  ──►  that story's tasks.yaml
      ├─ GET  /api/search-index   ──►  every story, its tasks and its test cases
      └─ GET  /api/validate       ──►  check every file, report line numbers
```

The board reads only the index, so it stays fast however much detail your stories carry. A story's
tasks and test cases are fetched when you open it, and not before.

Search is the one thing that reads everything, once, when you open it — then your typing filters it
in the browser and costs nothing. It's built fresh every time rather than kept: your files change
every time you click, and a search that can't find the task you added a minute ago is worse than one
that takes a moment to open.

Writes are whole-file: read, change, write back. One writer, so nothing more elaborate is needed,
and the output is stable enough that one status change is one line in `git diff`.

```
src/AIFTM.Api/
├── Backlog/         reading and writing BACKLOG.yaml and the story folders
│   └── Legacy/      importing an old BACKLOG.md — nothing else uses it
├── Services/        settings, path resolution
├── Demo/            writing the static demo site
└── wwwroot/         the screen — index.html, app.css, app.js, vendor/alpine-csp.min.js
templates/           a starter BACKLOG.yaml and skills/ folder
tests/               test suite
```

<details>
<summary>How the live demo works with no server</summary>

```bash
dotnet run --project src/AIFTM.Api -- export-demo _site /AI-Friendly-Task-Manager
```

That writes a folder you can serve anywhere static: `wwwroot` copied verbatim — the same
`index.html`, `app.js` and stylesheet you just ran — plus `demo-data.js` holding every response the
API would have produced, and `demo-api.js`, which answers `fetch` from it. `app.js` is not told any
of this and does not know the difference.

There is deliberately **no second copy of the app**. A demo built as its own little clone starts
lying the day after you write it; this one cannot drift, because it is the same files, and the data
comes from the same `BacklogService` and `SearchIndex` the running app uses. Two things are edited
in `index.html` and nothing else: the folder it is served from, and the three absolute asset paths.
A test asserts exactly that by undoing both and comparing.

Writes go to a copy held in the page, so clicking works and a reload starts over. Anything that
needs real files — staging in git, adding an epic — says so rather than failing silently.

Each asset is published under a name taken from a hash of its own contents (`app.874c0eaa.js`).
GitHub Pages caches everything it serves for ten minutes and that header is its own, so without this
a returning visitor can end up running a fresh page against a stale script — a combination that
never existed anywhere. A release that changes nothing keeps its URLs and stays cached.

</details>

## Technologies

| | | |
|---|---|---|
| **.NET 9** | ASP.NET Core Minimal API | the only thing you install |
| **YamlDotNet** 18.1 | reads and writes the files | one NuGet package |
| **Alpine.js** (CSP build) | 60 KB, vendored | no CDN, works offline |
| `index.html` · `app.css` · `app.js` | the entire front end | three files, no build step |

No Node, no npm, no bundler, no build step. Two dependencies in total.

If you came looking for either in practice, this is a working reference for both: **YamlDotNet**
round-tripping a real file deterministically enough that one edit is one line of `git diff`, and
**Alpine's CSP build** driving a whole app under a strict `Content-Security-Policy` — no `eval`, no
inline scripts, nothing loaded from another site. Text from your files reaches the page as text,
never as markup. Both are lightly documented elsewhere.

**Responsive down to 320px.** Below 900px the sidebar becomes a drawer; below 768px it's gone and a
bottom bar carries the destinations with the add action raised in the centre. Nothing scrolls
sideways at any width. Add it to your phone's home screen and it behaves like an app.

## Performance

At **1,000 stories across 50 epics**, the board loads in **20 ms** and opening a story takes **19 ms**
— and opening a story costs the same there as at 20 stories, because it reads one folder rather than
the whole backlog.

<details>
<summary>The numbers, and how to reproduce them</summary>

Driven for 5 minutes against 3,000 story files with a realistic mix of reading, ticking and writing:
7,225 operations, zero errors. Milliseconds:

| Action | p50 | p95 | p99 |
|---|---|---|---|
| Open a story | 19 | 32 | 45 |
| Load the board | 20 | 31 | 52 |
| Tick a task | 39 | 55 | 77 |
| Add a task | 39 | 56 | 88 |
| Set a test result | 39 | 53 | 81 |
| Change a status | 46 | 87 | 111 |
| Check every file (Sync) | 236 | 267 | 498 |

Keeping tasks and test cases in the index instead measured **270 ms per board load against 20 ms** —
which is why they aren't in it.

Opening search reads the same 3,000 files Sync does, so that 236 ms is its worst case at a thousand
stories — once per open, and nothing after it. Typing is filtering a list already in the browser.

```bash
node tests/perf/generate.js /tmp/perf 50 20     # 50 epics × 20 stories
dotnet run -c Release --project src/AIFTM.Api -- --BacklogPath=/tmp/perf/BACKLOG.yaml
node tests/perf/drive.js http://localhost:5249 300
```

</details>

## Run the tests

```bash
dotnet test
```

---

MIT licensed — see [LICENSE](LICENSE). AI Friendly Task Manager is an independent project with no connection to
Atlassian or Microsoft; Jira and Azure DevOps are their trademarks.
