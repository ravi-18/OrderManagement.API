# Order Management API

Prototype REST API for order management, aligned with the functional requirements (FRD). This repository is the ASP.NET Core scaffold; persistence, orders, and related endpoints are added in later tasks.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (required for database integration in later tasks; not wired up yet)

## Build

```bash
dotnet build
```

## Run the API

```bash
dotnet run --project src/OrderManagement.API
```

## Test

```bash
dotnet test
```

## Configuration

Local development uses `src/OrderManagement.API/appsettings.Development.json` for a placeholder PostgreSQL connection string. Replace `CHANGE_ME` with your local credentials when persistence is implemented; do not commit real secrets.
