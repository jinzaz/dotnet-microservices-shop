---
name: inventory-service
description: Change stock, reservations, inventory CQRS APIs, Redis availability caching, or inventory workflow consumers.
---

# Inventory bounded context

Inventory owns on-hand stock and order reservations. Preserve `Available = OnHand - Reserved`; reserve only when sufficient, release only a live reservation, and commit exactly once after payment. Use the aggregate `Version` as an optimistic concurrency token.

Use the same reservation lifecycle for ordinary and limited-offer items. A reservation line may reference an optional Inventory-owned allocation; an allocation ring-fences stock for an offer without creating a separate flash-sale inventory workflow. Enforce allocation window, available quantity, and cumulative per-user quantity in the reservation transaction. Released reservations may become eligible again according to the allocation policy; do not model every offer as one permanent order per user.

Start allocation stock with one atomic PostgreSQL counter. Add generic allocation buckets only after measured lock contention shows the single row is a bottleneck; inventory quantity alone is not evidence of concurrency.

Consume order, release, and commit events through the service-local Inbox. Persist stock/reservation changes plus response events in one PostgreSQL transaction and Outbox. Duplicate delivery must be a no-op.

Redis availability entries are short-lived read caches under `inventory:availability:{sku}`. Database concurrency rules prevent overselling; Redis is never used to authorize a reservation.
