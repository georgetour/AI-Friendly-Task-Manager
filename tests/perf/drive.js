// Drives a realistic mix of what someone actually does in AI Friendly Task Manager, and reports latency
// percentiles per action.
//
//   node tests/perf/drive.js [baseUrl] [seconds]
//
// The mix is weighted the way a session tends to go: mostly reading, some ticking, occasional
// writing. Navigation between the Overview, an epic and By release is deliberately absent — those
// are client-side view swaps that issue no request at all, so counting them would flatter the
// numbers rather than measure anything.
//
// It runs flat out with no think time. A person pauses between clicks; this doesn't. Treat the
// numbers as an upper bound on what a real session would feel.
const BASE = process.argv[2] || 'http://localhost:5249';
const SECONDS = Number(process.argv[3] || 300);

const MIX = [
  ['open story',        45],   // GET /api/story/{code}
  ['tick task',         20],   // PUT tasks
  ['change status',     15],   // POST status
  ['add task',           8],   // PUT tasks, one longer
  ['set test result',    7],   // PUT test-cases
  ['load board',         4],   // GET /api/board
  ['sync (validate)',    1],   // GET /api/validate
];

const total = MIX.reduce((s, [, w]) => s + w, 0);
const pick = () => {
  let r = Math.random() * total;
  for (const [name, w] of MIX) { if ((r -= w) < 0) return name; }
  return MIX[0][0];
};

const samples = {};                       // action -> [ms]
const errors = {};
const record = (action, ms) => (samples[action] ||= []).push(ms);
const fail = (action, why) => { errors[action] ||= {}; errors[action][why] = (errors[action][why] || 0) + 1; };

async function timed(action, run) {
  const t0 = process.hrtime.bigint();
  try {
    const res = await run();
    const ms = Number(process.hrtime.bigint() - t0) / 1e6;
    if (!res.ok) { fail(action, 'HTTP ' + res.status); return null; }
    record(action, ms);
    return res;
  } catch (e) {
    fail(action, e.code || e.message);
    return null;
  }
}

const json = (path) => fetch(BASE + path);
const send = (path, method, body) => fetch(BASE + path, {
  method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
});

const STATUSES = ['Not Yet Started', 'Under Review', 'Refined', 'In Progress', 'Vendor Test', 'Done', 'On Hold'];
const TC_STATUSES = ['Not Run', 'Passed', 'Failed'];
const any = a => a[Math.floor(Math.random() * a.length)];

function percentiles(list) {
  if (!list.length) return null;
  const s = [...list].sort((a, b) => a - b);
  const at = p => s[Math.min(s.length - 1, Math.floor(s.length * p))];
  return {
    n: s.length,
    p50: at(0.5), p95: at(0.95), p99: at(0.99),
    max: s[s.length - 1],
    mean: s.reduce((a, b) => a + b, 0) / s.length,
  };
}

const pad = (s, w) => String(s).padEnd(w);
const num = (v, w = 7) => String(v.toFixed(1)).padStart(w);

