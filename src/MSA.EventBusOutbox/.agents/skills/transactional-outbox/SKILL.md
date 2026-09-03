---
name: transactional-outbox
description: Implement or modify transactional outbox storage, dispatch, retries, cleanup, and publisher coordination in MSA.EventBusOutbox or a consuming service.
---

# Transactional outbox

Write business changes and the outbox row through the same service-owned DbContext and PostgreSQL transaction. The background processor is the only component that publishes stored messages through `IEventBus`.

Model at-least-once delivery explicitly. Claim messages safely for concurrent workers, record attempts and error details, retry with bounded exponential backoff and jitter, and move exhausted messages to a reviewable failed state. Mark sent only after broker confirmation. Consumers remain idempotent because a crash can cause redelivery.

Store UTC timestamps and a stable event type. Avoid embedding secrets in payloads or headers. Keep cleanup retention configurable and preserve failed records for diagnosis.

Use `Status`, `NextAttemptOnUtc`, `LockId`, `LockedUntilUtc`, and a concurrency-token `Version`. Claim a bounded batch before publishing; expired processing leases are reclaimable. Persist each publish outcome promptly. Keep Outbox in the owning service database and DbContext so command data plus new messages commit atomically.
