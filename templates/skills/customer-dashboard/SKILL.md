---
name: customer-dashboard
description: >
  Use this skill when working on the signed-in customer summary. Triggers on: 'customer dashboard',
  'recent orders panel', 'spend this month'.
---

# Customer Dashboard

## Description

As a signed-in customer, I want a dashboard summarising recent activity, so that I can pick up where
I left off instead of re-navigating to orders or re-deriving how much I've spent this month.

This skill covers the dashboard's two panels: a list of recent orders, and a running total of what
the customer has spent so far in the current calendar month. It is presentation over data other
skills already own — order data comes from `orders`; this skill adds no order records of its own,
only summarizes them.

Owned by US-10 (Customer Dashboard).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Recent orders panel** — show the customer's five most recent orders, most recent first, each
   linking through to its detail page. *(→ AC1)*
2. **Spend-this-month summary** — total what the customer has spent in the current calendar month, so
   far. *(→ AC2)*
3. **Tests** — coverage for the recent-orders panel. *(→ AC1)*

## Acceptance Criteria

- [ ] AC1: The dashboard shows the signed-in customer's five most recent orders, most recent first.
- [ ] AC2: The dashboard shows a running total of the customer's spend for the current calendar month.
