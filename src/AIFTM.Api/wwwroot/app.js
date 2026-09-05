/* ============================================================================
   AI Friendly Task Manager — the browser half.

   One Alpine component. It holds state, talks to the API, and shapes the JSON
   into exactly what index.html binds to. It never builds HTML: the only place
   markup is produced is renderMarkdown(), for the one field that is markdown by
   definition, and that output is escaped before any transform touches it.

   Written against Alpine's CSP build, so expressions in the markup are property
   reads and method calls only. Anything that needs a template literal, a
   ternary chain or a computation is precomputed here as a plain value — which
   is why decorate() exists.
   ============================================================================ */

const STATUSES = [
  { label:"Not Yet Started", emoji:"⬜", cls:"st-nys"  },
  { label:"Under Review",    emoji:"🔍", cls:"st-rev"  },
  { label:"Refined",         emoji:"✨", cls:"st-ref"  },
  { label:"In Progress",     emoji:"🔄", cls:"st-prog" },
  { label:"Vendor Test",     emoji:"🧪", cls:"st-test" },
  { label:"Done",            emoji:"✅", cls:"st-done" },
  { label:"On Hold",         emoji:"⏸", cls:"st-hold" },
];
const TC_STATUSES = [
  { label:"Not Run", emoji:"⬜", cls:"st-nys"  },
  { label:"Passed",  emoji:"✅", cls:"st-done" },
  { label:"Failed",  emoji:"❌", cls:"st-fail" },
];
// The files store a plain word — "In Progress". The emoji is presentation and lives only here.
const CLASS_FOR = {}, EMOJI_FOR = {};
STATUSES.concat(TC_STATUSES).forEach(s => { CLASS_FOR[s.label] = s.cls; EMOJI_FOR[s.label] = s.emoji; });

/** Ranks statuses by how much attention an epic is getting — the rule the roll-up's 📍 uses. */
const ACTIVITY = { "In Progress":5, "Vendor Test":4, "Under Review":3, "Refined":2, "Not Yet Started":1, "Done":0, "On Hold":0 };

/** Form pages, by URL. Everything else is a board view, which has its own URL shape — see
 *  routePath() and readUrl(). Every screen is addressable, so the breadcrumb, the address bar and
 *  the browser's back button always agree with each other. */
const PAGES = { "/configure":"configure", "/projects":"projects", "/add-project":"add-project",
                "/remove-project":"remove-project", "/add-epic":"add-epic", "/add-story":"add-story",
                "/edit-epic":"edit-epic", "/edit-story":"edit-story" };
const PATH_FOR = { configure:"/configure", projects:"/projects", "add-project":"/add-project",
                   "remove-project":"/remove-project", "add-epic":"/add-epic", "add-story":"/add-story",
                   "edit-epic":"/edit-epic", "edit-story":"/edit-story" };

const pct = (a, b) => (b === 0 ? 0 : Math.round((a / b) * 100));
const width = n => "width:" + n + "%";
const plural = (n, one, many) => n + " " + (n === 1 ? one : many);

function esc(s){
  return String(s == null ? "" : s).replace(/[&<>"]/g, c => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;" }[c]));
}

/**
 * The folder the app is served from, read from <meta name="app-base">. Empty when it owns the root,
 * which is every `dotnet run` — so nothing changes for the normal case.
 *
 * It exists because a GitHub Pages project site lives in a folder: the same files are served from
 * /AI-Friendly-Task-Manager/ rather than from /. Every URL the app builds — routes, history entries, API calls —
 * goes through here, so there is one place that knows.
 *
 * Not a <base> tag: the app's own CSP sends base-uri 'none', and that directive is there so an
 * injected <base> cannot silently re-point every relative URL on the page.
 */
const BASE = (document.querySelector('meta[name="app-base"]')?.content || "/").replace(/\/+$/, "");

/** An app path ("/api/board", "/core-application") as a URL the browser can use. */
const url = path => BASE + path;

/** The reverse: what the app calls the current address, with the base taken off. */
const appPath = () => {
  const path = location.pathname;
  if(!BASE || !path.startsWith(BASE)) return path;
  return path.slice(BASE.length) || "/";
};

