# Order Management API — Implementation Plan

> **For agentic workers:** Implement task-by-task. Do not start the next task until the current one passes verification. Prefer one Conventional Commit per completed task.  
> **Design spec:** `docs/superpowers/specs/2026-10-13-order-management-design.md`  
> **Requirements source:** `FRD.pdf`

**Goal:** Build a production-minded ASP.NET Core REST prototype for order management with idempotent create, concurrency-safe stock/status updates, structured logging, and concurrent tests.

**Architecture:** Single Web API project with folder layering (`Controllers` → `Services` → `Domain`/`Data`). Reusable `Common` (`Result<T>`, errors) and `Abstractions` (interfaces). Hybrid concurrency: atomic stock SQL, optimistic order locking, unique idempotency key.

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core 10, Npgsql/PostgreSQL, ProblemDetails, xUnit (+ integration tests against PostgreSQL).

## Global Constraints

- Target `net10.0`; EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Do not expose EF entities as API responses; use DTOs.
- Controllers stay thin; business rules in services / domain.
- Async I/O + `CancellationToken` on request/DB paths.
- No `Database.EnsureCreated()` as migration substitute; use EF migrations.
- Never drop/reset DB without explicit approval; never push without authorization.
- Auth is out of scope (FRD does not require it).
- Incremental commits; do not commit secrets or real connection credentials.

## FRD Coverage Map

| FRD requirement | Plan tasks |
|---|---|
| Create Order | Task 12–13 |
| Get / List Orders | Task 14 |
| Update Order Status + transitions | Task 15 |
| Cancel Order + stock restore | Task 16 |
| Products / stock rules | Task 7–8, 13, 16 |
| Idempotency (Skenario C) | Task 11–13, 17 |
| Concurrency A/B/C | Task 13, 15, 17 |
| Extra race conditions (≥2) | Task 18 (README) |
| Consistent errors / status codes | Task 9–10 |
| Correlation ID logging | Task 4, 18 |
| Persistence + migration | Task 6–8 |
| Concurrent test (≥1) | Task 17 |

## Target Solution Layout

```
OrderManagement.sln
src/OrderManagement.API/
  Controllers/
  Contracts/Requests/
  Contracts/Responses/
  Domain/
  Data/
  Services/
  Validators/
  Common/
  Middleware/
  Abstractions/
  Program.cs
  appsettings.json
  appsettings.Development.json
tests/OrderManagement.API.Tests/
docs/
  IMPLEMENTATION_PLAN.md
  superpowers/specs/2026-10-13-order-management-design.md
README.md
```

---

## Task Checklist

### Phase 0 — Alignment

- [x] Task 0: Align implementation plan and design with FRD; lock architecture decisions.

Acceptance criteria: plan and design document FRD endpoints, concurrency, idempotency, and layered structure.

---

### Phase 1 — Project Foundation

- [x] Task 1: Create solution + Web API project (`net10.0`) + test project skeleton.
- [x] Task 2: Add `.gitignore`, basic `README` stub, connection string placeholder (no secrets).
- [x] Task 3: Configure DI baseline, nullable, implicit usings, OpenAPI.
- [x] Task 4: Add health check + correlation ID middleware (`X-Correlation-Id`).
- [x] Task 5: Verify `dotnet build` and app starts locally.

**Verification:** `dotnet build`; run API; `GET /health` (or map equivalent) returns success; correlation ID appears on responses/logs.

**Commit examples:** `chore: initialize .NET API solution`, `feat: add health check and correlation id middleware`

---

### Phase 2 — Persistence

- [x] Task 6: Add EF Core 10 + Npgsql packages; register `AppDbContext`.
- [x] Task 7: Implement entities: `Product`, `Order`, `OrderItem`, `IdempotencyRecord` + status enum.
- [x] Task 8: EF configurations (PKs, FKs, indexes, unique `IdempotencyRecord.Key`, order concurrency token); initial migration; seed sample products for tests.

**Verification:** Migration reviewed; apply to approved **dev** DB only; schema matches entities; seed products visible.

**Commit examples:** `feat: configure PostgreSQL persistence`, `feat: add domain entities and initial migration`

---

### Phase 3 — Reusable Cross-Cutting

- [x] Task 9: Add `Result` / `Error` types and domain exceptions (not found, conflict, validation, business).
- [x] Task 10: Global exception / result → `ProblemDetails` mapping (400/404/409/422).
- [x] Task 11: `IIdempotencyService` + store using unique constraint (Skenario C foundation).

