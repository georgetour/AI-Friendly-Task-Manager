---
name: checkout-and-payment
description: >
  Use this skill when working on paying for a basket. Triggers on: 'checkout', 'payment', 'basket to
  order', 'receipt'.
---

# Checkout and Payment

## Description

As a customer, I want to pay for my basket securely, so that I can complete a purchase with
confidence and know immediately whether it succeeded.

This skill covers the moment a basket becomes an order: converting the current basket into an order
record, handing the total off to a payment provider, and showing (and emailing) a receipt once payment
succeeds. It stops at the receipt — what happens to the order afterwards (status history, cancellation)
belongs to `orders`, and the separate confirmed/shipped transactional emails belong to `notifications`.

Owned by US-09 (Checkout and Payment).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Basket to order conversion** — turn the current basket's line items and total into an order
   record at the moment checkout is started. *(→ AC1)*
2. **Payment provider integration** — submit the order total to a payment provider and act on the
   result: mark the order Paid on success, leave it untouched on decline. *(→ AC2, AC3)*
3. **Receipt shown and emailed on success** — display a receipt immediately after a successful payment
   and send the same receipt by email. *(→ AC4)*
4. **Tests** — coverage for the success and decline paths. *(→ AC2, AC3)*

## Acceptance Criteria

- [ ] AC1: Starting checkout converts the current basket into an order with matching line items and
  total.
- [ ] AC2: A successful payment marks the order Paid.
- [ ] AC3: A declined payment leaves the basket intact and creates no lingering unpaid order.
- [ ] AC4: On successful payment, a receipt is shown on-screen and emailed to the customer.
