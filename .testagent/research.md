# Commerce service unit-test research

- Scope: Catalog, Order, Inventory, and Basket business services; both customer API and existing admin API surfaces.
- Repository: SDK-style .NET 10 solution `MicroservicesArchitecture.slnx`, centrally managed packages, MSTest 4.0.2 and Moq 4.18.4. The only existing project, `MSA.FlashSaleTests`, targets legacy flash-sale code and has no active representative endpoint suite.
- API shapes: Catalog, Inventory, and Basket use minimal-API endpoint maps; Order uses MVC controllers. Catalog/Inventory have separate `*-api`, `*.admin.read`, and `*.admin.write` policies. Order uses the same split. Basket exposes only `basket-api`; it has no admin endpoints to invoke.
- Static source-to-test pairing: Roslyn analyzer reports 296 source files, 4 test files, 0 paired files before this work. This is symbol pairing, not executed code coverage; the legacy tests are mostly commented out.
- Test boundary: no PostgreSQL, Redis, RabbitMQ, Identity, network listener, or real clock. Mock MediatR `ISender` at API boundaries and exercise domain entities directly.

## Acceptance checklist

1. Catalog: product variant/version/revision and lifecycle invariants; customer and admin route-policy contracts; API response/command translation for representative read and write paths.
2. Order: order price/fulfillment/idempotency rules; customer create/list/read behavior; admin read and fulfillment command translation; route-policy contracts.
3. Inventory: stock balance/reservation/allocation invariants; customer and admin route-policy contracts; API response/command translation for representative read and write paths.
4. Basket: quantity/version/coupon and stale-snapshot invariants; customer route-policy contracts; ETag/If-Match command translation. Basket has no admin endpoint; assert that no admin route is registered rather than inventing one.
5. Register one new test project in the exact solution, run targeted tests and solution-level validation, and review assertions for false-positive coverage.