(async () => {
  // Warm up and learn the board, exactly as the browser does on first load.
  const boot = await fetch(BASE + '/api/board');
  if (!boot.ok) { console.error('board did not load:', boot.status); process.exit(1); }
  const board = await boot.json();
  const stories = board.epics.flatMap(e => e.stories);
  const boardBytes = Buffer.byteLength(JSON.stringify(board));

  console.log(`${stories.length} stories / ${board.epics.length} epics`);
  console.log(`board payload ${(boardBytes / 1024).toFixed(0)} KB\n`);
  console.log(`driving ${SECONDS}s of activity against ${BASE} …\n`);

  const started = Date.now();
  const deadline = started + SECONDS * 1000;
  const firstMinute = [], lastMinute = [];
  let ops = 0;

  while (Date.now() < deadline) {
    const action = pick();
    const story = any(stories);
    const t0 = Date.now();

    switch (action) {
      case 'open story':
        await timed(action, () => json('/api/story/' + story.code));
        break;

      case 'load board':
        await timed(action, () => json('/api/board'));
        break;

      case 'sync (validate)':
        await timed(action, () => json('/api/validate'));
        break;

      case 'change status':
        await timed(action, () => send('/api/story/' + story.code + '/status', 'POST',
          { status: any(STATUSES) }));
        break;

      case 'tick task': {
        // Read what is there, flip one, write the list back — what the checkbox does.
        const res = await fetch(BASE + '/api/story/' + story.code);
        if (!res.ok) { fail(action, 'HTTP ' + res.status); break; }
        const detail = await res.json();
        if (!detail.tasks.length) break;
        const tasks = detail.tasks.map(t => ({ text: t.text, done: t.done }));
        const i = Math.floor(Math.random() * tasks.length);
        tasks[i].done = !tasks[i].done;
        await timed(action, () => send('/api/story/' + story.code + '/tasks', 'PUT', { tasks }));
        break;
      }

      case 'add task': {
        const res = await fetch(BASE + '/api/story/' + story.code);
        if (!res.ok) { fail(action, 'HTTP ' + res.status); break; }
        const detail = await res.json();
        const tasks = detail.tasks.map(t => ({ text: t.text, done: t.done }));
        // Keep the file from growing without bound over a long run.
        if (tasks.length > 12) tasks.length = 6;
        tasks.push({ text: 'Task added by the perf driver at ' + new Date().toISOString(), done: false });
        await timed(action, () => send('/api/story/' + story.code + '/tasks', 'PUT', { tasks }));
        break;
      }

      case 'set test result': {
        const res = await fetch(BASE + '/api/story/' + story.code);
        if (!res.ok) { fail(action, 'HTTP ' + res.status); break; }
        const detail = await res.json();
        if (!detail.testCases.length) break;
        const cases = detail.testCases.map(c => ({ text: c.text, status: c.status }));
        cases[Math.floor(Math.random() * cases.length)].status = any(TC_STATUSES);
        await timed(action, () => send('/api/story/' + story.code + '/test-cases', 'PUT', { testCases: cases }));
        break;
      }
    }

    ops++;
    const elapsed = Date.now() - started;
    const took = Date.now() - t0;
    if (elapsed < 60_000) firstMinute.push(took);
    if (elapsed > (SECONDS - 60) * 1000) lastMinute.push(took);
  }

  const ran = (Date.now() - started) / 1000;

  console.log(pad('action', 20) + '    n     p50     p95     p99     max    mean   (ms)');
  console.log('─'.repeat(70));
  for (const [name] of MIX) {
    const p = percentiles(samples[name]);
    if (!p) { console.log(pad(name, 20) + '    –  (not exercised)'); continue; }
    console.log(pad(name, 20) + String(p.n).padStart(5) + num(p.p50) + num(p.p95)
      + num(p.p99) + num(p.max) + num(p.mean));
  }

  const all = Object.values(samples).flat();
  const overall = percentiles(all);
  console.log('─'.repeat(70));
  console.log(pad('all requests', 20) + String(overall.n).padStart(5) + num(overall.p50)
    + num(overall.p95) + num(overall.p99) + num(overall.max) + num(overall.mean));

  console.log(`\n${ops.toLocaleString()} operations in ${ran.toFixed(0)}s — ${(ops / ran).toFixed(1)} ops/sec`);

  // Does it get slower the longer it runs? Files are rewritten thousands of times here.
  const a = percentiles(firstMinute), b = percentiles(lastMinute);
  if (a && b) {
    const drift = ((b.p50 - a.p50) / a.p50) * 100;
    console.log(`drift: first minute p50 ${a.p50.toFixed(1)}ms → last minute p50 ${b.p50.toFixed(1)}ms `
      + `(${drift >= 0 ? '+' : ''}${drift.toFixed(0)}%)`);
  }

  const errorCount = Object.values(errors).reduce((s, m) => s + Object.values(m).reduce((x, y) => x + y, 0), 0);
  if (errorCount) {
    console.log('\nerrors:');
    for (const [action, kinds] of Object.entries(errors)) {
      for (const [why, count] of Object.entries(kinds)) console.log(`  ${pad(action, 20)} ${why} ×${count}`);
    }
  } else {
    console.log('errors: none');
  }
})();
