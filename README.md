# Order Management API

Prototype ASP.NET Core REST API for order management (FRD assignment). It creates orders with atomic stock deduction, lists and fetches orders, updates status with optimistic concurrency, and cancels with stock restore. Create is idempotent via `Idempotency-Key`. Requests are traceable with `X-Correlation-Id`.

**Stack:** .NET 10 / ASP.NET Core Web API, EF Core 10, PostgreSQL (Npgsql), ProblemDetails, xUnit.

Auth is out of scope (not required by the FRD).

## Why PostgreSQL

PostgreSQL is the persistence choice because the prototype needs **database-enforced correctness under concurrent load**, not just application-level checks:

| Need | PostgreSQL capability |
|---|---|
| Concurrent stock (Scenario A) | Atomic `UPDATE … WHERE StockQuantity >= @qty` — row-level locks, one winner, never negative |
| Concurrent status (Scenario B) | System column `xmin` as EF Core rowversion — lost updates surface as `DbUpdateConcurrencyException` |
| Idempotent create race (Scenario C) | Unique primary key on `IdempotencyRecords.Key` (`23505`) — insert-or-replay, no double order |
| Transactions | Stock deduct/restore + order write + idempotency complete commit or roll back together |
| Production-shaped prototype | Constraints, indexes, and migrations match how this would ship |

EF Core InMemory is used only for unit tests. It does not reproduce unique violations, `xmin`, or atomic `UPDATE` predicates. Concurrent A/B/C tests run against a real PostgreSQL database (`order_management_tests`).

## Idempotency

`POST /api/orders` **requires** the `Idempotency-Key` header (1–256 characters).

**Why a unique constraint, not a cache:** retries and double-submits must be safe across process restarts and multiple app instances. A Redis/memory cache can expire or disagree between nodes. A uniqueness constraint on `IdempotencyRecords` (`CustomerId`, `Key`) is the source of truth.

**Keys are scoped per customer**, so one customer cannot block another by claiming a key first. `CustomerId` comes from the request body and auth is out of scope, so this is not caller identity: anyone who knows a customer's id can still use that customer's key space.

Flow:

1. Hash the request body (`CustomerId`, `ShippingAddress`, items).
2. Begin a transaction and `INSERT` a pending idempotency row (`ResponseStatus = 0`) inside it.
3. On success, continue: deduct stock, insert the order, store the 201 body, commit.
4. A concurrent request with the same key waits on the unique key until the first one finishes. If that one committed, the insert fails with `23505` and the existing row is loaded:
   - **Same hash + completed 201** → replay the stored body (no second stock deduct).
   - **Different hash** → **422** `business_rule` (key reused with a different body).

If the first request rolled back, the waiting request's insert succeeds and it creates the order itself.

The key commits with the order or not at all, so a failed create or a process crash leaves nothing behind and the same key can be retried.

## Concurrency (FRD scenarios A, B, C)

Hybrid strategy: **atomic SQL for stock**, **optimistic lock for order status**, **unique key for idempotency**.

### A — Concurrent stock

Two creates that each need 10 units of a product with stock 15 must not both succeed.

- Deduct uses `UPDATE "Products" SET "StockQuantity" = "StockQuantity" - @qty WHERE "Id" = @id AND "StockQuantity" >= @qty`.
- Zero rows updated → **409** insufficient stock.
- Combined qty 20 > 15 ⇒ at most one **201**; remaining stock is 5 or 15 (if both fail, which is allowed only when the total would exceed). Stock never goes negative.

Covered by `ConcurrentOrderTests.ScenarioA_ConcurrentStock_AtMostOneSuccessAndNeverNegative`.

### B — Concurrent status

Two admins PATCH the same order at once.

- `Order.RowVersion` maps to PostgreSQL `xmin` (`IsRowVersion()`).
- First `SaveChanges` wins (**200**). The loser gets `DbUpdateConcurrencyException` → **409**, or **422** if it reloads after the winner already moved status (illegal transition).
- PATCH to `Cancelled` is rejected (**422**); cancel is `POST /api/orders/{id}/cancel` so stock restore cannot be skipped.

Covered by `ConcurrentOrderTests.ScenarioB_ConcurrentStatus_OneWinnerAndConflict` (two parallel Confirmed → Shipped PATCHes).

### C — Idempotent create race

Two parallel POSTs with the **same** `Idempotency-Key` and the same body.

- Unique (`CustomerId`, `Key`) ⇒ exactly one order row.
- The other request waits, then **replays** the same 201.
- Stock is deducted once.

