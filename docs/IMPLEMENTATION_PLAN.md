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

- [ ] Task 6: Add EF Core 10 + Npgsql packages; register `AppDbContext`.
- [ ] Task 7: Implement entities: `Product`, `Order`, `OrderItem`, `IdempotencyRecord` + status enum.
- [ ] Task 8: EF configurations (PKs, FKs, indexes, unique `IdempotencyRecord.Key`, order concurrency token); initial migration; seed sample products for tests.

**Verification:** Migration reviewed; apply to approved **dev** DB only; schema matches entities; seed products visible.

**Commit examples:** `feat: configure PostgreSQL persistence`, `feat: add domain entities and initial migration`

---

### Phase 3 — Reusable Cross-Cutting

- [ ] Task 9: Add `Result` / `Error` types and domain exceptions (not found, conflict, validation, business).
- [ ] Task 10: Global exception / result → `ProblemDetails` mapping (400/404/409/422).
- [ ] Task 11: `IIdempotencyService` + store using unique constraint (Skenario C foundation).

**Verification:** Unit tests for Result mapping / status helper if present; build passes.

**Commit examples:** `feat: add Result and ProblemDetails pipeline`, `feat: add idempotency record service`

---

### Phase 4 — Domain Rules & Order API

- [ ] Task 12: Domain status transition rules (valid/invalid matrix from FRD); unit tests.
- [ ] Task 13: Create Order — DTOs, validator, service (transaction + atomic stock deduct + idempotency), `POST /api/orders`.
- [ ] Task 14: Get Order + List Orders (filters + pagination), read-only `AsNoTracking` where appropriate.
- [ ] Task 15: Update Order Status — optimistic concurrency; invalid transition → 409/422 as designed; Skenario B.
- [ ] Task 16: Cancel Order — Pending/Confirmed only; restore stock atomically; terminal rejection.

**Create Order acceptance:** insufficient stock → conflict/error; successful create decreases stock; same `Idempotency-Key` returns same outcome without double stock deduct.

**Status/Cancel acceptance:** only FRD transitions allowed; concurrent status updates produce one winner + clear 409; cancel restores stock and never leaves stock negative.

**Commit examples:** `feat: add order status transition rules`, `feat: add create order with atomic stock`, `feat: add get and list orders`, `feat: add update order status with optimistic lock`, `feat: add cancel order with stock restore`

---

### Phase 5 — Quality, Concurrency Tests, Docs

- [ ] Task 17: Unit + integration tests; **mandatory** concurrent coverage for Skenario A, B, and C (at least one required by FRD; implement all three).
- [ ] Task 18: README — idempotency justification, concurrency strategy, ≥2 extra race conditions, local setup, env vars, migration commands, example requests.
- [ ] Task 19: Final verification — full test suite, secret/config review, update this plan’s Progress Log.

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
- **Commit:** _(see Task 5 commit on branch)_
- **Notes:** Phase 1 foundation complete; Phase 2 (EF) not started.

---

## Known Issues / Open Items

- Dev PostgreSQL connection string and whether Docker is available for integration tests — confirm at Task 6/17.
- Product admin CRUD endpoints are optional; seed + test helpers may be enough unless needed for demos.
