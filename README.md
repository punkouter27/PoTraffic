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

Test caps: at most 100 unit, 50 integration, 25 API (`E2ETests/Api`) and 25 UI tests —
counted as test cases, so each `[Theory]` row counts. Merge overlapping tests before adding
new ones. `[ApiSkipUnlessReady]` probes `E2E_BASE_URL` when it is set.

## Key Concepts

Routes have monitoring windows. When a window is active, the scheduler records
traffic samples at configured intervals until the window closes. Samples are
stored in Table Storage and used to build baseline travel-time bands per route
and time slot.

On a route's Schedule tab a user can edit the schedule (swapped in one save, so a
rejected edit keeps the old one), change the addresses (only a changed address is
geocoded again; history is kept), and pause or resume monitoring (paused keeps the
schedule and history but stops the poll chain; Stop only ends today's session). The
sample commute's history is generated, so it never counts toward spend, admin usage or
the fleet volatility chart. With mock providers on, address suggestions are canned and
free.

Cost controls and feature flags, all editable at runtime by administrators on the Admin
page (Config tab, or `PUT /api/admin/configuration/{key}`). Every key, default and
validator lives in one catalogue, `Features/Config/SystemSettings.cs`:

| Config key | Default | Effect |
|---|---|---|
| `quota.daily.default` | `10` | Monitoring sessions per user per local day. A per-user override (`PUT /api/admin/users/{userId}/daily-quota`, null to clear) takes precedence. |
| `cost.perpoll.googlemaps` | `0.005` | USD per Distance Matrix call: each recorded poll and each Check now. |
| `cost.pergeocode.googlemaps` | `0.005` | USD per address geocode (two per route created). |
| `cost.perdirections.googlemaps` | `0.005` | USD per Directions call (a route map's road shape, fetched once per route). |
| `cost.perautocomplete.googlemaps` | `0.00283` | USD per Places Autocomplete request (address suggestions while typing). |
| `cost.monthly.cap.usd` | `100` | Monthly provider budget. Spend is this UTC month's polls plus the Check now, geocode, directions and autocomplete calls (counted in the `ProviderUsage` table), each at its rate. Once it reaches the cap, scheduled polls and Check now pause until the next month or until the cap is raised; route creation and suggestions keep working. `0` disables the cap. Breakdown: `GET /api/admin/spending`. |
| `feature.mockproviders.enabled` | `false` | Use the mock traffic/places providers instead of Google. Takes effect on the next request. The `Testing` environment or `Features:UseMockProviders=true` forces mocks regardless. |
| `feature.weather.enabled` | `true` | Fetch Open-Meteo weather for the route Weather tab. |

Development and Production require Microsoft OAuth. Testing exposes isolated
auth bypass endpoints for integration and E2E tests; these are not available
when running the app normally.

## Client experience

- **Dashboard:** one toolbar for search, status filter and sort; **Select** opens a drawer to
  pause, resume or delete several commutes at once. A WebGL background
  (`DashboardAmbient`, `pt-ambient.js`) tints with overall traffic right now. First load shows
  skeletons the same size as the final cards, so nothing jumps.
- **Sound:** short Web Audio cues (`pt-audio.js`) for Check now, new alerts, saves and undo.
  Check now's pitch rises with delay against the usual time; a faster-than-usual result also
  gets a particle burst. Mute it under Settings → Sound.
- **Graphics:** route cards and panels use frosted glass (`pt-glass.css`) with a solid
  fallback; the heatmap terrain is lit by a time-of-day sun; route flow renders with WebGPU,
  then WebGL2, then Canvas2D; map pins pulse faster as delay grows; the trend chart morphs
  between ranges. Chart and terrain data cross to JS as typed arrays via `[JSImport]`
  (`FxInterop.cs`). Everything respects the Motion setting and `prefers-reduced-motion`.
- **Keyboard:** `/` search, `n` new commute, `c` Check now, `?` shortcut help, `Esc` closes.
- **Settings:** common options on top; density, motion, locale, quota and extra panels sit
  in a collapsible Advanced section.

## Development Guidelines

- Keep features as vertical slices under `Features/<FeatureName>/`.
- Do not add root-level `Services/`, `Repositories/`, or `DTOs/` folders.
- Keep NuGet versions in `Directory.Packages.props`.
- Do not enable AOT or trimming.
- Remove dead code and stale docs before merge.

See [AGENTS.md](AGENTS.md) for the full coding-agent contract.
