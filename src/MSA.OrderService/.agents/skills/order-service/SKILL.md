---
name: order-service
description: Add or modify the Ordering bounded context, order aggregate, commands, queries, persistence, integration events, or order workflow.
---

# Ordering bounded context

Keep order lifecycle rules inside the aggregate. Controllers translate HTTP requests to commands and do not construct outbox rows directly. Monetary values require an amount plus ISO currency and must not use floating point.

The Ordering service owns order data and its PostgreSQL database. Store product snapshots needed for historical orders; do not query another service's database. Coordinate reservation, payment, and fulfillment asynchronously using versioned integration events and persisted saga state when compensation is required.

Write order state and outgoing events atomically through the same DbContext. Make creation idempotent using a client request/idempotency key. Reject invalid state transitions and use optimistic concurrency for competing updates.

Never trust client-supplied product names or prices. Build order lines from the local Catalog event projection. A successful payment commits the reservation; inventory rejection, payment failure, and Saga timeout cancel and compensate. Scope customer queries to the token subject; only explicit operator/admin roles may read another user's order.

Use one order-submission command, aggregate, table set, Outbox event, and Saga for ordinary and limited-offer orders. An order item may carry an optional offer identifier, but flash sales must not introduce a parallel order aggregate or duplicated handler. Keep variant identity, SKU, option values, price, offer, and promotion revision as immutable order-line snapshots. Ordering may reject stale or inactive offers early from its local projection, while Inventory remains authoritative for stock allocation and cumulative per-user allocation limits.

`OrderDbContext` derives from `MSADbContext` and also implements the service-local Outbox and Saga stores. Controllers send commands and queries through MediatR. Command handlers never call `SaveChanges`; `TransactionBehavior` dispatches domain events and commits aggregate, outbox, and saga mutations together. Query handlers project no-tracking DTOs directly.