**Verification:** Unit tests for Result mapping / status helper if present; build passes.

**Commit examples:** `feat: add Result and ProblemDetails pipeline`, `feat: add idempotency record service`

---

### Phase 4 — Domain Rules & Order API

- [x] Task 12: Domain status transition rules (valid/invalid matrix from FRD); unit tests.
- [x] Task 13: Create Order — DTOs, validator, service (transaction + atomic stock deduct + idempotency), `POST /api/orders`.
- [x] Task 14: Get Order + List Orders (filters + pagination), read-only `AsNoTracking` where appropriate.
- [x] Task 15: Update Order Status — optimistic concurrency; invalid transition → 409/422 as designed; Skenario B.
- [x] Task 16: Cancel Order — Pending/Confirmed only; restore stock atomically; terminal rejection.

**Create Order acceptance:** insufficient stock → conflict/error; successful create decreases stock; same `Idempotency-Key` returns same outcome without double stock deduct.

**Status/Cancel acceptance:** only FRD transitions allowed; concurrent status updates produce one winner + clear 409; cancel restores stock and never leaves stock negative.

**Commit examples:** `feat: add order status transition rules`, `feat: add create order with atomic stock`, `feat: add get and list orders`, `feat: add update order status with optimistic lock`, `feat: add cancel order with stock restore`

---

### Phase 5 — Quality, Concurrency Tests, Docs

- [x] Task 17: Unit + integration tests; **mandatory** concurrent coverage for Skenario A, B, and C (at least one required by FRD; implement all three).
- [x] Task 18: README — idempotency justification, concurrency strategy, ≥2 extra race conditions, local setup, env vars, migration commands, example requests.
- [x] Task 19: Final verification — full test suite, secret/config review, update this plan’s Progress Log.

**Verification:** `dotnet test` green; concurrent tests demonstrate correct behavior under parallel load.

**Commit examples:** `test: add concurrent stock status and idempotency tests`, `docs: document setup concurrency and race conditions`

---

## Reusable Building Blocks (implement once, reuse)

| Component | Responsibility |
|---|---|
| `Result` / `Error` | Uniform success/failure without throwing for expected paths |
| `OrderStatusTransitions` | Single source of truth for allowed transitions |
| `IStockService` / stock SQL helper | Atomic deduct & restore (`WHERE StockQuantity >= @qty`) |
| `IIdempotencyService` | Begin/complete/replay by key + request hash |
| Correlation middleware | Ensure every request is traceable |
| ProblemDetails factory | One error response shape for all endpoints |

---

## Progress Log

### Task 1 — Solution + Web API + test skeleton

- **Status:** `[x]` Completed
- **Files changed:** `OrderManagement.sln`, `src/OrderManagement.API/` (project, `Program.cs`, appsettings, launchSettings), `tests/OrderManagement.API.Tests/`
- **Verification:** `dotnet build` on solution
- **Commit:** `fd4c120` — `chore: initialize .NET API solution`
- **Notes:** Preceded on branch by `f406d18` (ignore `.superpowers/sdd/` scratch). Targets `net10.0`.

### Task 2 — README, gitignore, connection placeholder

- **Status:** `[x]` Completed
- **Files changed:** `.gitignore`, `README.md`, `appsettings.Development.json` (placeholder connection string, no secrets)
- **Verification:** Manual review; build still passes
- **Commit:** `d1ff271` — `docs: add README stub and connection string placeholder`

### Task 3 — DI baseline and layered scaffold

- **Status:** `[x]` Completed
- **Files changed:** Layer folders under `src/OrderManagement.API/` (`Controllers`, `Services`, `Data`, etc.), `Program.cs` (OpenAPI/DI baseline)
- **Verification:** `dotnet build`
- **Commit:** `09c7191` — `chore: configure DI baseline and layered folder scaffold`

### Task 0 — Align plan with FRD

- **Status:** `[x]` Completed
- **Files changed:** `docs/IMPLEMENTATION_PLAN.md`, `docs/superpowers/specs/2026-10-13-order-management-design.md`
- **Verification:** Manual review against FRD functional + non-functional requirements
- **Commit:** `136deca` — `docs: align implementation plan and design with FRD`
- **Notes:** Decisions locked — single-project layering; Idempotency-Key; hybrid concurrency; PostgreSQL. `.gitignore` fixed so `docs/` is tracked (FRD.pdf remains ignored as local binary).

### Task 4 — Health check + correlation ID middleware

