---
name: commerce-contracts
description: Add or evolve cross-service e-commerce integration events and their compatibility rules in MSA.Contracts.
---

# Commerce integration contracts

Keep this project transport-only. Contracts may contain immutable DTOs and integration events, but never domain aggregates, EF mappings, service interfaces, or provider SDK types.

Event identity and creation time must survive serialization so Inbox idempotency works. Prefer additive schema evolution; do not rename, remove, or reinterpret published fields without introducing a new versioned event. Include correlation identifiers and only the business snapshot a consumer needs. Never expose credentials, access tokens, or payment secrets.

Update every producer, consumer registration, and workflow compensation when changing an event. Confirm payload round-tripping and build the whole solution.