async function api(path, method, body){
  const res = await fetch(url(path), {
    method: method || "GET",
    headers: body ? { "Content-Type":"application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  if(!res.ok) throw new Error((await res.text()) || String(res.status));
  return res.json();
}

/**
 * Turns the index into the shape the markup binds to: every label and class name is computed once,
 * here. The CSP build cannot format strings in an attribute, and even if it could, doing it per
 * binding would recompute on every keystroke elsewhere on the page.
 *
 * The board carries no tasks or test cases — those live in each story's folder and arrive from
 * /api/story/{code} only when that story is opened. That is what keeps this fast as the backlog
 * grows: nothing above the story page ever parses detail it does not render.
 */
function decorate(board){
  const epics = (board.epics || []).map(epic => {
    const stories = (epic.stories || []).map(story => Object.assign({}, story, {
      statusClass: CLASS_FOR[story.status] || "st-nys",
      emoji:       EMOJI_FOR[story.status] || "⬜",
      // A release is optional, so this slot is hidden rather than removed — see the row markup.
      releaseSlotClass: story.release ? "" : "empty",
    }));

    const count = stories.length;
    return Object.assign({}, epic, {
      stories,
      countLabel:  plural(count, "story", "stories"),
      optionLabel: epic.number + " — " + epic.title,
      railTitle:   epic.title + " · " + plural(count, "story", "stories"),
      activity:    Math.max(0, ...stories.map(s => ACTIVITY[s.status] || 0)),
      isCurrent:   false,
    });
  });

  // Exactly one epic is current. What you chose wins; with nothing chosen it is inferred from the
  // statuses, so a backlog written before this field existed still marks the work in flight.
  let chosen = epics.find(e => e.number === board.currentEpic) || null;
  if(!chosen){
    let best = 0;
    epics.forEach(e => { if(e.activity > best){ best = e.activity; chosen = e; } });
  }
  if(chosen) chosen.isCurrent = true;

  // Precomputed here because the CSP build cannot choose a label or a class in the markup.
  epics.forEach(e => {
    e.currentLabel = e.isCurrent ? "CURRENT EPIC" : "Set as current";
    e.currentClass = e.isCurrent ? "on" : "";
    e.currentTitle = e.isCurrent
      ? (board.currentEpic === e.number
          ? "This is the current epic"
          : "Current by default — the most advanced work is here. Click another epic to choose one.")
      : "Set as the current epic";
  });

  return { project: board.project || "", epics, roadmap: board.roadmap || [],
           currentEpic: board.currentEpic ?? null };
}

/**
 * Trims the two things the page would otherwise say twice: the YAML frontmatter, and a leading
 * "# Title" that the story page already shows above the card.
 *
 * Display only. The editor and the file on disk keep both — the frontmatter is what an agent reads
 * to know when the skill applies, and the title is what makes the file stand on its own in a diff.
 * The same reasoning is in SearchIndex.cs, which drops them from the index for the same reason:
 * they are machine metadata and a heading, not the story.
 */
function forDisplay(markdown){
  const lines = markdown.replace(/\r\n/g, "\n").split("\n");

  if(lines[0] === "---"){
    const end = lines.indexOf("---", 1);
    if(end > 0) lines.splice(0, end + 1);
  }
  while(lines.length && lines[0].trim() === "") lines.shift();

  // Only the first heading, and only when it is the first line left — a SKILL.md that opens with
  // prose keeps everything, and no other heading is touched.
  if(/^#\s+\S/.test(lines[0] || "")){
    lines.shift();
    while(lines.length && lines[0].trim() === "") lines.shift();
  }

  return lines.join("\n");
}

/**
 * Splits text into alternating plain and matched parts, so a hit can be shown highlighted without
 * building any HTML.
 *
 * The markup renders these with x-for and two spans — the alternative would be assembling a string
 * with <mark> in it and handing it to x-html, which is the one thing this codebase keeps out of
 * JavaScript. Escaping correctly every time is a promise; not producing markup at all is a fact.
 */
function splitOnMatch(text, needle){
  // hitClass rather than a boolean: Alpine's CSP build evaluates property reads only, so the class
  // a part should carry has to be decided here rather than in the markup.
  const plain = t => ({ t, hitClass: "" });
  const parts = [];
  const hay = String(text || "");
  if(!needle) return [plain(hay)];

  const lower = hay.toLowerCase(), find = needle.toLowerCase();
  let at = 0;
  for(;;){
    const i = lower.indexOf(find, at);
    if(i === -1){ if(at < hay.length) parts.push(plain(hay.slice(at))); break; }
    if(i > at) parts.push(plain(hay.slice(at, i)));
    parts.push({ t: hay.slice(i, i + find.length), hitClass: "mark" });
    at = i + find.length;
  }
  return parts.length ? parts : [plain(hay)];
}

/**
 * A window of text around the first hit, so an excerpt reads as a sentence rather than starting
 * mid-word 400 characters in. Trimmed to word boundaries at both ends.
 */
function excerptAround(text, needle, span = 120){
  const hay = String(text || "");
  const i = needle ? hay.toLowerCase().indexOf(needle.toLowerCase()) : -1;
  if(i === -1 || hay.length <= span) return hay.slice(0, span * 2);

  let from = Math.max(0, i - Math.floor(span / 2));
  let to = Math.min(hay.length, i + needle.length + Math.floor(span / 2));
  if(from > 0){ const s = hay.indexOf(" ", from); if(s > -1 && s < i) from = s + 1; }
  if(to < hay.length){ const e = hay.lastIndexOf(" ", to); if(e > i + needle.length) to = e; }

  return (from > 0 ? "… " : "") + hay.slice(from, to).trim() + (to < hay.length ? " …" : "");
}

/**
 * Marks every occurrence of a term inside an element that is already on screen — what you land on
 * after clicking a search result.
 *
 * This wraps existing text nodes in spans built with createElement and createTextNode. It is the
 * one place that touches the DOM directly, and it is allowed to because it produces no markup: no
 * string is ever parsed as HTML, so nothing in anyone's files can arrive as a tag. The alternative,
 * splicing <mark> into the rendered description, would mean matching inside HTML the app generated
 * — where searching for "span" or "class" rewrites the page's own structure.
 *
 * Alpine overwrites these spans whenever it re-renders the text it owns. That is the right outcome:
 * the mark belongs to the arrival, not to the data.
 */
function markMatches(root, needle){
  if(!root || !needle) return;

  const find = needle.toLowerCase();
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const targets = [];

  for(let node = walker.nextNode(); node; node = walker.nextNode()){
    const parent = node.parentElement;
    if(!parent || parent.classList.contains("mark")) continue;      // already marked
    if(/^(script|style|textarea)$/i.test(parent.tagName)) continue;
    if(node.nodeValue.toLowerCase().includes(find)) targets.push(node);
  }

  // Collected first, replaced after: mutating the tree while walking it skips nodes.
  for(const node of targets){
    const pieces = document.createDocumentFragment();
    for(const part of splitOnMatch(node.nodeValue, needle)){
      if(!part.hitClass){ pieces.appendChild(document.createTextNode(part.t)); continue; }
      const span = document.createElement("span");
      span.className = "mark";
      span.textContent = part.t;
      pieces.appendChild(span);
    }
    node.parentNode.replaceChild(pieces, node);
  }
}

/**
 * Wraps Tab around inside an open dialog, so focus cannot leave it for the page behind the veil.
 * aria-modal tells a screen reader the rest of the page is out of scope; this is what makes that
 * true for the keyboard as well.
 */
function keepTabInside(ev, selector){
  const panel = document.querySelector(selector);
  if(!panel) return;

  const stops = [...panel.querySelectorAll("input, button, [href], select, textarea")]
    .filter(el => !el.disabled && el.offsetParent !== null);
  if(!stops.length) return;

  const first = stops[0], last = stops[stops.length - 1];
  const on = document.activeElement;

  if(ev.shiftKey && (on === first || !panel.contains(on))){ ev.preventDefault(); last.focus(); }
  else if(!ev.shiftKey && on === last){ ev.preventDefault(); first.focus(); }
}

/**
 * Puts marked text back the way it was.
 *
 * Needed because Alpine only rewrites what changed: reopening the same story without a search term
 * leaves its title untouched, so a mark from last time would still be sitting on it.
 */
function clearMarks(root){
  if(!root) return;
  for(const span of root.querySelectorAll("span.mark"))
    span.replaceWith(document.createTextNode(span.textContent));
  root.normalize();      // rejoins the text nodes the marking split apart
}

/**
 * Where a dragged row would land, as an index into the list once the row being dragged has been
 * taken out of it. Measured against each row's midpoint, which is what makes the drop line flip
 * halfway across a neighbour rather than at its edge.
 */
function dropIndex(rows, y, from){
  for(let i = 0; i < rows.length; i++){
    const box = rows[i].getBoundingClientRect();
    if(y < box.top + box.height / 2) return i > from ? i - 1 : i;
  }
  return rows.length - 1;
}

/** Adds the display fields to one story's tasks and test cases, once per load. */
function decorateDetail(detail){
  const tasks = (detail.tasks || []).map(t => Object.assign({}, t));
  const testCases = (detail.testCases || []).map(t => Object.assign({}, t, {
    statusClass: CLASS_FOR[t.status] || "st-nys",
  }));
  return { tasks, testCases, loading: false, error: "" };
}

/**
 * Renders markdown to HTML. Everything is escaped first, so the transforms below only ever see
 * safe text — a description can never inject markup. Deliberately small: it covers what these
 * files actually contain rather than trying to be a complete CommonMark implementation.
 */
function renderMarkdown(src){
  // Only these schemes may become a link. Without this check a file containing [x](javascript:…)
  // would render a working script URL — escaping the text is not enough, because the danger is in
  // the href, not in the characters.
  const safeHref = url => (/^(https?:\/\/|mailto:|#|\/|\.{0,2}\/)/i.test(url) ? url : null);

  const inline = t => esc(t)
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
    .replace(/(^|[^*])\*([^*\n]+)\*/g, "$1<em>$2</em>")
    .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (m, text, url) => {
      const href = safeHref(url);
      return href ? '<a href="' + href + '" rel="noopener noreferrer" target="_blank">' + text + "</a>" : m;
    });

  const lines = String(src == null ? "" : src).replace(/\r\n?/g, "\n").split("\n");
  const out = [];
  let i = 0;

  // "- [ ] AC1: …" and "- [x] …". Rendered as a real checkbox rather than the literal characters,
  // and deliberately not clickable: these belong to the description file, whereas the tick-boxes
  // elsewhere on the page are the story's tasks in tasks.yaml. Two different things.
  const TASK_ITEM = /^\[([ xX])\]\s+([\s\S]*)$/;

  let list = [], ordered = false, para = [];
  const flushList = () => {
    if(!list.length) return;
    const items = list.map(x => {
      const m = x.match(TASK_ITEM);
      if(!m) return "<li>" + inline(x) + "</li>";
      const done = m[1] !== " ";
      return '<li class="md-task"><span class="md-box' + (done ? " on" : "") + '" aria-hidden="true">'
        + (done ? "✓" : "") + "</span><span>" + inline(m[2]) + "</span></li>";
    });
    const tag = ordered ? "ol" : "ul";
    const cls = list.some(x => TASK_ITEM.test(x)) ? ' class="md-tasks"' : "";
    out.push("<" + tag + cls + ">" + items.join("") + "</" + tag + ">");
    list = [];
  };
  const flushPara = () => { if(para.length){ out.push("<p>" + inline(para.join(" ")) + "</p>"); para = []; } };

  for(; i < lines.length; i++){
    const line = lines[i];

    if(line.startsWith("```")){                                     // fenced code
      flushPara(); flushList();
      const body = [];
      for(i++; i < lines.length && !lines[i].startsWith("```"); i++) body.push(lines[i]);
      out.push("<pre><code>" + esc(body.join("\n")) + "</code></pre>");
      continue;
    }
    if(/^\|.*\|\s*$/.test(line)){                                   // table
      flushPara(); flushList();
      const rows = [];
      for(; i < lines.length && /^\|.*\|\s*$/.test(lines[i]); i++) rows.push(lines[i]);
      i--;
      const cells = r => r.trim().replace(/^\||\|$/g, "").split("|").map(c => c.trim());
      const isSep = r => /^[\s|:-]+$/.test(r);
      const head = cells(rows[0]);
      const body = rows.slice(isSep(rows[1] || "") ? 2 : 1).map(cells);
      out.push('<div class="md-tablewrap"><table><thead><tr>'
        + head.map(c => "<th>" + inline(c) + "</th>").join("") + "</tr></thead><tbody>"
        + body.map(r => "<tr>" + r.map(c => "<td>" + inline(c) + "</td>").join("") + "</tr>").join("")
        + "</tbody></table></div>");
      continue;
    }

    const h = line.match(/^(#{1,6})\s+(.*)$/);
    if(h){
      flushPara(); flushList();
      const lvl = Math.min(h[1].length + 1, 6);                     // an h1 in the file is an h2 here
      out.push("<h" + lvl + ">" + inline(h[2]) + "</h" + lvl + ">");
      continue;
    }
    if(/^\s*[-*]\s+/.test(line)){
      flushPara(); if(ordered) flushList();
      ordered = false; list.push(line.replace(/^\s*[-*]\s+/, "")); continue;
    }
    if(/^\s*\d+\.\s+/.test(line)){
      flushPara(); if(!ordered) flushList();
      ordered = true; list.push(line.replace(/^\s*\d+\.\s+/, "")); continue;
    }
    if(/^>\s?/.test(line)){
      flushPara(); flushList();
      out.push("<blockquote>" + inline(line.replace(/^>\s?/, "")) + "</blockquote>"); continue;
    }
    if(/^(---|\*\*\*|___)\s*$/.test(line)){
      flushPara(); flushList(); out.push("<hr>"); continue;
    }
    if(line.trim() === ""){ flushPara(); flushList(); continue; }

    // A wrapped bullet: markdown lets a list item run onto the next line without indentation, and
    // that continuation belongs to the item rather than starting a new paragraph.
    if(list.length && !para.length){ list[list.length - 1] += " " + line.trim(); continue; }

    flushList();
    para.push(line.trim());
  }
  flushPara(); flushList();
  return out.join("");
}

document.addEventListener("alpine:init", () => {
  Alpine.data("aiftm", () => ({

    /* ------------------------------------------------------------ state -- */
    board: { project:"", epics: [], roadmap: [] },
    detail: { tasks: [], testCases: [], loading: false, error: "" },
    addingTask: false, addingTest: false, editingTask: -1, draftText: "",
    report: { show:false, title:"", cls:"", issues:[] },
    config: { backlogPath:null, skillsPath:null, logoPath:null },
    loadError: "",

    view: "board",          // board | epic | story | releases | release
    page: "",               // "" | configure | add-epic | add-story | edit-epic
    epicNumber: null,
    storyCode: null,
    releaseTag: null,
    editingEpic: null,
    seedEpic: null,          // which epic the add-story form should preselect, if reached from one
    projects: [],            // every remembered project, current one marked; loaded with the page
    pinned: false,           // a deploy-time BacklogPath override is fixing which backlog is used
    removing: { name:"", backlogPath:"" },   // the project the remove page is confirming

    search: { open:false, q:"", loading:false, index:null, results:[], count:0, active:-1,
              hint:false, hintText:"", announce:"", opener:null },

    highlight: "",           // the ?h= term to mark on the page a search result opened
    writeSeq: 0,             // orders overlapping writes to a story's folder — see saveTasks

    winWidth: window.innerWidth,
    sidebarCollapsed: false,
    drawerOpen: false,
    expanded: [],
    addOpen: false,
    addMobile: false,

    picker: { open:false, options:[], narrow:false, cls:"", pos:"", current:"", kind:"", story:null, tc:null },
    skill:  { path:null, original:"", draft:"", html:"", editing:false, loading:false, saving:false, error:"" },
    confirm:{ title:"", body:"", okLabel:"Delete", okClass:"btn danger" },

    form: {}, err: {}, saving: false,
    logoStamp: Date.now(),
    toastText: "", toastOn: false, toastTimer: null,

    /* ------------------------------------------------------------- init -- */
    init(){
      // Delegated, because the rows it applies to are created and destroyed by x-for.
      document.addEventListener("pointerdown", ev => {
        const grip = ev.target.closest && ev.target.closest(".grip");
        if(grip) this.beginDrag(grip, ev);
      });

      window.addEventListener("resize", () => { this.winWidth = window.innerWidth; });
      window.addEventListener("popstate", () => this.readUrl());
      document.addEventListener("keydown", ev => {
        // "/" opens search, the convention everywhere from GitHub to Slack — but only when you are
        // not already typing, or it would swallow the slash in a file path.
        const typing = /^(input|textarea|select)$/i.test((ev.target.tagName || ""));
        if(ev.key === "/" && !typing && !this.search.open){ ev.preventDefault(); this.openSearch(); return; }

        // A modal dialog owns the Tab key while it is open — otherwise focus walks out of it into
        // the page behind the veil, which is still there and still clickable to a keyboard.
        if(ev.key === "Tab" && this.search.open) return keepTabInside(ev, ".searchpanel");

        if(ev.key !== "Escape") return;
        this.addOpen = false;
        this.picker.open = false;
        if(this.search.open) this.closeSearch();
        if(this.drawerOpen) this.drawerOpen = false;
      });

      // The board first: on a first run the demo is materialised as a side effect of GET
      // /api/board, so asking for the config before that would race and come back empty.
      this.load()
        .catch(e => { this.loadError = "Failed to load the board: " + e.message; })
        .then(() => this.loadConfig())
        .then(() => this.readUrl());
    },

    async load(){
      this.board = decorate(await api("/api/board"));
      if(!this.expanded.length) this.expanded = this.board.epics.map(e => e.number);
    },

    /**
     * Projects are pairs of paths the app remembers. Switching one re-reads the board from the
     * newly current backlog — nothing is copied, and each project's files stay where they are,
     * beside the code they describe.
     */
    // ---------- search ----------
    //
    // The index is fetched once when the panel opens and filtered in memory from then on, so every
    // keystroke is free. Fetching on open rather than caching for the session is deliberate: the
    // files change constantly, and a search that cannot find the task you added a minute ago is
    // worse than one that takes a moment to open.

    async openSearch(){
      // Remembered so closing puts focus back where it came from, rather than dropping it on the
      // body and making the next Tab start from the top of the page.
      this.search.opener = document.activeElement;
      this.search.open = true;
      this.search.active = -1;
      this.focusRef("searchBox");
      this.runSearch();
      if(this.search.index) return;

      this.search.loading = true;
      this.search.hint = false;              // one note at a time, or the panel says two things
      try{ this.search.index = await api("/api/search-index"); }
      catch(e){ this.search.index = []; this.toast("Search could not read the backlog"); }
      finally{ this.search.loading = false; this.runSearch(); }
    },

    /** @param restore pass false when a result is being opened — focus belongs on what you opened,
     *  not back on the button you left. */
    closeSearch(restore){
      const back = restore === false ? null : this.search.opener;
      this.search.open = false;
      this.search.q = "";
      this.search.results = [];
      this.search.announce = "";
      this.search.opener = null;
      // Only if it is still on the page and still focusable — opening a result replaces the view.
      if(back && back.isConnected) back.focus();
    },

    /** Dropped on reload so the next search reflects whatever changed on disk. */
    forgetSearchIndex(){ this.search.index = null; },

    runSearch(){
      const q = this.search.q.trim();
      this.search.active = -1;

      if(q.length < 2 || !this.search.index){
        this.search.results = [];
        this.search.count = 0;
        this.search.announce = "";
        // A note in every empty state, not only after a keystroke: an open panel with a blank body
        // gives no clue what it searches, and it made the box change height as soon as you typed.
        this.search.hint = !this.search.loading;
        // Two characters, because one matches most of a backlog and the list is then noise.
        this.search.hintText = q.length === 1
          ? "Keep typing — two characters at least."
          : "Search story titles and codes, epics, tasks and test cases.";
        return;
      }

      const hit = t => String(t || "").toLowerCase().includes(q.toLowerCase());
      const out = [];

      for(const e of this.search.index){
        const isEpic = e.kind === "epic";
        // An epic matches by its own name only. Its stories are not dragged in with it: the epic is
        // a result you can open, and its page is the list of them — repeating all eight here would
        // bury every other hit under one match.
        const inTitle = hit(e.title) || hit(e.code);
        const parts = e.sections.filter(s => hit(s.text));
        if(!inTitle && !parts.length) continue;

        // Two excerpts, then a count — the same idea as "4 more on this page": enough to judge
        // relevance, never enough for one story to fill the panel.
        const shown = parts.slice(0, 2).map(s => ({
          kind: s.kind,
          parts: splitOnMatch(excerptAround(s.text, q), q),
        }));

        out.push({
          key: (isEpic ? "e:" : "s:") + e.epicSlug + "/" + e.storySlug + e.code,
          id: "sr" + out.length,          // what aria-activedescendant points at
          isEpic,
          isStory: !isEpic,
          url: isEpic ? "/" + e.epicSlug : "/" + e.epicSlug + "/" + e.storySlug,
          // An epic has no code and no status of its own; what it has is stories.
          subtitle: isEpic ? plural(e.storyCount, "story", "stories") : e.epicTitle,
          statusClass: CLASS_FOR[e.status] || "st-nys",
          status: e.status,
          titleParts: splitOnMatch(e.title, q),
          codeParts: splitOnMatch(e.code, q),
          excerpts: shown,
          moreLabel: parts.length > 2 ? (parts.length - 2) + " more in this story" : "",
        });
      }

      this.search.results = out;
      this.search.count = out.length;
      this.search.hint = out.length === 0;
      this.search.hintText = "Nothing matched “" + q + "”.";
      // The list is not what has focus, so the count has to be said rather than only shown.
      this.search.announce = plural(out.length, "result", "results") + " for " + q;
    },

    moveSearch(delta){
      if(!this.search.results.length) return;
      const n = this.search.results.length;
      this.search.active = (this.search.active + delta + n) % n;

      // Keep the current option in view — the list scrolls, and the arrow keys are the only way
      // through it for anyone not using a mouse.
      this.$nextTick(() => {
        const el = document.getElementById(this.activeResultId);
        if(el) el.scrollIntoView({ block: "nearest" });
      });
    },

    openActiveResult(){
      const r = this.search.results[this.search.active] || this.search.results[0];
      if(r) this.openSearchResult(r);
    },

    openSearchResult(r){
      // The term travels in the URL, the way MkDocs and Read the Docs do it. It survives a refresh,
      // it can be pasted to someone else, and it leaves on the next navigation without anything
      // having to remember to clear it — routePath() builds paths with no query string.
      const term = this.search.q.trim();
      this.closeSearch(false);
      history.pushState({}, "", url(r.url) + (term ? "?h=" + encodeURIComponent(term) : ""));
      this.readUrl();

      // Focus lands on the heading of what opened. Nothing moves it in a single-page app otherwise,
      // so a screen reader would still be sitting in a dialog that is no longer there, and the next
      // Tab would start again from the top of the page.
      const heading = r.isEpic ? "#epicView h2" : ".detail h1";
      this.$nextTick(() => requestAnimationFrame(() => {
        const el = document.querySelector(heading);
        if(el) el.focus();
      }));
    },

    resultClass(i){ return this.search.active === i ? "on" : ""; },

    /** The option the combobox is currently on, named for aria-activedescendant. Empty until an
     *  arrow key has been pressed, because nothing is current before then. */
    get activeResultId(){ return this.search.active >= 0 ? "sr" + this.search.active : ""; },

    selectedFlag(i){ return this.search.active === i ? "true" : "false"; },

    /** Marks the ?h= term on whichever view just opened, once Alpine has rendered it. */
    markHighlight(){
      const story = document.querySelector(".detail");
      const epic = document.querySelector("#epicView");

      this.$nextTick(() => requestAnimationFrame(() => {
        // Clear both first: every view stays in the document behind x-show, so last search's marks
        // are still sitting there waiting to be shown again.
        clearMarks(story);
        clearMarks(epic);
        if(!this.highlight) return;
        // Then mark only the one on screen.
        markMatches(this.isStoryView ? story : epic, this.highlight);
      }));
    },

    async loadProjects(){
      try{
        const res = await api("/api/projects");
        this.pinned = !!res.pinned;
        this.projects = (res.projects || []).map(p => Object.assign({}, p, {
          rowClass: p.isCurrent ? "on" : (p.missing ? "gone" : ""),
          // Precomputed: Alpine's CSP build evaluates property reads and method calls only, so a
          // label built from a value has to be built here rather than in the markup.
          checkLabel: p.isCurrent ? "Open" : "Check project",
          removeLabel: "Remove " + p.name,
          // Only a deploy override disables this, and only for the projects it stops you reaching.
          // The current project's button always works — it opens the board.
          locked: this.pinned && !p.isCurrent,
        }));
      }catch(e){ this.err.form = e.message || "The project list could not be loaded."; }
    },

    async selectProject(p){
      // Already open, so there is nothing to switch — but the button still has to do something,
      // and the obvious something is showing you the board it names. A control that is a no-op is
      // worse than no control.
      if(p.isCurrent) return this.goBoard();
      try{
        this.config = await api("/api/projects/select", "POST", { path: p.backlogPath });
        await this.load();
        this.goBoard();
        this.toast("Switched to " + p.name);
      }catch(e){ this.err.form = e.message || "That project could not be opened."; }
    },

    /**
     * Removing asks you to type the project's name. The server checks it too — this endpoint is
     * reachable without the UI, and this is the one action on the page a person could regret.
     */
    async submitRemoveProject(){
      this.err = {};
      const typed = (this.form.confirmName || "").trim();
      if(typed !== this.removing.name){
        this.err.confirmName = "That doesn't match. Type " + this.removing.name + " exactly.";
        return;
      }

      this.saving = true;
      try{
        this.config = await api("/api/projects/remove", "POST",
          { path: this.removing.backlogPath, confirmName: typed });
        await this.load();
        this.goProjects();
        this.toast("Project removed from the list");
      }catch(e){ this.err.form = e.message || "That project could not be removed."; }
      finally{ this.saving = false; }
    },

    async submitProject(){
      this.err = {};
      const backlog = (this.form.backlogPath || "").trim();
      if(!backlog) this.err.backlogPath = "Enter the path to a BACKLOG.yaml file.";
      else if(!/\.ya?ml$/i.test(backlog)) this.err.backlogPath = "That should be a .yaml file.";
      if(this.err.backlogPath) return;

      this.saving = true;
      try{
        this.config = await api("/api/projects", "POST",
          { backlogPath: backlog, skillsPath: (this.form.skillsPath || "").trim() });
        await this.load();
        // Back to the list rather than the board: you have just changed what the list contains, and
        // seeing the new entry marked Current is the confirmation that it worked.
        this.goProjects();
        this.toast("Project added");
      }catch(e){ this.err.form = e.message || "That project could not be added."; }
      finally{ this.saving = false; }
    },

    async loadConfig(){
      try{ this.config = await api("/api/config"); }
      catch(e){ /* non-fatal: the board is already usable, and Configure can still be opened */ }
    },

    /**
     * Sync. Splitting storage across an index and one folder per story made it possible for the two
     * to disagree — a story naming a folder that is not there, a folder nobody references, a file
     * that will not parse. That class of bug was impossible when it was all one file, so this
     * button pays it back: it checks the whole backlog and says exactly what is wrong and where.
     */
    async reload(){
      try{
        const report = await api("/api/validate");
        const issues = report.issues || [];
        const errors = issues.filter(i => i.severity === "error").length;
        const warnings = issues.length - errors;

        this.report = {
          show: issues.length > 0,
          cls: report.ok ? "warn" : "bad",
          // The board is still on screen behind this — these are integrity problems, not a failure
          // to load. Saying "stopping the backlog loading" while it is plainly loaded reads as a
          // lie and makes people distrust the rest of the message.
          title: report.ok
            ? plural(warnings, "thing worth a look", "things worth a look")
            : plural(errors, "problem to fix", "problems to fix"),
          issues: issues.map(i => Object.assign({}, i, {
            sevClass: i.severity === "error" ? "sev-bad" : "sev-warn",
          })),
        };

        if(!report.ok) return this.toast("The backlog has errors");

        await this.load();
        if(this.view === "story") await this.loadDetail();
        // Reload means "the files may have changed", which is exactly when a cached search index
        // stops being true.
        this.forgetSearchIndex();
        this.toast(issues.length ? "Reloaded — with warnings" : "Reloaded from BACKLOG.yaml");
      }catch(e){
        this.toast("Could not read the backlog");
      }
    },

    dismissReport(){ this.report.show = false; },

    async stage(){
      // The server's own words when it has them: "not a git repository" and "not in the demo" are
      // different problems, and "Stage failed" tells you neither.
      try{ await api("/api/git/stage", "POST"); this.toast("Staged (git add)"); }
      catch(e){ this.toast(e.message || "Stage failed"); }
    },

    /* --------------------------------------------------------- routing -- */
    /**
     * The URL for whatever is on screen — and it is exactly the breadcrumb:
     *   Overview › Core Application › Checkout and Payment
     *   /          core-application / checkout-and-payment
     * The slugs come from the board JSON. They are generated in C# (Slugs.cs) and never recomputed
     * here, so a link the browser builds and a URL the server resolves cannot drift apart.
     */
    routePath(){
      if(this.page) return PATH_FOR[this.page];
      if(this.view === "releases") return "/releases";
      if(this.view === "release")  return "/releases/" + encodeURIComponent(this.releaseTag);
      if(this.view === "epic"){
        const e = this.epic;
        return e ? "/" + e.slug : "/";
      }
      if(this.view === "story"){
        const e = this.storyEpic, s = this.story;
        return e && s ? "/" + e.slug + "/" + s.slug : "/";
      }
      return "/";
    },

    /** Reads the address bar into state. Runs on first load and on every back/forward, so it never
     *  pushes history of its own. A URL naming something that no longer exists falls back to the
     *  board and rewrites itself, so back doesn't bounce off a dead entry. */
    readUrl(){
      const path = appPath();
      // ?h=<term>, set when you arrive from a search result. Read from the address bar rather than
      // held in state, so back and forward land on the same page in the same condition.
      this.highlight = new URLSearchParams(location.search).get("h") || "";

      const page = PAGES[path];
      if(page){
        // Both edit pages need something already selected. Reached cold — a bookmark, a refresh —
        // there is nothing to edit, so fall back rather than showing an empty form.
        if(page === "edit-epic" && this.editingEpic === null) return this.resetToBoard();
        if(page === "edit-story" && !this.storyCode) return this.resetToBoard();
        // Same reason: reached cold there is no project selected to remove, and a confirm form
        // naming nothing is worse than no form.
        if(page === "remove-project" && !this.removing.backlogPath) return this.resetToBoard();
        return this.openPage(page, false);
      }
      this.page = "";

      if(path === "/" || path === "") return this.show("board", false);
      if(path === "/releases") return this.show("releases", false);

      const parts = path.split("/").filter(Boolean).map(decodeURIComponent);

      if(parts[0] === "releases" && parts.length === 2){
        if(!this.releaseGroups().some(g => g.title === parts[1])) return this.resetToBoard();
        this.releaseTag = parts[1];
        return this.show("release", false);
      }

      // /{epic-slug} and /{epic-slug}/{story-slug} — the breadcrumb, read back.
      if(parts.length === 1 || parts.length === 2){
        const epic = this.epics.find(e => e.slug === parts[0]);
        if(!epic) return this.resetToBoard();

        if(parts.length === 1){
          this.epicNumber = epic.number;
          this.show("epic", false);
          return this.markHighlight();
        }

        const story = epic.stories.find(s => s.slug === parts[1]);
        if(!story) return this.resetToBoard();
        this.storyCode = story.code;
        this.show("story", false);
        // Marked after each load rather than once: the tasks and the description arrive from two
        // requests, and whichever lands second would otherwise come up unmarked.
        this.loadDetail().then(() => this.markHighlight());
        return this.loadSkill().then(() => this.markHighlight());
      }

      return this.resetToBoard();
    },

    /** A URL naming something that is not there — a stale bookmark, a typo, a story since deleted.
     *  Landing silently on the Overview looks like the link worked, so say what happened. */
    resetToBoard(){
      const asked = appPath();
      history.replaceState({}, "", url("/"));
      this.show("board", false);
      if(asked && asked !== "/") this.toast("That page no longer exists — showing the Overview");
    },

    openPage(page, push){
      this.page = page;
      this.err = {};
      this.addOpen = false;
      this.drawerOpen = false;
      if(page === "configure") this.form = { backlogPath: this.config.backlogPath || "",
                                             skillsPath: this.config.skillsPath || "",
                                             projectName: this.projectName };
      // The add form starts empty: it is for a project you do not have yet, so prefilling it with
      // the current one invites overwriting the entry you are looking at.
      //
      // Loaded here rather than in goProjects, so a bookmark or a refresh of /projects shows the
      // list too — routing to the page and arriving at it are different paths through this code.
      if(page === "projects") this.loadProjects();
      if(page === "add-project") this.form = { backlogPath:"", skillsPath:"" };
      if(page === "remove-project") this.form = { confirmName:"" };
      if(page === "add-epic")  this.form = { title:"" };
      // seedEpic is set when Add is reached from inside an epic, so the dropdown already names the
      // epic you were looking at rather than the first one on the board.
      if(page === "add-story") this.form = { epicNumber: String(this.seedEpic != null ? this.seedEpic
                                               : (this.board.epics.length ? this.board.epics[0].number : 0)),
                                             title:"", release:"", description:"" };
      this.seedEpic = null;
      if(page === "edit-epic") this.form = { title: this.epicOf(this.editingEpic) ? this.epicOf(this.editingEpic).title : "" };
      if(page === "edit-story") this.form = { title: this.story ? this.story.title : "",
                                              release: this.story ? this.story.release : "" };
      if(push !== false) this.navigate();
    },

    /** Leaves any form page and shows a board view. */
    show(view, push){
      this.view = view;
      this.page = "";
      this.addOpen = false;
      this.drawerOpen = false;
      this.picker.open = false;
      if(push !== false) this.navigate();
    },

    navigate(){
      const path = this.routePath();
      if(appPath() !== path) history.pushState({}, "", url(path));
    },

    goBoard(push){ this.show("board", push); },
    goReleases(push){ this.show("releases", push); },
    goConfigure(){ this.openPage("configure"); },
    /** The "+" logo slot. On Configure it would otherwise be a button that does nothing, because
     *  it is already showing the page it takes you to — so it puts you in the file picker instead. */
    setLogo(){
      if(this.page === "configure") return this.focusRef("logo");
      this.goConfigure();
    },

    goProjects(){ this.openPage("projects"); },
    goAddProject(){ this.openPage("add-project"); },
    goRemoveProject(p){ this.removing = p; this.openPage("remove-project"); },
    goAddEpic(){ this.openPage("add-epic"); },
    goAddStory(){ this.openPage("add-story"); },
    goAddStoryHere(){ this.seedEpic = this.epicNumber; this.openPage("add-story"); },
    goRenameEpic(){ this.editingEpic = this.epicNumber; this.openPage("edit-epic"); },
    goEditStory(){ if(this.story) this.openPage("edit-story"); },

    /** Cancel returns you to what you were editing, not to the Overview. Dumping someone at the
     *  top of the app because they changed their mind loses their place for no reason. */
    cancelEditEpic(){ this.openEpicByNumber(this.editingEpic); },
    cancelEditStory(){ this.show("story"); },

    openEpic(epic){ this.openEpicByNumber(epic.number); },
    openEpicByNumber(number, push){ this.epicNumber = number; this.show("epic", push); },
    openRelease(section){ this.releaseTag = section.title; this.show("release"); },

    openStory(story){
      this.storyCode = story.code;
      this.show("story");
      this.loadDetail();
      this.loadSkill();
    },

    goCrumb(c){
      if(c.to === "board") this.goBoard();
      else if(c.to === "releases") this.goReleases();
      else if(c.to === "epic") this.openEpicByNumber(c.epicNumber);
    },

    /* ------------------------------------------------------ derived UI -- */
    get epics(){ return this.board.epics; },
    get onFormPage(){ return this.page !== ""; },
    get isListView(){ return this.view === "board" || this.view === "releases" || this.view === "release"; },
    // The summary counts the whole backlog, so it only belongs on the whole backlog. Inside a
    // release the same strip would be counting a subset while looking identical.
    get isBoardView(){ return this.view === "board"; },
    get isEpicView(){ return this.view === "epic"; },
    get isStoryView(){ return this.view === "story"; },
    get bottomBarOwnsNav(){ return this.winWidth <= 768; },
    get railed(){ return this.winWidth > 900 && this.sidebarCollapsed; },

    get sidebarClass(){
      const cls = [];
      if(this.railed) cls.push("collapsed");
      if(this.drawerOpen) cls.push("open");
      return cls.join(" ");
    },
    get scrimClass(){ return this.drawerOpen ? "show" : ""; },
    get drawerLabel(){ return this.drawerOpen ? "Hide navigation" : "Show navigation"; },
    toggleDrawer(){ this.drawerOpen = !this.drawerOpen; },
    closeDrawer(){ this.drawerOpen = false; },
    toggleSidebar(){ this.sidebarCollapsed = !this.sidebarCollapsed; },

    get boardNavClass(){ return this.view === "board" && !this.onFormPage ? "on" : ""; },
    get releasesNavClass(){ return (this.view === "releases" || this.view === "release") && !this.onFormPage ? "on" : ""; },
    mnavClass(which){
      if(this.onFormPage) return this.page === which ? "active" : "";
      if(which === "board") return this.view === "board" ? "active" : "";
      if(which === "releases") return this.view === "releases" || this.view === "release" ? "active" : "";
      return "";
    },

    toggleAdd(){ this.addMobile = false; this.addOpen = !this.addOpen; },
    toggleAddMobile(){ this.addMobile = true; this.addOpen = !this.addOpen; },
    closeAdd(){ this.addOpen = false; },
    get addMenuClass(){ return this.addMobile ? "mobile" : ""; },

    toggleTheme(){
      const cur = document.body.getAttribute("data-theme");
      const dark = matchMedia("(prefers-color-scheme:dark)").matches;
      document.body.setAttribute("data-theme", cur === "dark" ? "light" : cur === "light" ? "dark" : (dark ? "light" : "dark"));
    },

    /* ---------------------------------------------------------- sidebar -- */
    isEpicOpen(epic){ return this.expanded.indexOf(epic.number) !== -1; },
    chevron(epic){ return this.isEpicOpen(epic) ? "▾" : "▸"; },
    toggleEpic(epic){
      const i = this.expanded.indexOf(epic.number);
      if(i === -1) this.expanded.push(epic.number); else this.expanded.splice(i, 1);
    },
    /** An epic is highlighted when you are on it, or when the open story belongs to it. */
    epicIsHighlighted(epic){
      if(this.view === "epic" && this.epicNumber === epic.number) return true;
      return this.view === "story" && epic.stories.some(s => s.code === this.storyCode);
    },
    epicIsActive(epic){ return this.view === "epic" && this.epicNumber === epic.number; },
    epicRowClass(epic){
      const cls = [];
      if(this.epicIsHighlighted(epic)) cls.push("hl");
      if(this.epicIsActive(epic)) cls.push("on");
      return cls.join(" ");
    },
    railEpicClass(epic){ return this.epicRowClass(epic); },
    storyRowClass(story){ return this.view === "story" && this.storyCode === story.code ? "on" : ""; },

    /* ------------------------------------------------- list-view model -- */
    /** Board and release views are the same shape — a header plus story rows — so they share
     *  one template and differ only in what fills this list. */
    get sections(){
      if(this.view === "board"){
        // The current-epic fields are flattened onto the section rather than reached through a
        // nested object: x-show hides the epic header for a release section, but Alpine still
        // evaluates its bindings, and "section.epic.currentLabel" on a release throws.
        return this.epics.map(e => ({
          key: "e" + e.number, kind: "epic", epicNumber: e.number, title: e.title,
          countLabel: e.countLabel, rows: e.stories.map(s => ({ s, ctx: "" })),
          curOn: e.isCurrent, curClass: e.currentClass,
          curLabel: e.currentLabel, curTitle: e.currentTitle,
        }));
      }
      const groups = this.releaseGroups();
      if(this.view === "release"){
        const one = groups.filter(g => g.title === this.releaseTag);
        return one.map(g => Object.assign({}, g, { clickable:false }));
      }
      return groups;
    },

    /** Groups stories by release tag, ordered by the roadmap; unscheduled last. */
    releaseGroups(){
      const order = this.board.roadmap;
      const map = new Map();
      this.epics.forEach(e => e.stories.forEach(s => {
        const key = s.release || "Unscheduled";
        if(!map.has(key)) map.set(key, []);
        map.get(key).push({ s, ctx: e.title });
      }));
      const keys = Array.from(map.keys()).sort((a, b) => {
        if(a === "Unscheduled") return 1;
        if(b === "Unscheduled") return -1;
        const ia = order.indexOf(a), ib = order.indexOf(b);
        return (ia < 0 ? 999 : ia) - (ib < 0 ? 999 : ib);
      });
      // curOn/curClass/curLabel/curTitle so a release section has the same shape as an epic one:
      // its epic header is hidden, but Alpine evaluates the bindings either way.
      return keys.map(tag => ({
        key: "r" + tag, kind: "release", title: tag, clickable: true, epicNumber: -1,
        countLabel: plural(map.get(tag).length, "story", "stories"), rows: map.get(tag),
        curOn: false, curClass: "", curLabel: "", curTitle: "",
      }));
    },

    get stackClass(){ return this.view === "release" ? "tight" : ""; },
    get emptyListMessage(){ return this.view === "board" ? "No epics yet." : "No releases found."; },

    get crumbs(){
      if(this.view === "epic" && this.epic)
        return [{ label:"Overview", link:true, to:"board", first:true },
                { label: this.epic.number + " — " + this.epic.title, link:false }];
      if(this.view === "story" && this.story && this.storyEpic)
        return [{ label:"Overview", link:true, to:"board", first:true },
                { label: this.storyEpic.number + " — " + this.storyEpic.title, link:true, to:"epic", epicNumber: this.storyEpic.number },
                { label: this.story.title, link:false }];
      if(this.view === "release")
        return [{ label:"Overview", link:true, to:"board", first:true },
                { label:"By release", link:true, to:"releases" },
                { label: this.releaseTag, link:false }];
      return [];
    },
    get crumbClass(){ return this.view === "story" ? "flush" : ""; },

    /* -------------------------------------------------------- epic view -- */
    epicOf(number){ return this.epics.find(e => e.number === number) || null; },
    get epic(){ return this.epicOf(this.epicNumber); },
    get epicStories(){ return this.epic ? this.epic.stories : []; },
    get epicHeading(){ return this.epic ? this.epic.number + " — " + this.epic.title : ""; },
    get epicCountLabel(){ return this.epic ? this.epic.countLabel : ""; },
    get epicIsCurrent(){ return !!this.epic && this.epic.isCurrent; },
    get epicCurrentLabel(){ return this.epic ? this.epic.currentLabel : ""; },
    get epicCurrentClass(){ return this.epic ? this.epic.currentClass : ""; },
    get epicCurrentTitle(){ return this.epic ? this.epic.currentTitle : ""; },
    makeCurrentEpic(){ if(this.epic) return this.makeCurrent(this.epic); },
    get epicSectionClass(){ return ""; },
    get editingEpicLabel(){ return "Epic " + this.editingEpic; },

    /* ------------------------------------------------------ story view -- */
    storyByCode(code){
      // Every field on the detail page reads through this, so it stops at the first match rather
      // than walking the whole board each time.
      for(const e of this.epics){
        const s = e.stories.find(x => x.code === code);
        if(s) return s;
      }
      return null;
    },
    get story(){ return this.storyByCode(this.storyCode); },
    get storyEpic(){ return this.epics.find(e => e.stories.some(s => s.code === this.storyCode)) || null; },
    get storyTitle(){ return this.story ? this.story.title : ""; },
    get storyRelease(){ return this.story ? this.story.release : ""; },
    get storyStatusClass(){ return this.story ? this.story.statusClass : ""; },
    get storyStatusLabel(){ return this.story ? this.story.status : ""; },
    get storyEmoji(){ return this.story ? this.story.emoji : ""; },

    // Tasks and test cases come from the story's folder, not the board.
    get storyTasks(){ return this.detail.tasks; },
    get storyTestCases(){ return this.detail.testCases; },
    get doneCount(){ return this.detail.tasks.filter(t => t.done).length; },
    get tcPass(){ return this.detail.testCases.filter(t => t.status === "Passed").length; },
    get tcFail(){ return this.detail.testCases.filter(t => t.status === "Failed").length; },
    get detailTaskLabel(){ return this.doneCount + "/" + this.detail.tasks.length; },
    get detailTaskBar(){ return width(pct(this.doneCount, this.detail.tasks.length)); },
    get detailTcLabel(){ return this.tcPass + "/" + this.detail.testCases.length; },
    get detailTcFail(){ return this.tcFail; },
    get detailTcFailLabel(){ return " · " + this.tcFail + " ✗"; },
    get detailTcPassBar(){ return width(pct(this.tcPass, this.detail.testCases.length)); },
    get detailTcFailBar(){ return width(pct(this.tcFail, this.detail.testCases.length)); },
    get detailTasksHeading(){ return "TASKS · " + this.detailTaskLabel; },
    get detailTcHeading(){ return "TEST CASES · " + this.detailTcLabel; },

    /* ------------------------------------------------------ reordering -- */
    //
    // Order is meaning here: tasks are a sequence of work and test cases are a sequence of checks,
    // so moving one used to mean deleting it and typing it again. Nothing needed adding to the API
    // for this — PUT .../tasks already replaces the whole list, so a move is just a reordered array
    // and one write, which is exactly why that endpoint takes the list rather than an item.

    gripLabel(item){ return "Reorder “" + item.text + "” — drag, or use the arrow keys"; },

    /**
     * The single move. Drag and keyboard both end up here, so they cannot disagree.
     *
     * The list is reordered on screen first and saved after. Not for the animation: waiting for the
     * network leaves a window where the rows have not moved yet but the keyboard has, so holding
     * the arrow key moves the row that has since taken the place of the one you were moving. Do it
     * locally and the next key press reads a list that is already correct.
     */
    moveItem(kind, from, to){
      const list = (kind === "tasks" ? this.detail.tasks : this.detail.testCases).slice();
      if(from === to || from < 0 || to < 0 || from >= list.length || to >= list.length) return null;

      const [item] = list.splice(from, 1);
      list.splice(to, 0, item);

      if(kind === "tasks"){
        this.detail.tasks = list;
        return this.saveTasks(list, "Task moved");
      }
      this.detail.testCases = list;
      return this.saveTestCases(list, "Test case moved");
    },

    /** Keyboard moves keep focus on the row that moved, so a second press moves it again rather
     *  than moving whatever has taken its place. */
    moveBy(kind, i, delta){
      const to = i + delta;
      const saving = this.moveItem(kind, i, to);
      if(!saving) return;                                  // at the end of the list; nothing moved

      this.$nextTick(() => {
        const row = document.querySelector('[data-list="' + kind + '"] .lrow[data-i="' + to + '"] .grip');
        if(row) row.focus();
      });
      return saving;
    },

    moveTaskUp(i){ return this.moveBy("tasks", i, -1); },
    moveTaskDown(i){ return this.moveBy("tasks", i, 1); },
    moveTestUp(i){ return this.moveBy("testCases", i, -1); },
    moveTestDown(i){ return this.moveBy("testCases", i, 1); },

    /**
     * Pointer drag, from a delegated listener rather than per-row attributes — the rows are inside
     * x-for, and this keeps the markup to a handle with an accessible name.
     *
     * The DOM is deliberately not reordered while dragging: Alpine owns those nodes, and moving
     * them under it is how you get a list that disagrees with its own data. A line shows where the
     * item will land, and the array is reordered once, on release.
     */
    beginDrag(grip, ev){
      const row = grip.closest(".lrow");
      const list = grip.closest("[data-list]");
      if(!row || !list) return;

      ev.preventDefault();                       // no text selection, no scroll-on-touch
      const kind = list.dataset.list;
      const from = Number(row.dataset.i);
      const rows = Array.from(list.querySelectorAll(".lrow[data-i]"));
      let to = from;

      row.classList.add("dragging");

      const over = e => {
        to = dropIndex(rows, e.clientY, from);
        rows.forEach((r, i) => r.classList.toggle("drop-here", i === to && to !== from));
      };
      const done = () => {
        document.removeEventListener("pointermove", over);
        document.removeEventListener("pointerup", done);
        document.removeEventListener("pointercancel", done);
        rows.forEach(r => r.classList.remove("dragging", "drop-here"));
        this.moveItem(kind, from, to);       // reordered on screen at once, saved right after
      };

      document.addEventListener("pointermove", over);
      document.addEventListener("pointerup", done);
      document.addEventListener("pointercancel", done);
    },

    boxClass(task){ return task.done ? "on" : ""; },
    taskMark(task){ return task.done ? "✓" : ""; },
    taskTextClass(task){ return task.done ? "done" : ""; },

    async loadDetail(){
      this.detail = { tasks: [], testCases: [], loading: true, error: "" };
      const code = this.storyCode;
      try{
        const d = await api("/api/story/" + encodeURIComponent(code));
        if(this.storyCode !== code) return;   // another story was opened while this was in flight
        this.detail = decorateDetail(d);
      }catch(e){
        if(this.storyCode !== code) return;
        this.detail = { tasks: [], testCases: [], loading: false,
                        error: e.message || "This story's files could not be read." };
      }
    },

    /* ----------------------------------------------------------- writes -- */
    async write(path, body, message){
      try{
        this.board = decorate(await api(path, "POST", body));
        this.toast(message);
      }catch(e){ this.toast(e.message || "That change could not be saved"); }
    },

    /** Marks which epic you are working in. Already current: nothing to do, so nothing happens —
     *  a toast saying "no change" is noise for a button that looks like a label. */
    async makeCurrent(epic){
      if(!epic) return;
      if(epic.isCurrent && this.board.currentEpic === epic.number) return;
      await this.write("/api/epic/" + epic.number + "/current", null, "Current epic: " + epic.title);
    },

    makeCurrentByNumber(number){
      return this.makeCurrent(this.epics.find(e => e.number === number));
    },

    /* ----------------------------------------- tasks and test cases ------ */
    /** One call covers add, edit, delete, reorder and toggle: the client sends the list it wants
     *  the file to hold, so there is no per-item addressing to drift out of step. */
    /**
     * A reply is only allowed to paint the story it was asked about, and only if it is the newest
     * question asked.
     *
     * Both halves were bugs. Without the code check, ticking a task and opening another story
     * before the answer came back drew the first story's tasks and test cases onto the second —
     * which looks like every task suddenly finished and every test suddenly passed. loadDetail has
     * guarded against that since it was written; the writes never did.
     *
     * Without the sequence check, two writes in flight — an arrow key held down, two boxes ticked
     * quickly — can answer out of order, and the older answer puts the older list back.
     */
    applies(seq, code){ return seq === this.writeSeq && this.storyCode === code; },

    async saveTasks(tasks, message){
      const seq = ++this.writeSeq, code = this.storyCode;
      try{
        const d = await api("/api/story/" + encodeURIComponent(code) + "/tasks", "PUT",
                            { tasks: tasks.map(t => ({ text: t.text, done: t.done })) });
        if(!this.applies(seq, code)) return;
        this.detail = decorateDetail(d);
        this.toast(message);
      }catch(e){ this.failedWrite(e, code); }
    },

    async saveTestCases(cases, message){
      const seq = ++this.writeSeq, code = this.storyCode;
      try{
        const d = await api("/api/story/" + encodeURIComponent(code) + "/test-cases", "PUT",
                            { testCases: cases.map(c => ({ text: c.text, status: c.status })) });
        if(!this.applies(seq, code)) return;
        this.detail = decorateDetail(d);
        this.toast(message);
      }catch(e){ this.failedWrite(e, code); }
    },

    /** Says what went wrong and re-reads the folder. The list on screen has already been changed
     *  optimistically, and leaving it showing a change that was refused is the worse lie. Silent
     *  if you have moved on: the story it failed for is not the story you are looking at. */
    failedWrite(e, code){
      if(code !== undefined && code !== this.storyCode) return;
      this.toast(e.message || "That change could not be saved.");
      this.loadDetail();
    },

    async toggleTask(i){
      const tasks = this.detail.tasks.map(t => Object.assign({}, t));
      tasks[i].done = !tasks[i].done;
      await this.saveTasks(tasks, "Saved");
    },

    /** Long text would push the buttons off a phone screen, so the prompt quotes just enough of it
     *  to identify which row you are about to lose. */
    shorten(text){
      const t = (text || "").trim();
      return t.length > 90 ? t.slice(0, 90).trimEnd() + "…" : t;
    },

    async removeTask(i){
      const task = this.detail.tasks[i];
      if(!task) return;

      const ok = await this.ask("Delete this task?",
        "“" + this.shorten(task.text) + "” will be removed from tasks.yaml.", "Delete task");
      if(!ok) return;

      await this.saveTasks(this.detail.tasks.filter((_, j) => j !== i), "Task deleted");
    },

    async removeTestCase(i){
      const tc = this.detail.testCases[i];
      if(!tc) return;

      const ok = await this.ask("Delete this test case?",
        "“" + this.shorten(tc.text) + "” will be removed from test-cases.yaml.", "Delete test case");
      if(!ok) return;

      await this.saveTestCases(this.detail.testCases.filter((_, j) => j !== i), "Test case deleted");
    },

    /* --------------------------------------------- inline add and edit --- */
    // A task is one field with no URL of its own, so it is edited in place. Multi-field creates —
    // an epic, a story — still get a real page.
    startAddTask(){ this.cancelEdit(); this.addingTask = true; this.focusRef("newTask"); },
    startAddTest(){ this.cancelEdit(); this.addingTest = true; this.focusRef("newTest"); },
    cancelEdit(){ this.addingTask = false; this.addingTest = false; this.editingTask = -1; this.draftText = ""; },

    focusRef(name){
      // Two hops, not one. $nextTick lands after Alpine has updated the data, but an element that
      // x-show is revealing in the same tick can still be display:none when the callback runs — and
      // focus() on a hidden element does nothing and reports nothing. The frame is what makes the
      // difference between the search box being ready to type into and looking ready.
      this.$nextTick(() => requestAnimationFrame(() => {
        const el = this.$refs[name];
        if(el) el.focus();
      }));
    },

    startEditTask(i){
      this.cancelEdit();
      this.editingTask = i;
      this.draftText = this.detail.tasks[i].text;
      this.focusRef("editTask");
    },

    async commitEditTask(){
      const i = this.editingTask;
      if(i < 0) return;
      const text = this.draftText.trim();
      this.editingTask = -1;
      this.draftText = "";
      if(!text || text === this.detail.tasks[i].text) return;

      const tasks = this.detail.tasks.map(t => Object.assign({}, t));
      tasks[i].text = text;
      await this.saveTasks(tasks, "Task updated");
    },

    async commitAddTask(){
      const text = this.draftText.trim();
      if(!text) return this.cancelEdit();
      this.draftText = "";
      const tasks = this.detail.tasks.map(t => Object.assign({}, t));
      tasks.push({ text, done: false });
      await this.saveTasks(tasks, "Task added");
      this.focusRef("newTask");            // stay open so several can be typed in a row
    },

    async commitAddTest(){
      const text = this.draftText.trim();
      if(!text) return this.cancelEdit();
      this.draftText = "";
      const cases = this.detail.testCases.map(c => Object.assign({}, c));
      cases.push({ text, status: "Not Run" });
      await this.saveTestCases(cases, "Test case added");
      this.focusRef("newTest");
    },

    /* --------------------------------------------------- status picker -- */
    openStatusPicker(story, el){ this.openPicker(el, STATUSES, story.status, "story", story, null); },
    openTestPicker(i, el){ this.openPicker(el, TC_STATUSES, this.detail.testCases[i].status, "tc", null, i); },

    openPicker(el, options, current, kind, story, tc){
      const r = el.getBoundingClientRect();
      const w = options === TC_STATUSES ? 150 : 224;
      const max = window.scrollX + document.documentElement.clientWidth - w - 12;
      const left = Math.max(window.scrollX + 8, Math.min(r.left + window.scrollX, max));
      this.picker = {
        open: true, options, current, kind, story, tc,
        narrow: options === TC_STATUSES,
        cls: options === TC_STATUSES ? "narrow" : "",
        pos: "left:" + left + "px;top:" + (r.bottom + window.scrollY + 6) + "px",
      };
    },
    closePicker(){ this.picker.open = false; },
    isCurrentOption(option){ return option.label === this.picker.current; },
    optionClass(option){ return this.isCurrentOption(option) ? "on" : ""; },
    optionSquareClass(option){ return option.cls + (this.picker.narrow ? " md" : " lg"); },

    /**
     * What is still outstanding in the story on screen, as a sentence — or "" when nothing is.
     *
     * Only answerable on the story page: the board holds the index alone, and counting tasks there
     * would mean opening every story folder to draw a list that does not show them.
     */
    get outstandingWork(){
      const tasks = this.detail.tasks.filter(t => !t.done).length;
      const tests = this.detail.testCases.filter(t => t.status !== "Passed").length;

      const parts = [];
      if(tasks) parts.push(plural(tasks, "task is not ticked", "tasks are not ticked"));
      if(tests) parts.push(plural(tests, "test case has not passed", "test cases have not passed"));
      return parts.join(", and ");
    },

    async choose(option){
      const p = this.picker;
      this.picker.open = false;

      if(p.kind === "story"){
        // Marking a story Done over unfinished work is usually a slip, and occasionally a
        // decision. So it asks rather than refuses — and only here, where the story's folder is
        // loaded and the answer is actually known.
        if(option.label === "Done" && this.isStoryView && this.storyCode === p.story.code){
          const outstanding = this.outstandingWork;
          if(outstanding){
            const ok = await this.ask(
              "Mark " + p.story.code + " as Done?",
              outstanding.charAt(0).toUpperCase() + outstanding.slice(1)
                + ". Marking the story Done does not change them.",
              "Mark Done", "primary");
            if(!ok) return;
          }
        }
        this.write("/api/story/" + p.story.code + "/status", { status: option.label }, "Saved");
        return;
      }
      const cases = this.detail.testCases.map(c => Object.assign({}, c));
      cases[p.tc].status = option.label;
      this.saveTestCases(cases, "Saved");
    },

    /* ------------------------------------------------------ description -- */
    get skillReading(){ return !!this.skill.path && !this.skill.editing && !this.skill.loading && !this.skill.error; },

    async loadSkill(){
      const story = this.story;
      // The description is always <folder>/SKILL.md — created with the story, so there is no
      // "this story has no description" state left to handle.
      const path = story ? story.folder + "/SKILL.md" : null;
      this.skill = { path, original:"", draft:"", html:"", editing:false, loading:false, saving:false, error:"" };
      if(!path) return;

      this.skill.loading = true;
      try{
        const res = await fetch(url("/api/skill?path=" + encodeURIComponent(path)));
        const text = await res.text();
        if(!res.ok) throw new Error(text || "This description could not be opened.");
        if(this.skill.path !== path) return;      // a different story was opened meanwhile
        this.skill.original = text;
        // Trimmed for display only — see forDisplay(). The editor below shows the file whole,
        // because the file has to stand on its own on disk.
        this.skill.html = renderMarkdown(forDisplay(text));
      }catch(e){
        this.skill.error = e.message || "This description could not be opened.";
      }finally{
        this.skill.loading = false;
      }
    },

    editDescription(){
      this.skill.draft = this.skill.original;
      this.skill.editing = true;
    },

    cancelDescription(){ this.skill.editing = false; this.skill.draft = ""; },

    async saveDescription(){
      this.skill.saving = true;
      // The file being written, remembered — the same reason the task and test-case writes hold on
      // to the story code. Opening another story mid-save must not paint this one's prose onto it.
      const path = this.skill.path, draft = this.skill.draft;
      try{
        const res = await fetch(url("/api/skill"), {
          method: "POST",
          headers: { "Content-Type":"application/json" },
          body: JSON.stringify({ path, content: draft }),
        });
        if(!res.ok) throw new Error((await res.text()) || "The file could not be saved.");
        if(this.skill.path !== path) return;
        this.skill.original = this.skill.draft;
        // Through forDisplay() like the load path. Rendering the draft raw meant saving a
        // description brought its frontmatter and title back until the next reload.
        this.skill.html = renderMarkdown(forDisplay(draft));
        this.skill.editing = false;
        this.toast("Description saved");
      }catch(e){ this.toast(e.message || "The file could not be saved."); }
      finally{ this.skill.saving = false; }
    },

    /* --------------------------------------------------------- deleting -- */
    /** Resolves true only when the delete button closed the dialog — Escape and Cancel mean no. */
    /** @param tone "primary" for a question that destroys nothing — a red button on "Mark Done"
     *  reads as a warning about the click rather than about the story. */
    ask(title, body, okLabel, tone){
      this.confirm = { title, body, okLabel,
                       okClass: tone === "primary" ? "btn primary" : "btn danger" };
      const dlg = this.$refs.confirm;
      return new Promise(resolve => {
        dlg.addEventListener("close", () => resolve(dlg.returnValue === "delete"), { once:true });
        dlg.showModal();
      });
    },

    async askDeleteStory(){
      const s = this.story;
      if(!s) return;
      const ok = await this.ask("Delete " + s.code + "?",
        '"' + s.title + '" and its tasks and test cases are removed from BACKLOG.yaml. Its folder is deleted too.',
        "Delete story");
      if(!ok) return;
      try{
        this.board = decorate(await api("/api/story/" + encodeURIComponent(s.code), "DELETE"));
        this.goBoard();
        this.toast("Story deleted");
      }catch(e){ this.toast(e.message || "The story could not be deleted."); }
    },

    async askDeleteEpic(){
      const e = this.epic;
      if(!e) return;
      const n = e.stories.length;
      const ok = await this.ask("Delete Epic " + e.number + "?",
        n ? '"' + e.title + '" and its ' + plural(n, "story is", "stories are")
            + " removed from BACKLOG.yaml, along with " + (n === 1 ? "its folder" : "their folders")
            + ". This cannot be undone from here."
          : '"' + e.title + '" is removed from BACKLOG.yaml. It has no stories.',
        n ? "Delete epic and " + plural(n, "story", "stories") : "Delete epic");
      if(!ok) return;
      try{
        this.board = decorate(await api("/api/epic/" + e.number, "DELETE"));
        this.goBoard();
        this.toast("Epic deleted");
      }catch(err){ this.toast(err.message || "The epic could not be deleted."); }
    },

    /* ------------------------------------------------------------ forms -- */
    /** Identifiers are assigned by the app, never typed — the same reasoning as a database key. */
    get nextEpicNumber(){
      const used = this.epics.map(e => e.number);
      return used.length ? Math.max(...used) + 1 : 0;
    },
    get nextEpicLabel(){ return "Epic " + this.nextEpicNumber; },
    get nextStoryCode(){
      const nums = [];
      this.epics.forEach(e => e.stories.forEach(s => nums.push(Number(s.code.replace("US-", "")))));
      const next = nums.length ? Math.max(...nums) + 1 : 1;
      return "US-" + String(next).padStart(2, "0");
    },

    /** Client-side checks are a courtesy that saves a round trip. Every one of them is enforced
     *  again on the server, which is the check that actually counts. */
    require(field, message){
      const value = (this.form[field] || "").trim();
      if(!value) this.err[field] = message;
      return value;
    },

    async submitEpic(){
      this.err = {};
      const title = this.require("title", "Give the epic a title.");
      if(!title) return;
      this.saving = true;
      try{
        const number = this.nextEpicNumber;
        this.board = decorate(await api("/api/epic", "POST", { number, title }));
        // Open the epic just created, the same way adding a story opens the story. Returning to the
        // Overview meant the two add flows ended somewhere different for no reason, and left you to
        // find the thing you had just made.
        this.openEpicByNumber(number);
        this.toast("Epic added");
      }catch(e){ this.err.form = e.message || "The epic could not be added."; }
      finally{ this.saving = false; }
    },

    async submitRename(){
      this.err = {};
      const title = this.require("title", "Give the epic a title.");
      if(!title) return;
      this.saving = true;
      try{
        this.board = decorate(await api("/api/epic/" + this.editingEpic, "POST", { title }));
        this.openEpicByNumber(this.editingEpic);
        this.toast("Epic renamed");
      }catch(e){ this.err.form = e.message || "The epic could not be renamed."; }
      finally{ this.saving = false; }
    },

    async submitEditStory(){
      this.err = {};
      const title = this.require("title", "Give the story a title.");
      if(!title) return;

      const code = this.storyCode;
      this.saving = true;
      try{
        this.board = decorate(await api("/api/story/" + encodeURIComponent(code), "POST", {
          title, release: (this.form.release || "").trim(),
        }));
        // The slug follows the title, so the URL this story lives at has just changed. Re-open it
        // by code and let routePath() write the new address.
        this.storyCode = code;
        this.show("story");
        this.toast("Story updated");
      }catch(e){ this.err.form = e.message || "The story could not be updated."; }
      finally{ this.saving = false; }
    },

    async submitStory(){
      this.err = {};
      const title = this.require("title", "Give the story a title.");
      if(this.form.epicNumber === "" || this.form.epicNumber == null) this.err.epicNumber = "Choose which epic this story belongs to.";
      if(!title || this.err.epicNumber) return;
      this.saving = true;
      try{
        this.board = decorate(await api("/api/story", "POST", {
          epicNumber: Number(this.form.epicNumber),
          code: this.nextStoryCode,
          title,
          release: (this.form.release || "").trim(),
          description: (this.form.description || "").trim(),
        }));
        // Open the story that was just created rather than dropping back to the Overview — you
        // almost always want to keep working on the thing you just made.
        this.storyCode = this.board.epics
          .flatMap(e => e.stories).map(s => s.code)
          .sort((a, b) => Number(b.replace(/\D/g, "")) - Number(a.replace(/\D/g, "")))[0];
        this.show("story");
        this.loadDetail();
        this.loadSkill();
        this.toast("User story added");
      }catch(e){ this.err.form = e.message || "The story could not be added."; }
      finally{ this.saving = false; }
    },

    async saveConfig(){
      this.err = {};
      const backlog = (this.form.backlogPath || "").trim();
      // Must match AppConfigService.ValidateBacklogPath. This check only saves a round trip —
      // the server enforces the same rule — but when the two disagree the form blocks a path the
      // server would have accepted, which is worse than having no check at all.
      if(backlog && !/\.ya?ml$/i.test(backlog)){
        this.err.backlogPath = "Point this at a .yaml file, for example C:/projects/my-app/BACKLOG.yaml.";
        return;
      }
      const skills = (this.form.skillsPath || "").trim();
      const file = this.$refs.logo.files[0];

      this.saving = true;
      try{
        if(backlog) this.config = await api("/api/config/backlog", "POST", { path: backlog });
        if(skills)  this.config = await api("/api/config/skills",  "POST", { path: skills });
        // After the paths, so a rename lands on the backlog you just pointed at rather than the
        // one you were leaving.
        const name = (this.form.projectName || "").trim();
        if(name && name !== this.projectName) this.config = await api("/api/config/name", "POST", { name });
        if(file){
          const fd = new FormData();
          fd.append("logo", file);
          const res = await fetch(url("/api/config/logo"), { method:"POST", body: fd });
          if(!res.ok) throw new Error((await res.text()) || "The logo could not be saved.");
          this.config = await res.json();
          this.logoStamp = Date.now();
        }
        await this.load();
        this.goBoard();
        this.toast("Changes saved");
      }catch(e){ this.err.form = e.message || "Those settings could not be saved."; }
      finally{ this.saving = false; }
    },

    /* ------------------------------------------------------------- logo -- */
    /** The board's own `project:` value. Falls back to the product name so the header is never
     *  empty — on first load the board has not arrived yet. */
    /**
     * Counts per status, computed live from the index already in memory.
     *
     * Never stored — a total kept beside the thing it counts is a second copy free to disagree,
     * which is exactly why the STATUS-SUMMARY block was deleted. No story folder is opened, so the
     * board still draws without touching one.
     *
     * Statuses with no stories are left out: seven chips of which four read zero is noise, not
     * information.
     */
    get statusSummary(){
      const stories = this.epics.flatMap(e => e.stories);
      return STATUSES
        .map(s => ({
          label: s.label,
          emoji: s.emoji,
          cls: s.cls,
          n: stories.filter(x => x.status === s.label).length,
        }))
        .filter(s => s.n > 0);
    },

    get summaryTotal(){
      const stories = this.epics.flatMap(e => e.stories).length;
      return plural(stories, "story", "stories") + " in " + plural(this.epics.length, "epic", "epics");
    },

    get projectName(){ return (this.board && this.board.project) || "AI Friendly Task Manager"; },

    /** The Overview, as a real href — so ctrl-click and middle-click work, under a base or not. */
    get homeUrl(){ return url("/"); },

    get hasLogo(){ return !!this.config.logoPath; },
    // The filename is stable, so without a cache-buster the browser keeps showing the old image.
    get logoSrc(){ return url(this.config.logoPath) + "?v=" + this.logoStamp; },
    get logoClass(){ return this.hasLogo ? "has-logo" : ""; },
    get logoLabel(){ return this.hasLogo ? "Overview" : "Set a logo"; },
    get logoHint(){
      return this.hasLogo
        ? "Choose a file to replace it. PNG, JPG, SVG or WebP, up to 2 MB."
        : "PNG, JPG, SVG or WebP, up to 2 MB. Shown in the top-left corner.";
    },
    // With a logo set the slot behaves like any site logo and goes home; while it is still the
    // empty "+" placeholder its only useful job is to take you somewhere you can set one.
    /**
     * The logo is a real <a href="/">, so the browser handles ctrl-click, middle-click and
     * "open in new tab" natively. A plain left click is taken over here instead, because the
     * board is already loaded and swapping the view beats a full round trip.
     */
    logoNav(e){
      if(!e || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button) return;
      e.preventDefault();
      this.goBoard();
    },

    async removeLogo(){
      try{
        this.config = await api("/api/config/logo", "DELETE");
        this.toast("Logo removed");
      }catch(e){ this.err.form = e.message || "The logo could not be removed."; }
    },

    /* ------------------------------------------------------------ toast -- */
    get toastClass(){ return this.toastOn ? "show" : ""; },
    toast(text){
      this.toastText = text;
      this.toastOn = true;
      clearTimeout(this.toastTimer);
      this.toastTimer = setTimeout(() => { this.toastOn = false; }, 1600);
    },
  }));
});
