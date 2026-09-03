---
name: order-service
description: Add or modify the Ordering bounded context, order aggregate, commands, queries, persistence, integration events, or order workflow.
---

# Ordering bounded context

Keep order lifecycle rules inside the aggregate. Controllers translate HTTP requests to commands and do not construct outbox rows directly. Monetary values require an amount plus ISO currency and must not use floating point.

The Ordering service owns order data and its PostgreSQL database. Store product snapshots needed for historical orders; do not query another service's database. Coordinate reservation, payment, and fulfillment asynchronously using versioned integration events and persisted saga state when compensation is required.

Write order state and outgoing events atomically through the same DbContext. Make creation idempotent using a client request/idempotency key. Reject invalid state transitions and use optimistic concurrency for competing updates.

`OrderDbContext` derives from `MSADbContext` and also implements the service-local Outbox and Saga stores. Controllers send commands and queries through MediatR. Command handlers never call `SaveChanges`; `TransactionBehavior` dispatches domain events and commits aggregate, outbox, and saga mutations together. Query handlers project no-tracking DTOs directly.
