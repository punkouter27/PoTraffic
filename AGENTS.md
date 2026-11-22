# AGENTS.md

## 1. Standard

> This repo owns every file in it. Nothing here is generated from, synced with, or required to stay identical to
> another repository. Headers like "SAME FILE IN EVERY PO …", "Generated from po-shared … DO NOT EDIT", and scripts
> that copy files into or check other repos are forbidden. Only Azure resources in the `PoShared` resource group
> may be shared.

### Po Web App Standard

This repo is one of several independent Po apps. It follows the conventions below, but it owns every file:
nothing is generated from, synced with, or required to stay identical to another repository. Change anything
here freely; only the *conventions* are common, never the code. Azure resources may be shared through the
`PoShared` resource group; code may not.

#### Repo skeleton
```
README.md          what the app is, live URL, how to run locally, how it deploys
AGENTS.md          this standard + repo-specific notes (CLAUDE.md just points here)
CLAUDE.md          one line: "Read AGENTS.md."  (plus repo-specific notes only if unavoidable)
<App>.slnx  global.json  Directory.Build.props  Directory.Packages.props  .editorconfig
src/<App>.Api  src/<App>.Client  src/<App>.Shared  [src/<App>.Mobile]
tests/<App>.Unit  tests/<App>.Integration  [tests/<App>.E2E...]
infra/main.bicep  infra/main.bicepparam  [infra/modules/*.bicep]
scripts/           setup / helper scripts (no infra/*.ps1)
docs/              lowercase; everything else that is prose
.github/workflows/deploy.yml   the ONLY workflow
```
No `.cursorrules`, `.copilotignore`, `SPEC.md`, `tasks/`, `DOCS/` at the root (fold useful content into docs/).
`SCREENSHOTS/` is allowed only if README references it.

#### Page shell (every page, signed in or out, including the sign-in page)
- Header `.po-header`, height 56px, sticky: app name top-left (`.po-header__title`, links to `/`).
- Top-right `.po-header__actions`: signed-in user's **email** (`.po-header__email`, full email, ellipsis at 16ch on
  phones) — or, signed out, one **Sign in** button `class="po-link-btn filled small"` (same style on every page) —
  then the settings gear ONLY if the app has a settings page (the owner removed settings from some apps — don't
  re-add it). Apps without OAuth show nothing in place of the email (no fake identity chips).
- Version badge `.po-version` fixed bottom-right showing `vYYMMDD`. It must never cover content: `main` gets
  `padding-bottom: calc(var(--po-space-6) + env(safe-area-inset-bottom))`, and when a mobile bottom nav exists the
  badge sits above it (`--po-stamp-lift: <nav height>`).
- Colours only from `wwwroot/css/po-tokens.css` (`--po-*`). All text ≥ 4.5:1 contrast in light AND dark mode
  (status banners: use `--po-text` on `--po-*-soft` backgrounds, never warning-on-peach).
- Login-only data must not be requested for signed-out visitors (no 401s in the console on public pages), and
  actions a signed-out visitor cannot perform are hidden.

#### Version
- `Directory.Build.props` stamps ONE assembly metadata key: `BuildVersion` = UTC `yyMMdd`. No `BuildStamp`/`BuildDate`
  aliases. Every reader (header badge component, `/api/diag`, `/health` headers) reads `BuildVersion`.

#### Health + /diag contract
- `GET /health/live` (liveness, returns header `X-Commit: <sha>`), `GET /health` (readiness), `GET /api/diag` →
  ```
  DiagReportDto(string Status, string Environment, string Version, DateTimeOffset CheckedAt, double CheckedMs,
                DiagCheckDto[] Checks)
  DiagCheckDto(string Name, string Group /* "api" | "data" | "service" */, string Label, string Status
               /* Healthy|Degraded|Unhealthy */, string Purpose, string Detail, double LatencyMs, string? Problem)
  ```
- UI route `/diag` ONLY (delete other diagnostics pages/routes such as `/system`, `/diagnostics`, `/status`). Page
  `<title>` and `h1`: **System status**. Layout: overall banner in plain words ("Everything is working" /
  "Working, with N things degraded" / "Not working: …"), then three sections in this order: **API**, **Data**,
  **Services**; each row = label, purpose, status pill, latency ms, problem/detail text. Every external dependency
  the app really calls appears as a check. Anonymous, nothing secret in the payload.

#### Workflow (`.github/workflows/deploy.yml`, the only one)
push to master → format check → build (Release, warnings as errors) → Unit tests → Integration tests (Testcontainers /
Azurite; Docker is available on ubuntu-latest) → publish → deploy → confirm `/health/live` X-Commit = this SHA.
If the repo has a MAUI `src/<App>.Mobile`, a second job in the SAME file (`mobile`, `needs: []`, not blocking deploy)
installs the `maui-android` workload and builds the Android target, so the client is always compiled.

