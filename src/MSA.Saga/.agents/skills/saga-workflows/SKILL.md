---
name: saga-workflows
description: Design or implement persisted saga state machines, distributed workflow transitions, timeouts, idempotency, and compensations in MSA.Saga or business services.
---

# Saga workflows

Use orchestration only when a business workflow spans multiple bounded contexts and needs explicit recovery. Persist correlation id, state, version, timestamps, processed message ids, and failure reason in a service-owned PostgreSQL database.

Transitions must be idempotent and concurrency-safe. Persist state and outgoing commands/events atomically with an outbox. Compensation is a business action, not a database rollback; make it retryable and observable. Model terminal completed, compensated, and failed states plus timeout handling.

Keep this library limited to reusable contracts and mechanics. Concrete order/payment/inventory workflow states belong to the coordinating bounded context.

Persist Saga rows in the coordinating service's own database and DbContext, alongside but separate from its Outbox table. Use a concurrency-token `Version` for optimistic concurrency and timeout lease fields so multiple service instances can safely poll. Timeout handlers apply a business transition or compensation and persist any emitted Outbox messages in the same local transaction.
