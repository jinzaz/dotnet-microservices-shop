---
name: catalog-service
description: Change the Catalog bounded context, product/category model, catalog APIs, Redis projections, or ProductChanged events.
---

# Catalog bounded context

Catalog owns product identity and merchandising in its PostgreSQL database. Model `Product` as the SPU containing category, shared name, description, and lifecycle; model `ProductVariant` as the sellable SKU containing the globally unique normalized SKU, option-value snapshot, price, currency, and variant status. A product must have an active variant before publication. Other services consume variant-level snapshots and never read Catalog tables.

Catalog also owns ordered product media metadata. Media may apply to the whole product or one of its variants. Persist stable object keys or permanent URLs, media type, thumbnail metadata, alternative text, and sort order; do not store media bytes or expiring signed URLs in PostgreSQL.

Model flash sales as limited promotions, not as a separate product type. Promotion owns its window, SKU targets, pricing benefit, and per-user limit. When an offer needs ring-fenced inventory, request an Inventory-owned allocation and do not activate the offer until Inventory grants it.

Use CQRS through MediatR. Commands enforce aggregate invariants, use optimistic concurrency, and stage variant or promotion snapshots in the local Outbox. Queries use no-tracking DTO projections. Product creation must reference an existing category and SKU remains unique across variants.

Redis caches read models only. Use the `catalog:product:{id}` namespace, bounded TTLs, and invalidate on writes. A cache miss or outage must not change the source of truth.