#### Tests
- Unit tier: fast, no I/O, runs in CI.
- Integration tier: runs in CI; locally it SKIPS with a clear message when Docker is not running (never a wall of
  `DockerUnavailableException` failures).
- E2E/UI tiers: run locally against a running app; when no app URL / browser is available they SKIP with a clear
  message. They are not part of CI.
- Docs must state real counts, not caps that are out of date.

#### Azure naming
App Service `app-<repo lowercase>`; per-app resources named `<type>-<repo lowercase>`; shared things only in `PoShared`.

#### Components (Radzen), validation, layout states (this repo)
- Every control is a Radzen component (`Radzen.Blazor`, pinned in `Directory.Packages.props`):
  buttons are `RadzenButton`, fields `RadzenTextBox`/`RadzenDropDown`/…, dialogs and the bulk
  drawer go through `DialogService` (`Layout/PtDialog.razor`). A control that keeps the app's own
  look carries class `pt-plain` (the `plain` cascade layer in `wwwroot/css/vendor.css` reverts the
  Radzen theme on it); scoped CSS reaches it with `::deep`.
- Allowed native exceptions (the only ones): native file inputs for resumable uploads (none
  here), native audio/video (none here), and read-only data tables: `<table>` in
  `Features/Routes/VolatilityHeatmap.razor`.
- FluentValidation with inline errors. Validators are created BY TYPE (each handler holds
  `private static readonly XValidator Validator = new()`; the client's `CreateRoutePage` passes its
  validator instance to `FluentValidationValidator`), never by assembly scanning. No `alert()`/`confirm()`.
- One `MainLayout` (plus `AuthLayout` for `/login` and `BareLayout`, sharing `PoHeader` and
  `BuildVersion`), a `NotFound` page, and `StateCard`/`LoadingErrorCard`/`SectionErrorBoundary`
  for loading, empty and error states.
- Sign-in is Microsoft Entra; `/login` shows the title and one "Sign in with Microsoft" button
  (Development/Test hosts add "Continue as Guest" for the browser tests). The bottom nav and the
  header's account items are for signed-in users only.
- `/diag` calls every external service live (`Features/Diagnostics/ServiceProbes.cs`); a service
  switched off on purpose is `Off` (grey, not counted in the banner or the overall status).
- On phones (<=768px) the `vYYMMDD` stamp is static, after the page content.

## 2. Concurrent work

> Several sessions (people and agents) commit to this repo, sometimes at the same time.
> - Before every commit and every push: `git fetch origin && git rebase --autostash origin/master`.
> - Stage only the files you changed (`git add <paths>`); never `git add -A` / `git add .`; check `git diff --cached`.
> - Never revert, reformat or overwrite changes you didn't make; keep the owner's recent decisions (e.g. features
>   removed on purpose stay removed).
> - Don't commit or sync from the IDE while an agent is working in this folder.
> - Pushing `master` deploys production; changes to `.github/workflows/*` need a token with the `workflow` scope.

## 3. Verification

One command sequence verifies the repo (format, build, Unit, Integration, then E2E API + E2E UI on the Release publish):

```powershell
dotnet format PoTraffic.slnx --verify-no-changes && dotnet build PoTraffic.slnx -c Release && dotnet test tests/PoTraffic.Unit -c Release && dotnet test tests/PoTraffic.Integration -c Release && pwsh scripts/run-e2eui.ps1 -Tier All
```

Every command below was run on 2026-11-21 (PowerShell or bash, repo root).

| What | Command | Needs / skips |
|---|---|---|
| Format | `dotnet format PoTraffic.slnx --verify-no-changes` | — |
| Build | `dotnet build PoTraffic.slnx -c Release` | warnings are errors |
| Unit | `dotnet test tests/PoTraffic.Unit -c Release` | nothing (87 tests) |
| Integration | `dotnet test tests/PoTraffic.Integration -c Release` | Docker (Testcontainers Azurite); Docker-dependent tests skip with one message without it (36) |
| Azurite for a local run | `docker run -d --rm -p 21000:10000 -p 21001:10001 -p 21002:10002 mcr.microsoft.com/azure-storage/azurite:latest azurite --blobHost 0.0.0.0 --queueHost 0.0.0.0 --tableHost 0.0.0.0 --skipApiVersionCheck --loose` | Docker |
| Run locally (Test host: mock traffic, guest sign-in) | `ConnectionStrings__TableStorage="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=<Azurite's well-known dev key>;TableEndpoint=http://127.0.0.1:21002/devstoreaccount1;" dotnet run --project src/PoTraffic.Api --launch-profile Test` | → http://localhost:5150 and http://localhost:5150/diag |
| E2E UI + API on the Release publish | `pwsh scripts/run-e2eui.ps1 -Tier All` | Docker + Playwright Chromium; publishes `-c Release` (the build that deploys), starts its own Azurite + the published Test host on 5150, runs E2EAPI (13) and E2EUI (17), stops everything |
| E2E API (against a running host) | `E2E_BASE_URL=http://localhost:5150 dotnet test tests/PoTraffic.E2EAPI -c Release` | the Test host above; skips without it (13) |
| E2E UI (against a running host) | `E2E_HEADED=0 E2E_BASE_URL=http://localhost:5150 dotnet test tests/PoTraffic.E2EUI -c Release` | the Test host + Playwright Chromium; skips with the reason otherwise (17) |