Covered by `ConcurrentOrderTests.ScenarioC_IdempotentCreateRace_ExactlyOneOrder`.

## Additional race conditions

Beyond A/B/C, these races are identified and closed in code:

1. **Cancel vs status PATCH** — Confirmed order: one request ships, another cancels. Both load the order and try to write. `xmin` makes one `SaveChanges` fail → **409**. Cancel wraps restore + status in a transaction, so a lost cancel does not restore stock without flipping status (and a lost ship does not leave status Shipped after a successful cancel).
2. **Double cancel** — First cancel restores stock and sets `Cancelled`. Second cancel sees `Cancelled` and returns **409** without restoring again (no double credit).
3. **Cancel after Shipped/Delivered** — Status gate: only Pending/Confirmed can cancel. Shipped/Delivered → **422**; stock is not restored after the order has left the warehouse path.
4. **Same `Idempotency-Key`, different body** — Stored `RequestHash` mismatch → **422**. Prevents attaching a new payload to a key that already created (or is creating) a different order.
5. **Create failure or crash after pending idempotency insert** — The insert is part of the order transaction, so rollback (or the database, after a crash) discards it and a retry with the same key starts clean.
6. **Multi-item creates in opposite order** — Create and cancel both touch products sorted by `ProductId`, so concurrent multi-item writes take row locks in one order and cannot deadlock.

`Cancel_LosingTheRace_RollsBackRestoredStock` covers the rollback in (1) on PostgreSQL. A live test for (2) is not in the suite; it follows from the cancel status gate, which is covered by unit tests.

## Correlation ID and structured logging

Every response includes `X-Correlation-Id`.

- Send your own value to correlate a client retry chain; it is echoed if it is at most 64 characters from `A-Za-z0-9._:-`, otherwise it is replaced with a generated GUID.
- If omitted, the API generates a GUID.
- The middleware runs first and sets the header in `OnStarting` so it survives exception handling.
- The ID is pushed into the logger scope (`CorrelationId`) for structured logs. Do not use `Console.WriteLine` for tracing.

Business operations also emit structured logs (via `ILogger`, message templates) so ops can trace outcomes alongside the correlation ID:

| Area | Examples |
|---|---|
| Create order | created / validation failure / idempotency replay / hash mismatch / in-flight / insufficient stock |
| Stock | deduct success/failure, restore on cancel |
| Status update | transition success, illegal transition, concurrency conflict |
| Cancel | success with item count, already cancelled, illegal state, concurrency conflict |
| Idempotency store | begin created, existing-key outcome, complete, abandon |

Log properties include `OrderId`, `CustomerId`, `IdempotencyKey`, `ProductId`, `FromStatus`, `ToStatus`, etc., so they can be queried in a log aggregator.

## Local setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 15+ listening locally (default `localhost:5432`)
- `dotnet-ef` via the repo tool manifest (`.config/dotnet-tools.json`)

### Connection string (no secrets in git)

`src/OrderManagement.API/appsettings.Development.json` has a **placeholder**:

```text
Host=localhost;Port=5432;Database=order_management;Username=postgres;Password=CHANGE_ME
```

Do **not** commit a real password. Replace `CHANGE_ME` locally via environment variable (recommended) before migrate/run:

**Bash / Git Bash**

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=order_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
```

**PowerShell**

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=order_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
```

**CMD**

```bat
set ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=order_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD
```

### Initialize database (EF migrations)

This creates the `order_management` database (if missing), applies `InitialCreate`, and seeds products.

```bash
# from repo root
dotnet tool restore

dotnet ef database update \
  --project src/OrderManagement.API/OrderManagement.API.csproj \
  --startup-project src/OrderManagement.API/OrderManagement.API.csproj
```

Set `ConnectionStrings__DefaultConnection` first (see above). Useful checks:

```bash
# list migrations
dotnet ef migrations list \
  --project src/OrderManagement.API/OrderManagement.API.csproj \
  --startup-project src/OrderManagement.API/OrderManagement.API.csproj

# generate SQL without applying (optional review)
dotnet ef migrations script \
  --project src/OrderManagement.API/OrderManagement.API.csproj \
  --startup-project src/OrderManagement.API/OrderManagement.API.csproj \
  --output migration.sql
```

If auth fails (`28P01`), the password in the connection string is wrong — fix the env var (do not commit secrets).

### Run the API

```bash
dotnet run --project src/OrderManagement.API
# or press F5 in Cursor (profile: OrderManagement.API (Swagger))
```

HTTP profile: `http://localhost:5196`. HTTPS: `https://localhost:7265`.

