---
name: inventory-service
description: Change stock, reservations, inventory CQRS APIs, Redis availability caching, or inventory workflow consumers.
---

# Inventory bounded context

Inventory owns on-hand stock and order reservations. Preserve `Available = OnHand - Reserved`; reserve only when sufficient, release only a live reservation, and commit exactly once after payment. Use the aggregate `Version` as an optimistic concurrency token.

Consume order, release, and commit events through the service-local Inbox. Persist stock/reservation changes plus response events in one PostgreSQL transaction and Outbox. Duplicate delivery must be a no-op.

Redis availability entries are short-lived read caches under `inventory:availability:{sku}`. Database concurrency rules prevent overselling; Redis is never used to authorize a reservation.