- **Deploy:** push to `master` runs `.github/workflows/deploy.yml` (format, build, Unit, Integration, publish,
  deploy; the `mobile` job builds the Android client beside it). `*.md` changes do not deploy.
- **Deploy check:** `curl -sI https://potraffic-api-win2.azurewebsites.net/health/live` shows `X-Commit: <pushed sha>`
  (the workflow waits for it).
- **Live:** https://potraffic-api-win2.azurewebsites.net and https://potraffic-api-win2.azurewebsites.net/diag
- **Pinned:** .NET SDK `10.0.400` (`global.json`, rollForward latestFeature); MAUI client built with the
  `maui-android` workload in CI only (not in the .slnx).

### Deliberate deviations

| Deviation | Why | Commit sha / date | How to undo |
|---|---|---|---|
| `/login` brand panel removed: header title only, card h1 and one "Sign in with Microsoft" button. This overrides the owner's `6a24553` ("brand panel reaches the bottom again") | the v3 standard (title + one button) conflicted with the owner's panel | `3ee28fb` 2026-11-21 | `git revert 3ee28fb` (login part) or restore from `6a24553` |
| Development/Test hosts add "Continue as Guest" on `/login`; Production shows one button | the browser tests sign in with it | `3ee28fb` 2026-11-21 | remove it (tests then need a real account) |
| Radzen exception: read-only `<table>` in `Features/Routes/VolatilityHeatmap.razor` (the only one) | read-only data table, an allowed kind (Profile A) | `3ee28fb` 2026-11-21 | — |
| /diag: a service switched off on purpose is **Off** (grey, not counted); live today TomTom incidents (no `PoTraffic--TomTom--ApiKey`) and the scheduler on hosts without timed checks | opt-in services left off are not faults | `3ee28fb`, `9c06ec4` 2026-11-21 | add the key to `kv-poshared2` |
| Phones (<= 768px): stamp static after the page content; bottom nav signed-in only | a fixed stamp and nav covered /diag cards (overrides the older fixed rule) | `3ee28fb` 2026-11-21 | `git revert 3ee28fb` (stamp part) |
| Client app assembly is not `IsTrimmable` (owner note: routable pages); the browser tier runs on the Release publish that deploys | trimming routable pages breaks them | — | owner call |
| App Service `potraffic-api-win2` (standard `app-potraffic`) on `asp-PoTraffic-f1` | renaming means recreating | — | owner-approved recreate |

**Hosting:** App Service F1 (Free, Windows, `asp-PoTraffic-f1`, no Always On; checked with `az` 2026-11-22, so
`infra/README.md`'s B1 `asp-poissues-b1-wus2` is out of date): 60 CPU-minutes and a data-out quota per day (Azure
reported 3.1 GB). Never run test suites against the live site; live checks = at most 6 page loads + /diag + X-Commit.

---

# PoTraffic: repo-specific notes

Architecture, commands, tests and conventions are in [docs/development.md](docs/development.md);
the product overview is [README.md](README.md). The rules below cover how work is delivered.

## Getting oriented

Read [README.md](README.md) first for context, then [docs/development.md](docs/development.md)
for architecture and commands.

When a change touches dependencies, configuration keys, or major endpoints, update `docs/`
and `README.md` in the same change so they never drift from the code.

## Branching

**Work directly on `master`. Do not create branches unless explicitly asked.**

No feature branches, no `design/*` branches, no worktrees — commit to `master`. If a change
seems large enough to want isolation, ask first rather than branching on your own initiative.

Why: branches accumulated here faster than they were merged, so `master` stopped being the
latest code and reconciling them later cost more than the isolation was worth.

Corollary: if a branch does exist and is finished, merge it into `master` rather than
leaving it open.

## Configuration and secrets

