# PoTraffic - Empirical Commute Volatility Engine

PoTraffic is a Blazor WebAssembly + ASP.NET Core application that measures
commute route volatility using Google Maps travel times (TomTom supplies
traffic incidents only). It records
travel-time samples on a schedule, computes route baselines, and flags
congestion or reroute anomalies in real time.

## Architecture

| Layer | Technology |
|---|---|
| Front-end | Blazor WebAssembly (.NET 10) + Radzen Blazor |
| Back-end | ASP.NET Core Minimal API (.NET 10); feature-folder handlers called from minimal-API endpoints |
| Persistence | Azure Table Storage, Azurite locally |
| Background Jobs | Table Storage-backed scheduler |
| Auth | Microsoft OAuth in dev/prod, Testing-only auth bypasses |
| Logging | Serilog + WASM client log forwarding |
| Observability | OpenTelemetry + Azure Monitor / Application Insights; app spans (`PoTraffic.API` source: jobs, Google Maps / TomTom / Open-Meteo calls, Table Storage writes) export only when App Insights or `OTEL_EXPORTER_OTLP_ENDPOINT` is configured |
| Testing | xUnit + NSubstitute, integration tests, Playwright .NET E2E |

The API and Blazor WASM static files are hosted together on Azure App Service.
CORS is intentionally not configured.

## Project Structure

```text
src/
  PoTraffic.API/             # ASP.NET Core host, Features/ slices, Infrastructure/
  PoTraffic.Client/          # Blazor WASM front-end
  PoTraffic.Shared/          # DTOs shared by API and client
tests/
  PoTraffic.UnitTests/
  PoTraffic.IntegrationTests/
  PoTraffic.E2ETests/
```

## Prerequisites

- .NET 10 SDK
- Docker Desktop for Azurite
- Azure CLI for local Key Vault access

## Quick Start

```powershell
dotnet restore
dotnet build
./SCRIPTS/start-dev.ps1
```

The startup script stops a stale PoTraffic host, starts PoTraffic's own Azurite (host port
10012, so it never collides with another project's Azurite on 10002), and launches the hosted
API/client. If another app already holds 5001/5000 it uses 5011/5010 (then 5021/5020) and
prints the URL.

| URL | Purpose |
|---|---|
| `https://localhost:5001` | Hosted API + Blazor client |
| `https://localhost:5001/scalar/v1` | Scalar API reference in Development |
| `https://localhost:5001/health` | Health status page (JSON at `/health/json`) |
| `https://localhost:5001/diag/keyvault` | Admin-only Key Vault diagnostics API |

## Running Tests

```powershell
./SCRIPTS/run-tests.ps1
```

Or run individual suites:

```powershell
dotnet test tests/PoTraffic.UnitTests
dotnet test tests/PoTraffic.IntegrationTests
dotnet test tests/PoTraffic.E2ETests
```

## Key Concepts

Routes have monitoring windows. When a window is active, the scheduler records
traffic samples at configured intervals until the window closes. Samples are
stored in Table Storage and used to build baseline travel-time bands per route
and time slot.

Cost controls, all editable by administrators on the Admin page (System Config tab, or
`PUT /api/admin/configuration/{key}`):

| Config key | Default | Effect |
|---|---|---|
| `quota.daily.default` | `10` | Monitoring sessions per user per local day. A per-user override (`PUT /api/admin/users/{userId}/daily-quota`, null to clear) takes precedence. |
| `cost.perpoll.googlemaps` | `0.005` | USD per recorded poll, used for every cost figure. |
| `cost.monthly.cap.usd` | `100` | Monthly provider budget. Once this month's polls × per-poll cost reach it, scheduled polls and Check now pause until the next UTC month or until the cap is raised. `0` disables the cap. Month-to-date spend: `GET /api/admin/spending`. |

Development and Production require Microsoft OAuth. Testing exposes isolated
auth bypass endpoints for integration and E2E tests; these are not available
when running the app normally.

## Development Guidelines

- Keep features as vertical slices under `Features/<FeatureName>/`.
- Do not add root-level `Services/`, `Repositories/`, or `DTOs/` folders.
- Keep NuGet versions in `Directory.Packages.props`.
- Do not enable AOT or trimming.
- Remove dead code and stale docs before merge.

See [AGENTS.md](AGENTS.md) for the full coding-agent contract.
