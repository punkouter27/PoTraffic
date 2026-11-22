# PoTraffic development guide

The detailed companion to [AGENTS.md](../AGENTS.md) (the standard and the working rules) and
[README.md](../README.md). Paths below are relative to the repo root.

## What this is

PoTraffic measures commute-route volatility. It records travel-time samples on a schedule
(Google Maps), builds per-route/time-slot baselines, and flags congestion and
reroute anomalies. Blazor WebAssembly client + ASP.NET Core minimal API on .NET 10, hosted
together as one App Service app (the API serves the WASM client — CORS is intentionally
never configured).

## Commands

```powershell
./scripts/start-dev.ps1        # stops a stale PoTraffic host, starts PoTraffic's Azurite, runs API (https)
./scripts/stop-dev.ps1         # docker compose down (Azurite)
dotnet build                   # TreatWarningsAsErrors=true — a warning is a build break
./scripts/setup.ps1            # first-time check: .NET SDK, Docker, az login / Key Vault, then build
./scripts/post-deploy-smoke.ps1 -BaseUrl https://potraffic-api-win2.azurewebsites.net
```

Port clashes: sibling projects on this machine use the same defaults (5000/5001 for a dev host,
10002 for Azurite). PoTraffic's dev Azurite therefore lives on host port **10012**
(`docker-compose.yml` and `appsettings.Development.json` agree), and `start-dev.ps1` waits on
that container's own health check, so it can never hydrate from another project's storage. It
stops only PoTraffic hosts; if another app holds 5001/5000 it falls back to 5011/5010, then
5021/5020, and prints the URL it chose.

Azure resources are defined in `infra/main.bicep`; production is
`https://potraffic-api-win2.azurewebsites.net` (app `potraffic-api-win2`).

| URL (dev) | |
|---|---|
| `https://localhost:5001` | API + Blazor client |
| `https://localhost:5001/diag` | Blazor *System status* page, anonymous (the only diagnostics UI route) |
| `https://localhost:5001/api/diag` | anonymous `DiagReportDto` behind `/diag`: statuses, timings, fixed text only |
| `https://localhost:5001/health` | readiness: 200 unless a dependency is Unhealthy (503) |
| `https://localhost:5001/health/live` | liveness, `X-Commit` header |
| `https://localhost:5001/health/json` | machine-readable dependency status |
| `https://localhost:5001/health/ready` | hydration complete? |
| `https://localhost:5001/scalar/v1` | Scalar API reference (Development only) |
| `https://localhost:5001/api/diag/keyvault` | admin-only JSON: did a vault secret resolve? |