| Endpoint | Notes |
|---|---|
| `GET /health` | Liveness |
| `GET /swagger` | Swagger UI (Development) |
| `GET /openapi/v1.json` | OpenAPI document (Development) |

Seeded products (after migrate):

| Id | Name | Stock |
|---|---|---|
| `aaaaaaaa-0001-4000-8000-000000000001` | Scenario A Widget | 15 |
| `aaaaaaaa-0001-4000-8000-000000000002` | Standard Gadget | 50 |
| `aaaaaaaa-0001-4000-8000-000000000003` | Low Stock Accessory | 5 |

### Build and test

```bash
dotnet build
dotnet test
```

Concurrent tests need a reachable PostgreSQL. They **never** drop other databases. They create/use `order_management_tests` and apply migrations.

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
# or TEST_DATABASE_URL with the same shape
dotnet test
```

If PostgreSQL is unreachable, `[PostgresFact]` **skips** with a clear message (unit tests still run). Docker/Testcontainers is not required.

## Example requests

Base URL below is the HTTP launch profile. For a runnable walkthrough (create, get, list, confirm, cancel, plus error cases) open `src/OrderManagement.API/OrderManagement.API.http` in VS Code (REST Client), Rider or Visual Studio, or use Swagger UI at `/swagger`, where the request bodies are prefilled with seeded ids.

### Create order

```bash
curl -sS -D - http://localhost:5196/api/orders \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-create-1" \
  -H "X-Correlation-Id: demo-trace-1" \
  -d '{
    "customerId": "bbbbbbbb-0001-4000-8000-000000000001",
    "shippingAddress": "Jl. Sudirman 1",
    "items": [
      {
        "productId": "aaaaaaaa-0001-4000-8000-000000000002",
        "quantity": 2
      }
    ]
  }'
```

Expect **201** and `X-Correlation-Id: demo-trace-1`. Repeat the same key and body → same order (replay). Missing key → **400**. Same key, different body → **422**. Insufficient stock → **409**.

### Get order

```bash
curl -sS http://localhost:5196/api/orders/ORDER_ID
```

Missing → **404**.

### List orders

```bash
curl -sS "http://localhost:5196/api/orders?status=Pending&page=1&pageSize=20"
```

Optional filters: `customerId`, `from`, `to` (inclusive `CreatedAt`). `page` default 1; `pageSize` default 20, max 100. Invalid page → **400**. Body: `{ items, page, pageSize, totalCount }`.

### Update status

```bash
curl -sS -X PATCH http://localhost:5196/api/orders/ORDER_ID/status \
  -H "Content-Type: application/json" \
  -d '{"status":"Confirmed"}'
```

Allowed: Pending → Confirmed; Confirmed → Shipped; Shipped → Delivered. Illegal transition → **422**. Concurrent lost update → **409**. Do not PATCH `Cancelled`.

### Cancel order

```bash
curl -sS -X POST http://localhost:5196/api/orders/ORDER_ID/cancel
```

Pending or Confirmed → **200**, stock restored. Already cancelled → **409**. Shipped/Delivered → **422**. Missing → **404**.

### Error shape

Every error is `application/problem+json` with `type`, `title`, `status`, `detail`, and an extra `code` (`validation`, `not_found`, `conflict`, `business_rule`, `payload_too_large`, `bad_request`, `rate_limited`, `internal_error`). Unhandled exceptions return **500** without stack traces or secrets.

Limits: an order may have at most 40 items, each with quantity up to 1000 (**400**), and a request body may be at most 64 KB on Kestrel (**413**).

Rate limit: `/api/*` allows 120 requests per minute per client IP (`RateLimiting:PermitLimit` and `RateLimiting:WindowSeconds`), then returns **429** with `Retry-After`. Counters are per app instance. Behind a reverse proxy all clients share one address until forwarded headers are configured.

`validation` (**400**) errors, including malformed JSON, wrong types, unknown enum names and bad query values, also carry `errors`: a map of field name to messages, reported all at once.

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "Bad Request",
  "status": 400,
  "detail": "Request validation failed: ...",
  "code": "validation",
  "errors": { "customerId": ["CustomerId is required."], "items[0].quantity": ["Item quantity must be greater than zero."] }
}
```

Branch on `code` (or `status`), not on `detail` wording. Malformed GUIDs in the path (`/api/orders/not-a-guid`) return **404**.

## Project layout

```
OrderManagement.sln
src/OrderManagement.API/     # Web API (Controllers, Services, Domain, Data, Middleware)
tests/OrderManagement.API.Tests/
```
