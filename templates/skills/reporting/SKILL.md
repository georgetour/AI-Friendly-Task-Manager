---
name: reporting
description: >
  Use this skill when working on revenue reporting. Triggers on: 'revenue report', 'monthly revenue',
  'CSV export'.
---

# Reporting

## Description

As an owner, I want revenue and volume reports, so that I can see how the business is performing
without querying the database by hand every time a number is needed.

This skill covers owner-facing reporting: a revenue-by-month view built from paid orders, and a CSV
export of the same data for anyone who wants to work with it outside the app. It reads `orders`' data
rather than maintaining a separate ledger — the report and the export must always agree with each
other and with the underlying order records, since a report that drifts from the source data is worse
than no report at all.

Owned by US-12 (Reporting).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Revenue by month report** — group paid orders by the calendar month they were paid in and total
   each month's revenue. *(→ AC1)*
2. **Export to CSV** — export the same monthly totals (and the orders behind them) as a downloadable
   CSV. *(→ AC2)*
3. **Tests** — verify the monthly total matches the underlying paid orders. *(→ AC1)*

## Acceptance Criteria

- [ ] AC1: The revenue-by-month report's total for a given month equals the sum of that month's paid
  orders.
- [ ] AC2: The report's data can be exported as a CSV file matching what's shown on screen.