`/diag` follows the /diag contract in AGENTS.md: title "System status", a plain-words banner,
then API, Data and Services. `GET /api/diag` (`DiagReportHandler`) never passes raw check
descriptions (which can name accounts or config keys) to anyone. Storage and the scheduler come
from the health checks; every external service (Google Maps, Open-Meteo, Nager.Date, TomTom,
Key Vault, Microsoft sign-in, Application Insights) is called LIVE by
`Features/Diagnostics/ServiceProbes.cs` with free requests and a 60 s cache. A service switched
off on purpose (mock providers, no TomTom key, no vault locally, the Test host's scheduler) is
`Off`: a grey pill, no latency, left out of the banner and the overall status.

### Tests

Four tiers. `run-tests.ps1` owns Azurite (Testcontainers), starts/stops the `Testing` host
on `http://localhost:5150` for the E2E tiers, and writes `TestResults/test-report.html`.
It also enforces test-case caps per tier (100 unit / 50 integration / 25 E2E API / 25 E2E UI)
before running anything. Real counts (2026-11-21): Unit 87, Integration 36, E2EAPI 13, E2EUI 17.
`pwsh scripts/run-e2eui.ps1 [-Tier E2EUI|E2EAPI|All]` runs the E2E tiers against the Release
publish (the build that deploys) with its own Azurite container, and stops everything after.
CI runs Unit and Integration; the E2E tiers run locally and skip with one clear message when no
app answers (or Chromium is missing); Integration skips locally when Docker is not running.

```powershell
pwsh ./scripts/run-tests.ps1                       # all tiers
pwsh ./scripts/run-tests.ps1 -Tier unit,integration
pwsh ./scripts/run-tests.ps1 -Tier e2e-ui -SkipBuild
```

Individual suites / single tests:

```powershell
dotnet test tests/PoTraffic.Unit
dotnet test tests/PoTraffic.Unit --filter "FullyQualifiedName~CreateRouteValidator"
dotnet test tests/PoTraffic.Integration   # needs Docker
dotnet test tests/PoTraffic.E2EUI          # needs a Testing host on E2E_BASE_URL
```

`E2E_HEADED=0` forces headless Playwright (headed is the local default). `E2E_BASE_URL`
overrides the E2E target.

## Architecture

### Projects

`src/PoTraffic.Api` (host + all server logic), `src/PoTraffic.Client` (Blazor WASM),
`src/PoTraffic.Shared` (DTOs, enums, strongly-typed IDs — referenced by both),
`src/PoTraffic.Mobile` (.NET MAUI client; not in the solution because it needs the MAUI workload —
CI's `mobile` job builds its Android target).

Tests are `PoTraffic.Unit`, `PoTraffic.Integration`, `PoTraffic.E2EAPI` (live HTTP) and
`PoTraffic.E2EUI` (Playwright), each named for the tier it holds; namespaces follow the folders.

### Vertical slices

Server features live under `src/PoTraffic.Api/Features/<Name>/`: one `*Endpoints.cs` that
maps a `MapGroup`, plus one file per operation holding the command/query record, its
`AbstractValidator` (if it has input to check), and its handler together. Entities live in the slice that owns them
(`Features/Routes/Route.cs`, `Features/Auth/User.cs`). Do not add root-level `Services/`,
`Repositories/`, or `DTOs/` folders. Client features mirror this under
`src/PoTraffic.Client/Features/<Name>/`.

On the client the split is: `Components/` holds only shared, generic pieces that know
nothing about the domain (`PtIcon`, `PageHeader`, `UndoBar`); anything that knows what a
route is belongs to its feature slice; `Pages/` holds the app-shell routes that belong to
no feature (`/`, `/not-found`, `/access-denied`). The `/diag` page lives in `Features/Diagnostics`.

### Handlers

Handlers are plain classes. Endpoints and jobs take the concrete handler as a parameter and
call `Handle(request, ct)` directly — there is no mediator. `Program.cs` registers every
`*Handler` class under `Features/` by assembly scan, so a new slice needs no DI wiring. A
handler with a validator runs it first (`Validator.ValidateAndThrowAsync`, a static
instance); the resulting `ValidationException` maps to 422 in `GlobalExceptionHandler`.
Read endpoints pass `RequestAborted`; writes pass `CancellationToken.None` so a closed tab
cannot cancel a billed provider call mid-write.

### Persistence — the part most likely to surprise you

`Infrastructure/Storage/TableStorageContext.cs` keeps the **entire working set in memory**,
hydrated once at startup from Azure Table Storage, and exposes `IQueryable<T>` so handlers
read with plain LINQ. `SaveChangesAsync` diffs JSON snapshots and writes only the delta
(with ETag optimistic concurrency) through `ITableStore`. Consequences:

- Every entity is one row: keys from the `Maps` registry in `TableStorageContext`, payload
  as JSON in a single `Data` column — the schema evolves with the C# types, no migrations.
- Adding an entity type = one entry in `Maps` plus a backing list field.
- Queries are in-process and cheap; writes are not — batch them into one `SaveChangesAsync`.
- The parameterless constructor gives a volatile memory-only context for unit tests.
- `PollRecord` partitions by route and `Alert` by user; everything else uses `"main"`.

### Auth

Backend-for-frontend: an HttpOnly `SameSite=Strict` cookie (`.PoTraffic.Auth`) is the only
session credential — the WASM client never sees a token, and `CookieAuthenticationStateProvider`
derives auth state from `GET /api/auth/me`. Key points in
`Infrastructure/Security/SecurityExtensions.cs`:

- **Deny by default**: the authorization `FallbackPolicy` requires an authenticated user, so
  any endpoint without explicit metadata is protected. Genuinely public surfaces (login flow,
  health, `/api/diag` summary, SPA shell) must call `.AllowAnonymous()` explicitly.
- Feature endpoints use `.RequireAuthorization("ProductionMicrosoftAuth")` — Microsoft OAuth
  is required in Development/Staging/Production and GUEST sessions are rejected there; the
  policy is a no-op only in `Testing`.
- Outside Production a `SmartCookieOrFake` policy scheme forwards to `FakeAuthHandler` when
  the `X-Fake-User` header is present (`X-Fake-Roles` for roles). `FakeAuthHandler` and
  `MapGuestEndpoints` throw if wired into a Production host.
- `Testing` alone exposes `Infrastructure/Testing/TestingEndpoints.cs` (`/e2e/dev-login`,
  `/e2e/seed-admin`, `/e2e/seed-route`, `/e2e/execute-poll`).

### Scheduling

`BackgroundSchedulerService` ticks every 15 minutes against Table Storage-backed job rows,
executing each due job in its own DI scope. Job types (`PollRouteJob`,
`PruneOldPollRecordsJob`) are resolved from DI by the type name stored on the job, so a new
job type must also be registered in `Program.cs`. On startup it requeues jobs a crashed
process left `Running` and re-arms monitored routes whose poll chain died — polling is a
self-perpetuating chain, not a fixed timer.

### Traffic providers

Google Maps is the one traffic provider: handlers inject `ITrafficProvider` directly. The
`feature.mockproviders.enabled` system setting picks live or `MockTrafficProvider` per request
(admin-editable, no restart); `Features:UseMockProviders=true` or the `Testing` environment
forces mocks at host level and registers only the mocks. TomTom is used only for incident text on alerts (`IIncidentProvider`). Live
clients are wired to the named resilience pipeline in `ResiliencePipelineExtensions`.

### Program.cs ordering constraints

`builder.AddPoTrafficKeyVault()` must stay before service registration (services read secrets
eagerly at registration time). `UseForwardedHeaders` runs first so HTTPS redirection sees the
real scheme behind App Service. `UseBlazorFrameworkFiles` + `UseStaticFiles` run before auth
so the client can load before sign-in.

### Shared IDs

`PoTraffic.Shared/Ids` defines `UserId`, `RouteId`, etc. as `readonly record struct`s
implementing `IParsable` (minimal-API binding works with no extra code) and serializing as
bare GUID strings. Construct with `New()` / `From(guid)`; unwrap via `.Value` only at a real
boundary. Do not introduce raw `Guid` parameters for entity identity.

## Conventions

- Central package versions only — edit `Directory.Packages.props`, never a `<Version>` in a
  `.csproj`. Shared MSBuild properties live in `Directory.Build.props` and must not be
  duplicated per-project.
- No AOT, and do not mark `PoTraffic.Client` `IsTrimmable` (routable `@page` components are
  found by a runtime route scan the trimmer cannot see). Static web asset fingerprinting is
  deliberately off in the client csproj — the hosted pipeline serves physical file names.
- Logging is Serilog through `ILogger<T>` only; `LogContextEnrichmentMiddleware` adds UserId
  and Environment to every event. `PiiRedactor` exists for anything user-identifying.
- Integration test methods use `[SkipUnlessAzuriteAvailable]`, not `[Fact]`, so the suite
  skips rather than hard-fails without Docker. E2E tests use `[ApiSkipUnlessReady]` /
  `[SkipUnlessE2EReady]`.
- CI (`.github/workflows/deploy.yml`, the only workflow) checks formatting, builds, runs the Unit and
  Integration tiers, then publishes and deploys `master` to App Service and waits until `/health/live`
  answers with this commit's `X-Commit`; a red check stops the deploy. A second job, `mobile`, builds
  the Android target of `src/PoTraffic.Mobile` without blocking the deploy. No Bicep, no rollback and
  no pull-request builds. Repo variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
  `AZURE_SUBSCRIPTION_ID`, `AZURE_WEBAPP_NAME`, `AZURE_RESOURCE_GROUP`. Bicep is applied by hand
  (`infra/README.md`); `scripts/post-deploy-smoke.ps1` is run by hand after a deploy.

## Page shell

Every page, signed in and out, carries the same shell (AGENTS.md, Page shell), styled by
`wwwroot/css/po-tokens.css` (this repo's own copy of the Po conventions):

- **Header** `Layout/PoHeader.razor` (`.po-header`), rendered by `MainLayout`, `AuthLayout`,
  `BareLayout` and the root error boundary: the app name top-left (links to `/`); top-right the
  signed-in email (`.po-header__email`) or the Sign in button (`po-link-btn filled small`), then the one settings gear
  (`.po-header__settings`, `/settings`). The bell and the mock-data chip sit in the same cluster.
- **Settings** `/settings` (`/account/settings` is an alias) is anonymous: theme note and build
  stamp for anyone; email, Admin, Sign out, dev personas and the account settings when signed in.
- **Theme** follows the device (`prefers-color-scheme`) only: no switch, no `data-theme`. App
  tokens (`--pt-*`, `wwwroot/css/pt-tokens.css`) alias the `--po-*` estate tokens: indigo
  accent, Inter body text, Space Grotesk headings (self-hosted in `wwwroot/fonts/`).
  Material Symbols is the only Google Fonts load (icon font).
- **Version badge** `<span class="po-version">vYYMMDD</span>` (`Layout/BuildVersion.razor`), the
  UTC date of the HEAD commit, stamped once as `BuildVersion` by the `PoBuildStamp` target in
  `Directory.Build.props` for every assembly; `/api/diag` reports the same value. Fixed bottom-right
  on wide screens; at <=768px it is static and rendered last in the page column, so it sits after
  the content and never covers it. The bottom nav (all its destinations need an account) is shown
  to signed-in users only.
- **Sign-in page** `/login` (`AuthLayout`): the header shows the title only; the page shows the
  title and the one "Sign in with Microsoft" button (plus "Continue as Guest" on Development/Test hosts).
- **/diag** "System status": banner, then API / Data / Services as `.po-diag__card`s with `.po-badge--ok|warn|bad|off`.
- **Tab window**: Tab with nothing focused opens `/po-dev.html` (`js/po-devwindow.js`,
  `js/po-dev.js`): `/health/live` + `X-Commit`, and the checks from `GET /api/diag`. `App.razor` only moves focus to the h1 after the user has
  followed a link, so a fresh page has nothing focused.
