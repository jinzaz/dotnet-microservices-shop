---
name: basket-service
description: Change the Basket bounded context, SKU snapshots, basket commands, persistence, or checkout handoff.
---

# Basket bounded context

Basket owns a user's mutable selection and its PostgreSQL database. Address basket lines by sellable variant/SKU, consume Catalog variant snapshots through the Inbox, and persist the product and variant display snapshot needed to render the basket without querying Catalog tables.

Never trust client-supplied names, option values, prices, or availability. Adding or increasing a line requires the current local variant projection; retain the price and revision observed when the user selected it so later changes can be reported. Basket checkout hands SKU, quantity, and the basket version to Ordering; Ordering performs final pricing and Inventory remains authoritative for stock.

Write mutations through MediatR and the transaction pipeline, use optimistic version checks, and clear a checked-out basket idempotently when the matching order-submitted event arrives.
