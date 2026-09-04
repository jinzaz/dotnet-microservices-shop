---
name: payment-service
description: Change payment entities, CQRS/API endpoints, Paylinks callback handling, persistence, expiry, or payment integration events.
---

# Payment bounded context

Payment owns payment attempts and provider transaction references in its PostgreSQL database. One order has one idempotent merchant trade number. Persist callback state and `PaymentSucceeded`/`PaymentFailed` Outbox rows atomically with optimistic concurrency.

Provider callbacks are anonymous but never trusted: verify the provider signature, successful state, channel, merchant trade number, amount, and currency before completing a payment. Callback replay must not publish a second success event.

Expire pending payments with a multi-instance-safe database worker; publish failure through Outbox so the Order Saga releases inventory. Customer reads are restricted to the token subject, with explicit operator/admin override.
