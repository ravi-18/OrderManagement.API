#!/usr/bin/env bash
# Apply EF Core migrations to create/initialize the order_management database.
# Requires ConnectionStrings__DefaultConnection (do not commit real passwords).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -z "${ConnectionStrings__DefaultConnection:-}" ]]; then
  echo "ERROR: Set ConnectionStrings__DefaultConnection first."
  echo "Example:"
  echo '  export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=order_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD"'
  exit 1
fi

dotnet tool restore
dotnet ef database update \
  --project src/OrderManagement.API/OrderManagement.API.csproj \
  --startup-project src/OrderManagement.API/OrderManagement.API.csproj

echo "Database update completed."
