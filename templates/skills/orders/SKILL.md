---
name: orders
description: >
  Use this skill when working on order history. Triggers on: 'order list', 'order detail', 'cancel
  order', 'order status'.
---

# Orders

## Description

As a customer, I want to see my past and pending orders, so that I can track what I bought and what
is still on its way instead of digging through old confirmation emails.

This skill covers the order itself once it exists: the order model and the statuses it moves through,
the list and detail pages a customer uses to review their history, and cancelling an order while it is
still pending. It does not cover how an order gets created in the first place — that conversion from a
basket into an order belongs to `checkout`. This skill owns the order's life after that point.

Owned by US-08 (Orders).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Order model, statuses, and history** — an order records its line items, total, current status,
   and every status it has passed through, not just the latest one. *(→ AC1)*
2. **Order list and detail pages** — a list scoped to the signed-in customer, and a detail page for a
   single order showing its full line items and status history. *(→ AC2, AC3)*
3. **Cancel a pending order** — a customer can cancel an order only while it is still Pending; the
   action is rejected once it has moved past that. *(→ AC4, AC5)*
4. **Tests** — coverage for list scoping and the cancel transition. *(→ AC2, AC4)*

## Acceptance Criteria

- [ ] AC1: Every order is recorded with a current status and a history of the statuses it has held.
- [ ] AC2: The order list shows only the signed-in customer's own orders, most recent first.
- [ ] AC3: The order detail page shows the full line items, total, and status history for one order.
- [ ] AC4: Cancelling a Pending order sets its status to Cancelled and records the transition.
- [ ] AC5: Attempting to cancel an order that is not Pending is rejected with a clear message.
