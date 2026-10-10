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

After-sales (`AfterSalesRequest`) is a separate aggregate in Ordering: refund-only, return-and-refund, exchange, and reship, each over a partial quantity of order lines. Quantities held by active requests cap new ones, and refunds are allocated from each line's paid amount so the last unit takes the remainder and cumulative refunds never exceed the line. Refunds reuse `RefundPaymentRequested`/`PaymentRefunded`/`PaymentRefundFailed` with a fresh `RefundRequestId` per attempt; Payment allows one refund in flight per payment, so Ordering serializes them per order under the order row lock (order before after-sales row). Shipping is blocked while an after-sales request on an unshipped order is open, and a fully refunded unshipped order closes its fulfillment. Evidence images live in the private bucket; Ordering signs uploads with an applicant-scoped object key and signs short-lived read URLs per detail query, never storing URLs. Messages of `AfterSalesRuleException` are shown to users verbatim.

After-sales side effects come from domain events on `AfterSalesRequest` (status change → notification event, refund, stock), so manual actions, `AfterSalesTimeoutService` and acknowledgement handlers emit them identically; never publish them from handlers. Exchange and reship hold replacement stock at approval (`Reserve`), ship only once Inventory confirmed it (`Commit`), and return it when the request closes (`Release`, deferred until a pending hold is answered). Returned goods are disposed once after receipt: restocked units go back via `Restock`, and any unit not restocked needs a written reason. Timeouts count from `StatusChangedOnUtc` and are defined once in `AfterSalesOptions.Due`, used both by the scanner query and the re-check under lock.
