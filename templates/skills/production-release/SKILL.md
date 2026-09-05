---
name: production-release
description: >
  Use this skill when [trigger]. Triggers on: '[keyword1]', '[keyword2]'.
---

# US-16 · Production Release

## Description

[Plain, precise description of what happens and why — written so a developer
knows exactly what to build, and a BA/stakeholder can follow it without
needing technical background. Use "As a [actor], I want to [action], so
that [benefit]" when there's a real user or role initiating it. Use a
direct system-behavior description when it's automation/scheduled/event-driven.
Reference related design images or diagrams here if they help explain the
high-level intent, e.g. `docs/designs/[screen].png`.]

## Tasks

> One deliverable per task; tag each with the acceptance criterion(s) it satisfies, so task↔AC is
> explicit and a developer can see what "done" looks like. Functional, not code — the *how* lives in
> the detailed design. Don't bundle several features into one vague task.

1. **[Deliverable 1]** — what it does (e.g. "Build the X API — create, list, edit, delete; owner-scoped"). *(→ AC1, AC4)*
2. **[Deliverable 2]** — what it does (e.g. "Build the X page — list / add / edit / delete"). *(→ AC2, AC3)*
3. **Tests** — API/service + Playwright E2E covering each acceptance criterion. *(→ all AC)*

## Acceptance Criteria

> The continuation of the tasks — testable conditions that define "done." Each becomes a test case below.

- [ ] AC1: [Condition]
- [ ] AC2: [Condition]
