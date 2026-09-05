/**
 * The demo's server, in the browser.
 *
 * Everything else on this page is the real application, byte for byte: the same index.html, app.js
 * and stylesheet that `dotnet run` serves. This file is the only addition, and all it does is
 * answer `fetch` from the data baked in beside it instead of from a C# process. Nothing in app.js
 * knows it exists.
 *
 * That is the whole trick, and it is why the demo cannot rot: there is no second copy of the UI to
 * keep in step, and the data was produced by the same BacklogService and SearchIndex the real app
 * runs on.
 *
 * Writes are kept in memory. Clicking a status, ticking a task, editing a description — all work,
 * all last as long as the tab does, and all vanish on reload. A demo nobody can touch teaches
 * nothing; a demo that pretends to save would be a lie.
 *
 * Not shipped by the server. It sits in wwwroot so it lives beside the app it mirrors and is copied
 * with it, but only the export writes it into a site — nothing in index.html asks for it until the
 * export puts the tag there.
 */
(function(){
  "use strict";

  const data = window.AIFTM_DEMO;
  if(!data) return;                       // served by the real app: leave fetch alone

  const BASE = (data.basePath || "/").replace(/\/+$/, "");

  const clone = v => (window.structuredClone ? window.structuredClone(v) : JSON.parse(JSON.stringify(v)));

  // The session's copy. Mutated by writes, thrown away on reload — there is nowhere else for it to
  // go, and localStorage would turn "nothing is saved" into "saved until you wonder why".
  const state = {
    board: clone(data.board),
    stories: clone(data.stories),
    skills: clone(data.skills),
    search: clone(data.search),
  };

  const json = body => new Response(JSON.stringify(body), {
    status: 200, headers: { "Content-Type": "application/json" },
  });
  const text = body => new Response(body, { status: 200, headers: { "Content-Type": "text/plain" } });
  const refuse = why => new Response(why, { status: 400, headers: { "Content-Type": "text/plain" } });

  const storyOf = code => state.board.epics
    .flatMap(e => e.stories)
    .find(s => s.code === code);

  /** Keeps the search index honest after a write, the way the real one is rebuilt on every open. */
  function reindex(code){
    const entry = state.search.find(e => e.kind === "story" && e.code === code);
    const story = storyOf(code);
    if(!entry || !story) return;

    entry.status = story.status;
    entry.title = story.title;

    const detail = state.stories[code] || { tasks: [], testCases: [] };
    const prose = entry.sections.filter(s => s.kind === "description");
    entry.sections = prose
      .concat(detail.tasks.map(t => ({ kind: "task", text: t.text })))
      .concat(detail.testCases.map(t => ({ kind: "test case", text: t.text })));
  }

  const routes = [
    ["GET", /^\/api\/board$/, () => json(state.board)],
    ["GET", /^\/api\/validate$/, () => json(data.validate)],
    ["GET", /^\/api\/search-index$/, () => json(state.search)],

    ["GET", /^\/api\/config$/, () => json({
      backlogPath: "BACKLOG.yaml", skillsPath: "skills", logoPath: null, isDemo: true,
    })],

    // One project, and it cannot be changed: there is no filesystem here to point anything at.
    ["GET", /^\/api\/projects$/, () => json({
      projects: [{ backlogPath: "BACKLOG.yaml", skillsPath: "skills",
                   name: state.board.project, isCurrent: true, missing: false }],
      pinned: true,
    })],

    ["GET", /^\/api\/story\/([^/]+)$/, m => {
      const detail = state.stories[decodeURIComponent(m[1])];
      return detail ? json(detail) : refuse("This story's files could not be read.");
    }],

    // Held in memory like every other write here. Left out, this one control would have said
    // "not in the demo" while the status beside it worked — which reads as broken, not as a limit.
    ["POST", /^\/api\/epic\/(\d+)\/current$/, m => {
      const number = Number(m[1]);
      if(!state.board.epics.some(e => e.number === number)) return refuse("There is no epic " + number + ".");
      state.board.currentEpic = number;
      return json(state.board);
    }],

    ["POST", /^\/api\/story\/([^/]+)\/status$/, (m, body) => {
      const code = decodeURIComponent(m[1]);
      const story = storyOf(code);
      if(!story) return refuse("No such story.");
      story.status = body.status;
      reindex(code);
      return json(state.board);
    }],

    ["PUT", /^\/api\/story\/([^/]+)\/tasks$/, (m, body) => {
      const code = decodeURIComponent(m[1]);
      if(!state.stories[code]) return refuse("No such story.");
      state.stories[code].tasks = body.tasks || [];
      reindex(code);
      return json(state.stories[code]);
    }],

    ["PUT", /^\/api\/story\/([^/]+)\/test-cases$/, (m, body) => {
      const code = decodeURIComponent(m[1]);
      if(!state.stories[code]) return refuse("No such story.");
      state.stories[code].testCases = body.testCases || [];
      reindex(code);
      return json(state.stories[code]);
    }],

    ["GET", /^\/api\/skill$/, (m, body, query) => {
      const found = state.skills[query.get("path") || ""];
      return found === undefined ? new Response("SKILL.md not found.", { status: 404 }) : text(found);
    }],

    ["POST", /^\/api\/skill$/, (m, body) => {
      if(!(body.path in state.skills)) return refuse("That SKILL.md does not exist.");
      state.skills[body.path] = body.content || "";
      return json({ saved: true });
    }],
  ];

  // Everything the demo has no answer for — adding an epic, staging in git, uploading a logo — says
  // so in the app's own toast rather than failing as a network error with no explanation.
  const NOT_HERE = "Not in the demo — this writes to files, and there are none here. "
                 + "Run AI Friendly Task Manager locally to try it.";

  const realFetch = window.fetch.bind(window);

  window.fetch = function(input, init){
    const raw = typeof input === "string" ? input : input.url;
    const method = ((init && init.method) || (typeof input === "object" && input.method) || "GET").toUpperCase();

    let path, query;
    try{
      const parsed = new URL(raw, location.href);
      path = parsed.pathname.startsWith(BASE) ? parsed.pathname.slice(BASE.length) : parsed.pathname;
      query = parsed.searchParams;
    }catch(e){ return realFetch(input, init); }

    if(!path.startsWith("/api/")) return realFetch(input, init);

    const body = init && typeof init.body === "string" ? JSON.parse(init.body) : {};

    for(const [verb, pattern, handler] of routes){
      const m = path.match(pattern);
      if(m && verb === method) return Promise.resolve(handler(m, body, query));
    }

    return Promise.resolve(refuse(NOT_HERE));
  };
})();
