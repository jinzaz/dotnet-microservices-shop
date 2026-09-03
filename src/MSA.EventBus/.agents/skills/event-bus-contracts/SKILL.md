---
name: event-bus-contracts
description: Add or change integration-event contracts, handler registration, routing, serialization, or delivery semantics in MSA.EventBus.
---

# Event bus contracts

Keep this project broker-agnostic. Broker APIs belong in `MSA.EventBusRabbitMQ`; persistence belongs in `MSA.EventBusOutbox`.

Integration events are immutable facts in past tense. Treat their serialized shape and `EventType` as a versioned public contract. Add a new version for breaking changes. Include stable message, correlation, and causation identifiers where workflows need tracing.

Assume at-least-once delivery: handlers must be idempotent and acknowledge only after successful handling. Do not encode domain entities directly in event payloads or rely on CLR assembly-qualified names for routing.