- **Status:** `[x]` Completed
- **Files changed:** `Program.cs`, `Middleware/CorrelationIdMiddleware.cs`, health/correlation WebApplicationFactory tests, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet build`; `dotnet test` filter `HealthAndCorrelationIdTests` — 3 passed (GET `/health` 200; generated `X-Correlation-Id`; client-supplied ID preserved)
- **Commit:** `6a9381c` — `feat: add health check and correlation id middleware`
- **Notes:** Correlation middleware runs first in the pipeline and pushes `CorrelationId` into the logger scope. `public partial class Program` exposes the entry point for `WebApplicationFactory`. No EF.

### Task 5 — Verify build and startup

- **Status:** `[x]` Completed
- **Files changed:** `docs/IMPLEMENTATION_PLAN.md` (report: `.superpowers/sdd/reports/task-5-report.md`, local)
- **Verification:** `dotnet build` succeeded (NU1903 advisory on `Microsoft.OpenApi` 2.0.0 noted); `dotnet test` — 4 passed, 0 failed; `HealthAndCorrelationIdTests` — GET `/health` 200, `X-Correlation-Id` generated and client-supplied ID preserved (WebApplicationFactory)
- **Commit:** `9f7e86e` — `docs: mark phase 1 foundation tasks complete`
- **Notes:** Phase 1 foundation complete; Phase 2 (EF) not started.

### Task 7 — Domain entities + status enum + DbSets

- **Status:** `[x]` Completed
- **Files changed:** `Domain/OrderStatus.cs`, `Domain/Product.cs`, `Domain/Order.cs`, `Domain/OrderItem.cs`, `Domain/IdempotencyRecord.cs`, `Data/AppDbContext.cs`, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet build` on solution
- **Commit:** `feat: add domain entities for orders products and idempotency`
- **Notes:** Clean POCOs under `Domain/`; DbSets for Products, Orders, OrderItems, IdempotencyRecords. No Fluent configs or migrations (Task 8). `Order.RowVersion` is `uint` so Task 8 can map it with `IsRowVersion()` to PostgreSQL `xmin`.

### Task 8 — Fluent configs, initial migration, product seed

- **Status:** `[x]` Completed (apply blocked: local Postgres password is still `CHANGE_ME`)
- **Files changed:** `Data/Configurations/*`, `Data/ProductSeed.cs`, `Data/AppDbContext.cs`, `Data/Migrations/20261013080842_InitialCreate*`, `Data/Migrations/AppDbContextModelSnapshot.cs`, `.config/dotnet-tools.json`, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet build` succeeded; `dotnet ef migrations add InitialCreate`; SQL reviewed (`xmin` not added as a user column; `IdempotencyRecords` PK=`Key`; 3 seed products including stock 15). `dotnet ef database update` failed: `28P01 password authentication failed for user "postgres"`.
- **Commit:** `feat: add initial migration with concurrency and seed products`
- **Notes:** Concurrency API: `builder.Property(o => o.RowVersion).IsRowVersion()` (Npgsql 10 maps `uint` rowversion → PostgreSQL `xmin`/`xid`). `UseXminAsConcurrencyToken()` is obsolete/removed. Apply locally: set a real password (or `ConnectionStrings__DefaultConnection`), then `dotnet ef database update --project src/OrderManagement.API/OrderManagement.API.csproj`. Do not use `EnsureCreated` or drop the database.

### Task 10 — ProblemDetails error mapping pipeline

- **Status:** `[x]` Completed
- **Files changed:** `Common/ProblemDetailsMapper.cs`, `Common/ResultHttpResultExtensions.cs`, `Middleware/DomainExceptionHandler.cs`, `Middleware/CorrelationIdMiddleware.cs`, `Program.cs`, mapper + pipeline tests, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet build`; `dotnet test` — 28 passed (13 mapper/Result→Problem, 6 IExceptionHandler pipeline including 500 leak-safety + correlation on errors)
- **Commit:** `feat: add ProblemDetails error mapping pipeline`
- **Notes:** `IExceptionHandler` maps domain exceptions to 400/404/409/422 and unhandled to 500 without stack/secrets. Correlation header is applied in `OnStarting` so it survives `ExceptionHandlerMiddleware` clearing the response. Domain `ValidationException` aliased vs BCL.

### Task 11 — IIdempotencyService + store

