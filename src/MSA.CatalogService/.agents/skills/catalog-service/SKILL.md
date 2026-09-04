---
name: catalog-service
description: Change the Catalog bounded context, product/category model, catalog APIs, Redis projections, or ProductChanged events.
---

# Catalog bounded context

Catalog owns product identity, category, SKU, description, price, currency, and sale status in its PostgreSQL database. Other services consume `ProductChangedIntegrationEvent`; they never read Catalog tables.

Use CQRS through MediatR. Commands enforce aggregate invariants, use optimistic concurrency, and stage ProductChanged in the local Outbox. Queries use no-tracking DTO projections. Product creation must reference an existing category and SKU remains unique.

Redis caches read models only. Use the `catalog:product:{id}` namespace, bounded TTLs, and invalidate on writes. A cache miss or outage must not change the source of truth.
