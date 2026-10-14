# Apply EF Core migrations to create/initialize the order_management database.
# Requires ConnectionStrings__DefaultConnection (do not commit real passwords).
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__DefaultConnection)) {
    Write-Error @"
Set ConnectionStrings__DefaultConnection first.
Example:
  `$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=order_management;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
"@
}

dotnet tool restore
dotnet ef database update `
  --project src/OrderManagement.API/OrderManagement.API.csproj `
  --startup-project src/OrderManagement.API/OrderManagement.API.csproj

Write-Host "Database update completed."
