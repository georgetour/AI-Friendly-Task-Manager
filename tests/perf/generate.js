// Builds a synthetic backlog at whatever scale you ask for, in the real on-disk layout:
// a BACKLOG.yaml index plus one folder per story holding SKILL.md, tasks.yaml and test-cases.yaml.
//
//   node tests/perf/generate.js <target-dir> [epics] [storiesPerEpic]
//
// Zero dependencies, and it writes nothing inside the repo — the target directory is yours to pick.
const fs = require('fs');
const path = require('path');

const target = process.argv[2];
const epicCount = Number(process.argv[3] || 50);
const perEpic = Number(process.argv[4] || 20);

if (!target) {
  console.error('usage: node tests/perf/generate.js <target-dir> [epics] [storiesPerEpic]');
  process.exit(1);
}

const STATUSES = ['Not Yet Started', 'Under Review', 'Refined', 'In Progress', 'Vendor Test', 'Done', 'On Hold'];
const TC_STATUSES = ['Not Run', 'Passed', 'Failed'];
const AREAS = ['checkout', 'billing', 'search', 'accounts', 'reporting', 'notifications',
               'catalogue', 'shipping', 'returns', 'analytics', 'onboarding', 'permissions'];
const VERBS = ['Manage', 'Export', 'Import', 'Review', 'Archive', 'Schedule', 'Validate', 'Reconcile'];

const skills = path.join(target, 'skills');
fs.rmSync(target, { recursive: true, force: true });
fs.mkdirSync(skills, { recursive: true });

const lines = ['project: Scale Test', 'roadmap: [V1, V2, V3, V4, V5]', 'epics:'];
let n = 0;
let files = 0;

for (let e = 0; e < epicCount; e++) {
  lines.push(`  - number: ${e}`);
  lines.push(`    title: ${VERBS[e % VERBS.length]} ${AREAS[e % AREAS.length]} — epic ${e}`);
  lines.push('    stories:');

  for (let s = 0; s < perEpic; s++) {
    n++;
    const code = 'US-' + String(n).padStart(4, '0');
    const folder = `story-${n}-${AREAS[n % AREAS.length]}`;

    lines.push(`      - code: ${code}`);
    lines.push(`        title: ${VERBS[n % VERBS.length]} ${AREAS[n % AREAS.length]} for story ${n}`);
    lines.push(`        status: ${STATUSES[n % STATUSES.length]}`);
    lines.push(`        release: V${(n % 5) + 1}`);
    lines.push(`        folder: ${folder}`);

    const dir = path.join(skills, folder);
    fs.mkdirSync(dir, { recursive: true });

    // 3-6 tasks, 1-3 test cases — the shape a real story tends to have.
    const taskCount = 3 + (n % 4);
    const tcCount = 1 + (n % 3);

    fs.writeFileSync(path.join(dir, 'tasks.yaml'),
      Array.from({ length: taskCount }, (_, t) =>
        `- text: Task ${t + 1} for story ${n} — something specific that has to be built\n` +
        `  done: ${t % 2 === 0}`).join('\n') + '\n');

    fs.writeFileSync(path.join(dir, 'test-cases.yaml'),
      Array.from({ length: tcCount }, (_, t) =>
        `- text: Test case ${t + 1} for story ${n} verifying the behaviour holds\n` +
        `  status: ${TC_STATUSES[(n + t) % TC_STATUSES.length]}`).join('\n') + '\n');

    fs.writeFileSync(path.join(dir, 'SKILL.md'),
      `---\nname: ${folder}\ndescription: >\n  Use this skill when working on ${AREAS[n % AREAS.length]}.\n---\n\n` +
      `# ${code} · Story ${n}\n\n## Description\n\n` +
      `As a user, I want story ${n} to work, so that the ${AREAS[n % AREAS.length]} area behaves.\n\n` +
      `## Acceptance Criteria\n\n- [ ] AC1: it works\n- [ ] AC2: it keeps working\n`);

    files += 3;
  }
}

fs.writeFileSync(path.join(target, 'BACKLOG.yaml'), lines.join('\n') + '\n');

const indexBytes = fs.statSync(path.join(target, 'BACKLOG.yaml')).size;
console.log(`${n} stories across ${epicCount} epics`);
console.log(`index      ${(indexBytes / 1024).toFixed(0)} KB`);
console.log(`story files ${files.toLocaleString()} in ${n.toLocaleString()} folders`);
console.log(`written to ${target}`);
