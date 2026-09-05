---
name: navigation-menus
description: >
  Use this skill when working on site navigation. Triggers on: 'signed-out menu', 'signed-in menu',
  'navigation state'.
---

# Navigation Menus

## Description

As a customer, I want the navigation to reflect whether I am signed in, so that I always see the
actions available to me instead of hunting for a Sign in link buried behind an account menu, or
seeing account options I can't actually use yet.

This skill covers the two menu states the top navigation can be in: signed-out (visitor) and
signed-in (customer). It depends on `user-management` for the authentication state itself but owns
the navigation's content and layout in each state.

Owned by US-05 (Navigation Menus).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Signed-out menu** — Browse, Sign in, Register: the minimal set a visitor needs to explore the
   catalog or start an account. *(→ AC1)*
2. **Signed-in menu** — Orders, Wishlist, Profile, Sign out: the account-scoped actions a customer
   needs once authenticated. *(→ AC2)*
3. **Tests** — verify menu contents for both signed-out and signed-in states. *(→ AC1, AC2)*

## Acceptance Criteria

- [ ] AC1: A signed-out visitor sees only Browse, Sign in, and Register — no account-scoped entries.
- [ ] AC2: A signed-in customer sees Orders, Wishlist, Profile, and Sign out.
