---
name: day-0-local-setup
description: >
  Use this skill when working on the project's local development environment. Triggers on:
  'day 0 setup', 'health endpoint', 'clean database migrations'.
---

# Local Development Setup

## Description

As a developer, I want the project to run end to end on a fresh machine, so that a new teammate is
productive on their first day instead of losing it to undocumented setup steps.

This skill covers the "day 0" path: what toolchain a new machine needs, a health endpoint that proves
the app started and can reach its database, and migrations that apply cleanly with nothing manual.
None of this is about the product's features — it is the floor everything else stands on.

Owned by US-03 (Day 0 — Local Setup).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Toolchain documentation** — record the exact SDK/runtime versions and prerequisites a new
   machine needs, in one place, kept current as dependencies change. *(→ AC1)*
2. **Health endpoint** — expose a lightweight endpoint that confirms the app started and can reach
   its database, usable both by a developer and by deploy tooling. *(→ AC2)*
3. **Clean-database migrations** — ensure migrations run in order against an empty database with no
   manual steps, seed data, or hand-run scripts required. *(→ AC3)*
4. **Tests** — automated coverage for the health check and for migrations against a fresh database.
   *(→ AC2, AC3)*

## Acceptance Criteria

- [ ] AC1: A new developer can get the app running by following the documented setup steps alone,
  with no undocumented tribal knowledge required.
- [ ] AC2: The health endpoint returns 200 on a clean checkout, confirming the app started and can
  reach its database.
- [ ] AC3: Database migrations apply cleanly against an empty database with no manual intervention.
