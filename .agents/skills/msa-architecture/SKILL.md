---
name: msa-architecture
description: Evolve this repository's .NET 10 e-commerce platform architecture, service boundaries, DDD/CQRS layering, data ownership, or cross-cutting conventions.
---

# Architecture decisions

Treat each business service as an independently deployable bounded context that owns its PostgreSQL database. Never join tables across services or share EF entities/DbContexts. The gateway and identity service also own separate databases. Redis is a cache or coordination aid, never the source of truth.

Keep dependencies pointing inward: API/Infrastructure -> Application -> Domain. Domain code must not reference ASP.NET Core, EF Core, RabbitMQ, Redis, OpenIddict, or YARP. Put reusable technical primitives in `MSA.Infrastrucute`; do not put business rules there.

## HTTP route conventions

Business-service endpoints never include an `api` segment. Use lowercase kebab-case paths beginning with the bounded-context name: `/{business}/{subbusiness}/...`. Put administrative endpoints under `/{business}/admin/{subbusiness}/...`; never use `/admin/{business}`. Canonical business segments are `catalog`, `inventory`, `order`, `payment`, `basket`, `notification`, and `user`.

The API gateway owns the public `/api` prefix. Its public routes match `/api/{business}/...` and remove only `/api`, preserving the business segment forwarded to the service. BFF-owned endpoints retain their `/api` prefix because they may be called directly. A BFF YARP catch-all forwards other `/api/{business}/...` requests unchanged to the gateway. Identity protocol and authentication endpoints are not business-service routes and may retain their protocol or `/api/auth` paths.

When a route changes, update every caller, gateway/BFF transform, and `Location` header in the same change. This repository does not keep `.http` request samples. Do not keep compatibility aliases unless the user explicitly requests a deprecation window.

## Persistence naming and configuration

Every table owned by a business service uses lowercase snake case and the bounded-context prefix: `{business}_{table_name}`. This includes the service-local `outbox_messages`, `inbox_messages`, and `saga_states` tables. EF migration-history tables and tables owned by Identity/OpenIddict or a BFF are framework boundaries and are excluded unless a task explicitly includes them.

Keep business entity mappings in the owning service under `Infrastructure/EntityConfigurations` as `IEntityTypeConfiguration<T>` implementations. A business `DbContext` should contain DbSets, persistence-specific operations, save hooks, and `ApplyConfigurationsFromAssembly`, not inline entity mappings. Never centralize business entity configurations in `MSA.Repositories`; doing so couples bounded contexts. Shared messaging entity configurations stay in `MSA.Repositories/EntityConfigurations` and accept the owning service's table prefix.

Use commands for state changes and queries for reads. Enforce invariants in aggregates, not controllers. Dispatch in-process domain events through MediatR. Publish cross-service integration events through the transactional outbox; never publish to RabbitMQ inside the business transaction.

Every business DbContext derives from `MSADbContext`. Register it both as its concrete type and as scoped `MSADbContext`, then register MediatR plus `LoggingBehavior`, `ValidatorBehavior`, and `TransactionBehavior` in that order. Commands run inside the transaction behavior; queries remain transaction-free and use no-tracking projections. Command handlers stage aggregate and outbox changes and do not call `SaveChanges` themselves.

Prefer eventual consistency. Use `MSA.Saga` only for multi-step workflows requiring explicit persisted state, idempotent transitions, timeouts, and compensating actions. Do not use distributed database transactions.

Store a service's business tables, outbox rows, inbox rows, and sagas in the same service-owned PostgreSQL database, using separate tables and mappings. This enables one local transaction without creating a shared infrastructure database. Never share those tables between services.

When adding a bounded context, start with Catalog, Inventory, Ordering, Payment, Basket, or Notification according to the actual use case. Add Shipping/Promotion/Review only when requirements justify them. Define event contracts and ownership before implementation.

The checkout workflow is an Ordering-owned orchestration: `OrderSubmitted` asks Inventory to reserve, `InventoryReserved` asks Payment to collect, and Payment success makes Ordering emit both `CommitInventory` and `OrderConfirmed`. Inventory rejection, payment failure, or Saga timeout cancels the order and emits `ReleaseInventory` when a reservation exists. Notification reacts only to terminal order events. Keep every consumer idempotent through the service-local Inbox and publish every resulting cross-service event through the same local transaction and Outbox.

`MSA.Contracts` owns transport-only integration event schemas; it must not contain aggregates or EF models. `MSA.PaymentCore` owns provider-neutral payment ports and Paylinks adapters; payment entities, callback endpoints, persistence, and workflow decisions stay in `MSA.PaymentService`.

Preserve central package management in `Directory.Packages.props`, the .NET SDK pin in `global.json`, and per-project global usings under `src/Shared`. Never commit credentials; use environment variables or user secrets.

Persisted business fields must carry concise Chinese database comments through EF Core `HasComment`. Prioritize identifiers with business meaning, lifecycle/status fields, monetary or quantity fields, snapshots, idempotency/concurrency fields, and audit timestamps; omit comments that merely repeat an obvious CLR/property name. Generate migrations and model snapshots through `dotnet ef` so comments are applied to PostgreSQL, and keep the comment beside the relevant Fluent API mapping.

Add concise Chinese comments to core business code only when they explain intent that the code cannot express by itself: aggregate invariants, state-transition preconditions, snapshot boundaries, idempotency decisions, concurrency locks, eventual-consistency behavior, timeout/retry policy, or compensation rules. Explain why the rule exists rather than narrating individual statements. Do not comment routine CRUD, straightforward validation, framework plumbing, or every branch, and update or remove a comment whenever the associated rule changes.

Before finishing, build `MicroservicesArchitecture.slnx` and report remaining warnings or infrastructure prerequisites separately from compilation failures.