- **Status:** `[x]` Completed
- **Files changed:** `Abstractions/IIdempotencyService.cs`, `Abstractions/IdempotencyBeginResult.cs`, `Services/IdempotencyService.cs`, `Services/IdempotencyDecision.cs`, `Data/PostgresUniqueViolation.cs`, `Program.cs` (scoped DI), decision + 23505 unit tests, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet test` — 37 passed (4 decision, 5 unique-violation helper, existing)
- **Commit:** `feat: add idempotency record service`
- **Notes:** Begin insert-or-get via PK unique constraint; catch Postgres `23505`. Hash mismatch → `Conflict(HashMismatch)` (422 at Create Order). Matching hash + stored status → Replay. Matching hash + in-flight (status 0) → `Conflict(InFlight)` retryable. Create Order not wired (Task 13). Concurrent Scenario C tests deferred to Task 17 (real PostgreSQL).

### Task 13 — Create Order (atomic stock + idempotency)

- **Status:** `[x]` Completed (live PostgreSQL create/concurrency deferred to Task 17)
- **Files changed:** `Controllers/OrdersController.cs`, `Contracts/Requests|Responses`, `Validators/CreateOrderRequestValidator.cs`, `Services/OrderService.cs`, `Services/StockService.cs`, `Services/RequestHash.cs`, `Abstractions/IOrderService.cs`, `Abstractions/IStockService.cs`, `Program.cs` (`AddControllers`/`MapControllers`; weather sample removed), create-order tests, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet test` — 101 passed after review fixes (validator, request hash, OrderService with fakes + EF InMemory, idempotency abandon, controller ProblemDetails mapping)
- **Commit:** `feat: add create order with atomic stock`
- **Follow-up commit:** `fix: harden create-order rollback and idempotency abandon`
- **Notes:** Missing `Idempotency-Key` → 400 (also length > 256). ShippingAddress max 500 → 400. Hash mismatch → 422 (`business_rule`). In-flight → **409** (retryable). Insufficient stock → 409. Replay of stored **201** returns cached body; non-201 stored status maps to a failure Result (honors stored status). Failed create: rollback with `CancellationToken.None`, `ChangeTracker.Clear()`, then `IIdempotencyService.AbandonAsync` with `None` so an Added Order cannot persist without stock deduct. Atomic SQL + Scenario A concurrency need real PostgreSQL (Task 17).

### Task 14 — Get Order + List Orders

- **Status:** `[x]` Completed
- **Files changed:** `Controllers/OrdersController.cs`, `Services/OrderService.cs`, `Abstractions/IOrderService.cs`, `Contracts/Requests/ListOrdersQuery.cs`, `Contracts/Responses/PagedResponse.cs`, get/list controller + service tests, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet test` — 111 passed (GET 200/404; list filters + pagination + `AsNoTracking`; page/pageSize validation)
- **Commit:** `feat: add get and list orders`
- **Notes:** `GET /api/orders/{id}` returns `OrderResponse` with items (404 if missing). `GET /api/orders` filters `status`, `customerId`, `from`/`to` (inclusive `CreatedAt`), `page` default 1, `pageSize` default 20 max 100 (invalid → 400). Paged body `{ items, page, pageSize, totalCount }`. Reads use `AsNoTracking`; DTOs only. List ordered by `CreatedAt` desc, then `Id`. Status/cancel not in this task.

### Task 15 — Update Order Status (optimistic concurrency)

- **Status:** `[x]` Completed (live Scenario B concurrency deferred to Task 17)
- **Files changed:** `Controllers/OrdersController.cs`, `Services/OrderService.cs`, `Abstractions/IOrderService.cs`, `Contracts/Requests/UpdateOrderStatusRequest.cs`, status controller + service tests, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet test` — 127 passed (illegal transition → 422 `business_rule`; missing → 404; success Pending→Confirmed; `DbUpdateConcurrencyException` → 409)
- **Commit:** `feat: add update order status with optimistic lock`
- **Follow-up commit:** `fix: reject cancel via PATCH status`
- **Notes:** `PATCH /api/orders/{id}/status` body `{ "status": "Confirmed" }` (string enum). Loads tracked order (no `AsNoTracking`) so `RowVersion`/xmin can conflict. `OrderStatusTransitions.CanTransition` gates updates; terminal Delivered/Cancelled cannot change. Illegal transition → 422. `DbUpdateConcurrencyException` → 409. PATCH to `Cancelled` is rejected (422) so stock restore stays on `POST /cancel` (Task 16). Concurrent Scenario B against real PostgreSQL deferred to Task 17.

### Task 16 — Cancel Order + stock restore