Do not use `dotnet user-secrets` for local data. Put settings in `appsettings*.json`, and
secrets in Azure Key Vault (`KeyVault:Uri`) when one exists.

## Verifying a change

1. **Build clean.** `TreatWarningsAsErrors` is on — a compiler warning is a build break, so
   fix warnings rather than suppress them.
2. **Restart the app and confirm it came back.** After any code change, restart the dev
   host (`./scripts/start-dev.ps1`) and check it is serving (`/health/json` returns 200).
3. **Test only what you touched.** Never run the whole suite. Run the tests covering the
   changed code, or none at all when the change is minor. The full run
   (`pwsh ./scripts/run-tests.ps1`) is for the user to invoke deliberately.
4. **UI changes get a before/after comparison**: an annotated HTML page pointing out what
   changed, saved to `SCREENSHOTS/{YYMMDD}-{feature}.html`, plus a single annotated PNG
   showing before and after side by side with notes (`SCREENSHOTS/{YYMMDD}-{feature}.png`).
   Give the user both absolute paths.

Keep test files themselves correct and up to date when a change invalidates them.

Do it for the user: automate every run, build, and environment step. Never hand the user
CLI commands to type or portal pages to click through.

## UI standing requirements

Consolidate functionality and UI into shared components when possible — before adding a
widget, look for an existing one that does the same job and extend it instead.

The layout shows the version badge `vYYMMDD` (`BuildVersion`, the UTC date of the HEAD commit;
see the `PoBuildStamp` target in `Directory.Build.props`) as `<span class="po-version">`, bottom-right: fixed on
wide screens, static after the content on phones (<= 768px). Keep it so when changing the layout. The page shell header (app name,
email or Sign in, settings gear) is on every page; see "Page shell" in docs/development.md.

### Running a subset of tests

```powershell
dotnet test tests/PoTraffic.Unit --filter "FullyQualifiedName~CreateRouteValidator"
dotnet test tests/PoTraffic.E2EUI --filter "FullyQualifiedName~MonitoringWindowScenarios"
```

E2E filters need a Testing host on `E2E_BASE_URL` (default `http://localhost:5150`); start it
with `dotnet run --project src/PoTraffic.Api --launch-profile Test` and leave it up across
iterations rather than letting the script restart it every time.

When fixing a batch of failures, run only the tests you are working on — never the whole
suite, not even once at the end. The full run is the user's call.

## Committing and pushing

`.github/workflows/deploy.yml` deploys to the production App Service on every push to
`master` — a push is a deploy, not a save. Do not push unless asked.

When asked to commit or sync, commit with a short, casual message in plain American slang —
not technical jargon — so it reads like a person wrote it, then push without asking again.

## Reporting back

- If a request removed more than 100 lines of code overall, say so.
- If an answer runs longer than 100 words, end it with a TL;DR of 20 words or fewer.
- If problems are found along the way, propose a solution for the user to approve.
- If the code change is over 20 lines, append a three-tier summary, one sentence each,
  plus a diagram that makes the change easier to follow
  (style: https://github.com/cathrynlavery/diagram-design):
  1. **Toddler** — the simple intuition.
  2. **Child** — what it now does.
  3. **Adult** — the technical and architectural change.
- If the code change is over 100 lines and the user's prompt left no open questions, end
  with exactly 3 strategic questions about where the app goes next and follow-on changes.

## CI/CD and Azure

- A push to `master` runs `.github/workflows/deploy.yml` (the shape in the standard above): format check, Release build, Unit and Integration tiers, publish, deploy, then wait for `/health/live` to report the pushed commit in `X-Commit`. A red check stops the deploy and the live site keeps its last good build. The non-blocking `mobile` job builds the Android target of `src/PoTraffic.Mobile`. E2E tiers, Bicep and the other scripts run locally. Nothing is deployed by hand.
- Azure sign-in is GitHub OIDC (the `gh-<repo>-deploy` app). Its IDs are repo **variables** `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`, and the App Service name is `AZURE_WEBAPP_NAME`. Never move them to secrets or write resource names into workflows.
- Every permission the app and CI need lives in `scripts/ensure-azure-access.ps1`. A role granted by hand and not added there is a bug: the next re-provision loses it.
- The app answers `/health/live` (process up, no dependencies, `X-Commit` header), `/health` (readiness), `/health/json` and `/api/diag` (dependencies reachable). The deploy waits for `/health/live` to report the pushed commit.
- Tests pass on any machine: no drive-letter, temp-folder or working-directory assumptions, and no live config value hard-coded as an expected result (read it from the same config the app reads).
- Nothing alerts on a red run any more (the PoPunkouterSoftware CI watcher was dropped 2026-11-20): check `gh run list -L 1` after a push. Fix it, or say why it is red.
