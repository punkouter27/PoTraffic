# AGENTS.md

Contract for coding agents working in this repository. Architecture, commands, and code
conventions live in [CLAUDE.md](CLAUDE.md); this file covers how work is delivered.

## Getting oriented

Read `DOCS/` (if it exists) and [README.md](README.md) first for architectural context, then
[CLAUDE.md](CLAUDE.md) for architecture and commands.

When a change touches dependencies, configuration keys, or major endpoints, update `DOCS/`
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
   host (`./SCRIPTS/start-dev.ps1`) and check it is serving (`/health/json` returns 200).
3. **Test only what you touched.** Do not run the whole suite after a change. Run the tests
   covering the changed code, or none at all when the change is simple. The full run
   (`pwsh ./SCRIPTS/run-tests.ps1`) is for the user to invoke deliberately.
4. **UI changes get a before/after comparison**: an annotated HTML page pointing out what
   changed, saved to `SCREENSHOTS/{YYMMDD}-{feature}.html`. Give the user its absolute path.

Keep test files themselves correct and up to date when a change invalidates them.

Do it for the user: automate every run, build, and environment step. Never hand the user
CLI commands to type or portal pages to click through.

## UI standing requirements

The layout shows the build version as `YYMMDD` (from the build date or the git commit date),
fixed in the bottom-right corner. Keep it there when changing the layout.

### Running a subset of tests

```powershell
dotnet test tests/PoTraffic.UnitTests --filter "FullyQualifiedName~CreateRouteValidator"
dotnet test tests/PoTraffic.E2ETests --filter "FullyQualifiedName~MonitoringWindowScenarios"
```

E2E filters need a Testing host on `E2E_BASE_URL` (default `http://localhost:5150`); start it
with `dotnet run --project src/PoTraffic.API --launch-profile Testing` and leave it up across
iterations rather than letting the script restart it every time.

When fixing a batch of failures, never re-run the whole suite after each fix — run the tests
you are working on, and the full suite once at the end.

## Committing and pushing

`.github/workflows/deploy.yml` deploys to the production App Service on every push to
`master` — a push is a deploy, not a save. Do not push unless asked.

When asked to commit or sync, commit with a short, casual message in plain American slang —
not technical jargon — so it reads like a person wrote it, then push without asking again.

## Reporting back

- If a request removed more than 100 lines of code overall, say so.
- If an answer runs longer than 100 words, end it with a TL;DR of 20 words or fewer.
- If the code change is over 20 lines, append a three-tier summary, one sentence each:
  1. **Toddler** — the simple intuition.
  2. **Child** — what it now does.
  3. **Adult** — the technical and architectural change.
- If the code change is over 100 lines and the user's prompt left no open questions, end
  with exactly 3 strategic questions about where the app goes next and follow-on changes.

## CI/CD and Azure (same section in every Po repo)

- A push to `master` builds, tests, deploys and health-gates (`.github/workflows/deploy.yml`). Nothing is deployed by hand.
- Azure sign-in is GitHub OIDC (the `gh-<repo>-deploy` app). Its IDs are repo **variables** `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`, and the App Service name is `AZURE_WEBAPP_NAME`. Never move them to secrets or write resource names into workflows.
- Every permission the app and CI need lives in `SCRIPTS/ensure-azure-access.ps1`. A role granted by hand and not added there is a bug: the next re-provision loses it.
- The app answers `/health/live` (process up, no dependencies) and `/health` (dependencies reachable). The deploy's health gate checks both.
- Tests pass on any machine: no drive-letter, temp-folder or working-directory assumptions, and no live config value hard-coded as an expected result (read it from the same config the app reads).
- A red run on the default branch gets an ntfy push from the CI watcher in PoPunkouterSoftware. Fix it, or say why it is red.
