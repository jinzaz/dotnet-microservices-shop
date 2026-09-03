---
name: repositories
description: Implement or review EF Core/PostgreSQL repositories, unit-of-work boundaries, mappings, concurrency, and service-owned data access.
---

# Persistence rules

Repositories operate on aggregate roots, not arbitrary tables, and must not leak `IQueryable` across application boundaries. Use one DbContext/transaction per command. Queries may use dedicated read models and `AsNoTracking`.

Keep EF mappings outside domain entities where practical. Configure keys, precision, constraints, indexes, UTC timestamps, and optimistic concurrency explicitly. Generate migrations in the owning service; never create a shared cross-service schema or repository.

Avoid generic repositories that merely duplicate DbSet. Prefer intent-revealing repository methods and cancellation-aware async APIs. Diagnose query shape before adding caching; Redis entries require an owner, TTL, and invalidation strategy.

Do not require business services to reference `MSA.Repositories`. Keep aggregate-specific repository interfaces in each bounded context's Domain/Application layer and EF implementations in that service's Infrastructure layer. Retain this library only for genuinely reusable technical helpers such as specifications, pagination, interceptors, or mapping conventions; remove it if no such shared implementation emerges.
