---
name: public-homepage
description: >
  Use this skill when working on the public landing page. Triggers on: 'homepage', 'hero section',
  'featured categories'.
---

# Public Homepage

## Description

As a visitor, I want a homepage that explains the product and offers a clear next step, so that I
know what Acme App does before signing up, without having to guess from a bare navigation bar.

This skill covers the anonymous-visitor landing page: a hero section that states what the product is
and offers one primary action, and a featured-categories strip that gives a visitor an immediate,
concrete reason to click into the catalog. It is deliberately the first thing a new install shows.

Owned by US-07 (Public Homepage).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Hero section** — a headline, brief supporting copy, and one primary call-to-action button that
   leads a visitor into the product. *(→ AC1)*
2. **Featured categories strip** — a curated set of categories shown with links straight into the
   catalog, so a visitor's first click already lands somewhere useful. *(→ AC2)*
3. **Tests** — verify the page loads correctly for anonymous visitors. *(→ AC3)*

## Acceptance Criteria

- [ ] AC1: The homepage shows a hero section with a single, clear primary call to action.
- [ ] AC2: The homepage shows a strip of featured categories linking into the catalog.
- [ ] AC3: The homepage loads successfully for anonymous, signed-out visitors — no authentication
  required.