- **Status:** `[x]` Completed (live cancel-vs-status race deferred to Task 17)
- **Files changed:** `Controllers/OrdersController.cs`, `Services/OrderService.cs`, `Abstractions/IOrderService.cs`, cancel controller + service tests, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet test` — 142 passed (Pending/Confirmed cancel restores per item; already cancelled → 409; Shipped/Delivered → 422; missing → 404; `DbUpdateConcurrencyException` → 409)
- **Commit:** `feat: add cancel order with stock restore`
- **Notes:** `POST /api/orders/{id}/cancel`. Already Cancelled → **409** (no second restore). Not Pending/Confirmed → 422. Transaction wraps `IStockService.RestoreAsync` (`UPDATE … SET StockQuantity = StockQuantity + @qty`) then status Cancelled + `UpdatedAt` + SaveChanges. Restore only adds so stock cannot go negative. Concurrent cancel vs status (xmin) deferred to Task 17.

### Task 17 — Concurrent integration tests (Scenarios A, B, C)

- **Status:** `[x]` Completed
- **Files changed:** `tests/OrderManagement.API.Tests/PostgresApiFactory.cs`, `tests/OrderManagement.API.Tests/Concurrency/ConcurrentOrderTests.cs`, `tests/OrderManagement.API.Tests/OrderManagement.API.Tests.csproj`, `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet test` with `ConnectionStrings__DefaultConnection` → dedicated DB `order_management_tests` — 145 passed, 0 skipped (A/B/C all green on real PostgreSQL). Docker CLI not installed; Testcontainers not added.
- **Commit:** `test: add concurrent stock status and idempotency tests`
- **Notes:** Fixture applies EF migrations; `[PostgresFact]` skips with a clear reason if Postgres is unreachable. A: two qty-10 creates vs stock 15 → at most one 201, stock never negative. B: two parallel PATCH Confirmed→Shipped → one 200, other 409/422, final Shipped. C: same Idempotency-Key race → exactly one order; 409 in-flight retried to replay. PATCH Cancelled remains 422 so B uses two Shipped PATCHes (xmin). Tests never drop unrelated databases. **Review:** Approved — no Critical/Important issues.

### Task 18 — README (setup, concurrency, races)

- **Status:** `[x]` Completed
- **Files changed:** `README.md`
- **Verification:** Manual checklist vs brief — overview, why PostgreSQL, idempotency unique-key justification, A/B/C mapping, extra races (cancel vs status, double cancel, hash mismatch, abandon-after-failure), local setup (`dotnet ef`, env override, `dotnet test`), curl examples, `X-Correlation-Id`.
- **Commit:** `1779796` — `docs: document setup concurrency and race conditions`
- **Notes:** No real secrets in README (placeholders only). Extra races documented rather than additional live tests.

### Task 19 — Final verification

- **Status:** `[x]` Completed
- **Files changed:** `docs/IMPLEMENTATION_PLAN.md`
- **Verification:** `dotnet build` succeeded (NU1903 advisory on `Microsoft.OpenApi` 2.0.0). `dotnet test` with env `ConnectionStrings__DefaultConnection` → dedicated DB `order_management_tests` — **145 passed, 0 failed, 0 skipped**. Secret/config review: only `Password=CHANGE_ME` placeholders in `appsettings.Development.json` and test fallback; `.gitignore` excludes `.env` / `*.local.json`; no `.pem`/`.pfx`/user-secrets in git.
- **Commit:** `docs: finalize implementation plan verification`
- **Notes:** Tasks 1–19 marked complete (including previously unchecked Task 6 and Task 12 checkboxes). Not pushed; not merged to master.

---

## Known Issues / Open Items

- Local `appsettings.Development.json` still uses `Password=CHANGE_ME`. Runtime and concurrent tests need `ConnectionStrings__DefaultConnection` or `TEST_DATABASE_URL` (tests force database `order_management_tests`). Do not commit a real password.
- Docker CLI is not installed, so Testcontainers is not used; `[PostgresFact]` skips if PostgreSQL is unreachable.
- Product admin CRUD endpoints are optional and out of scope; seed products cover demos and concurrent tests.
- Extra races (cancel vs status, double cancel) are documented in README and covered by unit tests / xmin, not additional live parallel tests.
- `Microsoft.OpenApi` 2.0.0 (transitive via `Microsoft.AspNetCore.OpenApi` 10.0.9) has advisory NU1903; not upgraded in this prototype.
- Authentication / authorization remains out of scope (FRD does not require it).
- Dev database `order_management` is not auto-migrated; apply with `dotnet ef database update` after setting a local password.
