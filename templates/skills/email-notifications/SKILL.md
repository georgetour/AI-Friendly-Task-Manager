---
name: email-notifications
description: >
  Use this skill when working on transactional email. Triggers on: 'order confirmation email',
  'shipped email', 'email templates'.
---

# Email Notifications

## Description

As a customer, I want an email when my order is confirmed or shipped, so that I do not have to keep
checking the site for updates.

This skill covers transactional email tied to an order's lifecycle: the templates themselves, and the
two triggers that send them — an order moving to Confirmed, and an order moving to Shipped. It is
distinct from the receipt `checkout` shows and emails at the moment of payment: the receipt confirms
the transaction that just happened; this skill's emails confirm order-lifecycle events that can happen
later, including ones with no customer present to see a page at all (a warehouse marking an order
shipped, for instance).

Owned by US-11 (Email Notifications).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Transactional email templates** — one template for order-confirmed, one for order-shipped, both
   carrying enough order detail (items, total, status) to stand alone without the customer visiting
   the site. *(→ AC1)*
2. **Send on order confirmed and shipped** — queue the matching email the moment an order's status
   transitions to Confirmed or to Shipped. *(→ AC2, AC3)*
3. **Tests** — coverage for the confirmed-email trigger. *(→ AC2)*

## Acceptance Criteria

- [ ] AC1: A templated email exists for both the order-confirmed and order-shipped events.
- [ ] AC2: An order transitioning to Confirmed queues a confirmation email to the customer.
- [ ] AC3: An order transitioning to Shipped queues a shipped email to the customer.
