---
name: skill-name-here
description: >
  Use this skill when [trigger]. Triggers on: '[keyword1]', '[keyword2]'.
---

# [Skill Name]

> A user story is **Description → Tasks → Acceptance Criteria** only. Nothing else belongs in this file:
> - **Tasks** are tracked (with done-state) in `tasks.yaml`.
> - **Test cases** are tracked (with pass/fail) in `test-cases.yaml` — not listed here.
> - **Technical detail, flows, data model, gotchas** live in `docs/detailed-designs/[module].md`.

## Description

[Plain, precise description of what happens and why — written so a developer knows exactly what to
build, and a BA or stakeholder can follow it without needing technical background. Use "As a
[actor], I want to [action], so that [benefit]" when there is a real user or role initiating it.
Use a direct system-behavior description when it is automation, scheduled, or event-driven.
Reference related design images here if they help explain the intent, e.g. `docs/designs/[screen].png`.]

## Tasks

> One deliverable per task; each names the acceptance criterion it satisfies. Functional, not code —
> the *how* lives in the detailed design. Mirror these into `tasks.yaml`.

1. **[Deliverable 1]** — what it does. *(→ AC1)*
2. **[Deliverable 2]** — what it does. *(→ AC2)*

## Acceptance Criteria

> The continuation of the tasks — testable conditions that define "done". The ones you actually
> execute become entries in `test-cases.yaml`.

- [ ] **AC1** — [observable, checkable outcome]
- [ ] **AC2** — [observable, checkable outcome]
