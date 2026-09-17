# Commerce service test plan

1. Create one `tests/MSA.CommerceTests` MSTest project referencing the four requested services and register it in `MicroservicesArchitecture.slnx`.
2. Add a minimal in-memory endpoint harness: map existing endpoint classes into an unstarted `WebApplication`, inspect route/method/authorization metadata, and invoke request delegates with mocked `ISender` where practical. MVC controller methods are invoked directly with a claims principal.
3. Add focused domain tests: `CatalogProductTests`, `OrderAggregateTests`, `InventoryDomainTests`, and `BasketDomainTests`. Cover successful transitions, stale state, invalid boundaries, and idempotent/no-op behavior.
4. Add API/admin contract tests: `CatalogEndpointTests`, `OrderControllerTests`, `InventoryEndpointTests`, and `BasketEndpointTests`. Basket's lack of admin API is an explicit assertion.
5. Run `dotnet test tests/MSA.CommerceTests/MSA.CommerceTests.csproj`, solution-level test discovery/run, and a non-incremental solution build. Run assertion-quality and gap review; record findings in `status.md`.

| Requirement | Planned evidence |
| --- | --- |
| Catalog business and API/admin | `CatalogProductTests`, `CatalogEndpointTests` |
| Order business and API/admin | `OrderAggregateTests`, `OrderControllerTests` |
| Inventory business and API/admin | `InventoryDomainTests`, `InventoryEndpointTests` |
| Basket business and API/admin scope | `BasketDomainTests`, `BasketEndpointTests` (API plus verified absence of admin routes) |
| Acceptance | Passing targeted and solution-level `dotnet test` output, plus solution build |
