# Order Management API — Design Spec

**Date:** 2026-10-13  
**Source:** FRD.pdf (PT Adaya Solusi Teknologi — Senior .NET Developer assignment)  
**Status:** Approved

## Problem

Prototype REST API for order management that fixes:

1. Double orders from impatient double-submit / retries  
2. Negative stock under concurrent create  
3. Inconsistent status updates under concurrent admin updates  
4. Weak production tracing / logging  

## Decisions (approved)

| Topic | Choice |
|---|---|
| Project shape | Single ASP.NET Core Web API project, folder-layered |
| Database | PostgreSQL (EF Core 10) |
| Idempotency | `Idempotency-Key` header + `IdempotencyRecords` unique constraint |
| Concurrency | Hybrid: atomic stock update; optimistic lock on order status; unique key for idempotency |
| Architecture style | Layered + clean code + reusable `Common` / `Abstractions` (not multi-project Clean Architecture) |

## Folder structure

```
OrderManagement.API/
├── Controllers/
├── Contracts/Requests|Responses/
├── Domain/          # entities, enums, status transition rules
├── Data/            # DbContext, EF configs, migrations
├── Services/
├── Validators/
├── Common/          # Result<T>, errors, helpers
├── Middleware/      # correlation ID, exception → ProblemDetails
├── Abstractions/    # interfaces
└── tests/           # unit + integration (separate test project)
```

## Domain model

- **Product:** Id, Name, StockQuantity, Price  
- **Order:** Id, CustomerId, ShippingAddress, Status, CreatedAt, UpdatedAt, RowVersion  
- **OrderItem:** Id, OrderId, ProductId, Quantity, UnitPrice  
- **IdempotencyRecord:** Key (unique), RequestHash, ResponseStatus, ResponseBody, CreatedAt  

### Status transitions

```
Pending   → Confirmed | Cancelled
Confirmed → Shipped | Cancelled
Shipped   → Delivered
Delivered / Cancelled → terminal
```

Cancel only from Pending/Confirmed; restore stock on cancel.

## API

| Method | Path | Notes |
|---|---|---|
| POST | `/api/orders` | Requires `Idempotency-Key`; atomic stock deduct |
| GET | `/api/orders/{id}` | 404 if missing |
| GET | `/api/orders` | Filters: status, customerId, from, to + pagination |
| PATCH | `/api/orders/{id}/status` | Valid transition + optimistic lock → 409 on conflict |
| POST | `/api/orders/{id}/cancel` | Restore stock; reject invalid/terminal |

Errors: consistent ProblemDetails (`400`, `404`, `409`, `422` as needed).

## Concurrency mapping

| FRD scenario | Strategy |
|---|---|
| A — concurrent stock | `UPDATE … WHERE StockQuantity >= @qty`; fail → 409 |
| B — concurrent status | RowVersion / xmin optimistic concurrency → 409 |
| C — idempotent create race | Unique index on Idempotency Key; loser returns winner response |

### Additional races (document in README)

1. Cancel vs status update concurrently → optimistic lock + transaction  
2. Double cancel / cancel after shipped → status gate  
3. Same Idempotency-Key with different body → request hash mismatch → 422  

## Cross-cutting

- Structured logging with correlation ID (`X-Correlation-Id`)  
- No `Console.WriteLine` for production tracing  
- Migrations for schema; seed products for concurrency tests  
- Tests must include ≥1 concurrent scenario (A/B/C); prefer all three  

## Out of scope

- Authentication / authorization (not in FRD)  
- Separate inventory microservice  
- Multi-project Clean Architecture / CQRS  
