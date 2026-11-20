# AGENTS.md

## Po Web App Standard

This repo is one of several independent Po apps. It follows the conventions below, but it owns every file:
nothing is generated from, synced with, or required to stay identical to another repository. Change anything
here freely; only the *conventions* are common, never the code. Azure resources may be shared through the
`PoShared` resource group; code may not.

### Repo skeleton
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

### Page shell (every page, signed in or out, including the sign-in page)
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

### Version
- `Directory.Build.props` stamps ONE assembly metadata key: `BuildVersion` = UTC `yyMMdd`. No `BuildStamp`/`BuildDate`
  aliases. Every reader (header badge component, `/api/diag`, `/health` headers) reads `BuildVersion`.

### Health + /diag contract
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

### Workflow (`.github/workflows/deploy.yml`, the only one)
push to master → format check → build (Release, warnings as errors) → Unit tests → Integration tests (Testcontainers /
Azurite; Docker is available on ubuntu-latest) → publish → deploy → confirm `/health/live` X-Commit = this SHA.
If the repo has a MAUI `src/<App>.Mobile`, a second job in the SAME file (`mobile`, `needs: []`, not blocking deploy)
installs the `maui-android` workload and builds the Android target, so the client is always compiled.

### Tests
- Unit tier: fast, no I/O, runs in CI.
- Integration tier: runs in CI; locally it SKIPS with a clear message when Docker is not running (never a wall of
  `DockerUnavailableException` failures).
- E2E/UI tiers: run locally against a running app; when no app URL / browser is available they SKIP with a clear
  message. They are not part of CI.
- Docs must state real counts, not caps that are out of date.

### Azure naming
App Service `app-<repo lowercase>`; per-app resources named `<type>-<repo lowercase>`; shared things only in `PoShared`.

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
see the `PoBuildStamp` target in `Directory.Build.props`) as `<span class="po-version">`, fixed in
the bottom-right corner. Keep it there when changing the layout. The page shell header (app name,
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
- A red run on the default branch gets an ntfy push from the CI watcher in PoPunkouterSoftware. Fix it, or say why it is red.
