---
name: wishlist
description: >
  Use this skill when working on saved products. Triggers on: 'wishlist', 'save for later',
  'price-drop notification'.
---

# Wishlist

## Description

As a customer, I want to save products for later, so that I can plan purchases I am not ready to make
yet instead of losing track of them between visits.

This skill covers saving and removing products from a personal wishlist, and being notified when a
saved product's price drops. It depends on `catalog` for product identity and current price, and its
price-drop alert is a separate trigger from the order-lifecycle emails `notifications` sends — nothing
here fires off an order at all.

Owned by US-13 (Wishlist).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Add and remove wishlist items** — a signed-in customer can add a product to their wishlist and
   remove it later; the list persists across sessions. *(→ AC1)*
2. **Notify on price drop** — when a wishlisted product's price falls below what it was when saved (or
   below its last-known price), notify the customer. *(→ AC2)*
3. **Tests** — verify a wishlisted item persists. *(→ AC1)*

## Acceptance Criteria

- [ ] AC1: Adding a product to the wishlist persists it against the customer's account; it is still
  there on a later visit or session.
- [ ] AC2: A price drop on a wishlisted product triggers a notification to the customer who saved it.
