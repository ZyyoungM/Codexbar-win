# CodexBar Windows Implementation Progress

Last updated: 2026-04-20

This file is the project progress ledger. Whenever a feature is added, changed, removed, or meaningfully fixed, update this document in the same change.

Status legend:

- `[x]` Implemented and manually/test verified
- `[~]` Partially implemented or backend-only
- `[ ]` Not implemented
- `[!]` Known issue or needs follow-up

## Current Snapshot

This project is a Windows-native CodexBar port focused on preserving the shared `CODEX_HOME` / `~/.codex` history pool.

Non-negotiable behavior:

- switching must only update the active `config.toml` and `auth.json`
- switching must not copy, split, rewrite, or relocate `sessions` or `archived_sessions`
- account changes affect new sessions only
- OpenAI OAuth keeps the browser + localhost callback model with manual paste fallback

The current implementation is a working MVP with a native tray-window model (`MainFlyout` + independent `Overlay` + popup windows) for manual testing. Backend services are intended to survive later UI replacement.

## Feature Status

| Area | Status | Notes |
| --- | --- | --- |
| Shared `.codex` compatibility | `[x]` | Resolves `CODEX_HOME`, falls back to `%USERPROFILE%\.codex`, and recognizes `config.toml`, `auth.json`, `sessions`, and `archived_sessions`. |
| Shared history pool preservation | `[x]` | Activation updates active config/auth state only. Tests assert session files are not rewritten. |
| TOML / auth compatibility | `[x]` | Lightweight TOML editing preserves unrelated content. OAuth `auth.json` output now includes Codex-required top-level `last_refresh`. |
| Atomic activation write | `[x]` | `config.toml` and `auth.json` are written through transaction/rollback flow. |
| OpenAI OAuth browser flow | `[x]` | PKCE, browser auth, localhost callback capture, and manual callback/code fallback are wired. Successful saves now rotate to a fresh OAuth attempt, explicitly cancel and release any stale `localhost:1455` listener before starting the next flow, full callback URLs must match the current `state`, and manual fallback always prefers the current pasted input over stale captured tokens. |
| OpenAI OAuth account naming | `[x]` | Account label/email/sub are backfilled from `id_token` when available. |
| Local API browser CORS boundary | `[x]` | Cross-origin browser access is limited to trusted loopback origins only: the API self-host origin plus the frontend rebuild dev/preview origins on ports `5173` and `4173`. Arbitrary external pages cannot read or mutate the local API. |
| OpenAI official plan / quota snapshot | `[~]` | Read-only refresh works for OAuth accounts. UI shows remaining quota, next reset time, refresh timestamp, and refresh failures. Source endpoint is official-hosted but undocumented. |
| Multiple OpenAI OAuth accounts | `[x]` | Multiple OpenAI OAuth accounts can be stored, displayed, switched, and re-activated. |
| OpenAI-compatible providers | `[x]` | Provider ID/name/base URL plus account/API key are supported. Activation writes Codex-native provider config and `apikey` auth, launched Codex child processes receive the active compatible key as `OPENAI_API_KEY`, compatible activation preserves the existing OAuth identity snapshot when available, and compatible providers can map their Codex-facing provider ID to `openai` for Desktop history filtering via `openai_base_url` without overriding reserved built-ins. |
| Multiple API keys under same provider | `[x]` | Supported by reusing provider ID with different account IDs. |
| Compatible Provider connectivity probe | `[x]` | Main flyout and CLI can probe compatible accounts through `/models` and suggest a missing `/v1` Base URL when detected. |
| Account edit UI | `[x]` | Temporary UI can edit labels; compatible providers can edit internal Provider ID, Codex Provider ID, name, base URL, and API key. |
| Manual account ordering | `[x]` | `ManualOrder` is persisted and temporary UI supports Up/Down. Reorder writes now require a complete, non-duplicate account set so partial/stale payloads cannot silently drop omitted accounts. |
| Usage-based ordering | `[~]` | Ordering prefers official OpenAI quota pressure when present, then local usage history. Attribution still depends on recorded successful switch journal entries. |
| CSV export/import | `[x]` | Backend and native Settings popup both exist. Default export excludes secrets; explicit secret export is supported. |
| Windows Credential Manager | `[x]` | API keys and OAuth tokens are persisted through Credential Manager. |
| Local usage scanner | `[~]` | Scans `sessions` and `archived_sessions`, exposes Today / Last 7 Days / Last 30 Days / Lifetime totals, attributes sessions by switch intervals, and auto-refreshes every minute while the main flyout is open. Cost pricing is still placeholder. |
| Locked session file handling | `[x]` | Scanner uses shared read and skips unreadable active files. |
| Tray host | `[~]` | Tray host now coordinates left-click main flyout, right-click menu actions, direct overlay toggling, and warm-start command handoff. Icon/art and packaging still need work. |
| Native window hierarchy | `[x]` | `CodexBar.Win` now uses an explicit native surface model: tray host, `MainFlyout`, independent `Overlay`, and separate popup windows for Settings / OAuth / Add Compatible / Edit Account. The runtime entry stays native and does not collapse these surfaces into route pages. |
| Temporary WPF shell | `[x]` | Main flyout and popup windows remain native WPF surfaces for now, with shared state between the main flyout and overlay. UI is still optimized for manual testing rather than final polish. |
| Figma visual baseline | `[~]` | The current native rebuild follows the imported Figma hierarchy as a visual and interaction reference only. The candidate runtime remains the native WPF shell rather than a browser-style host. |
| Main flyout action feedback | `[x]` | Refresh / switch / save / delete / launch flows show in-progress and completion/failure feedback. The account list now stays interactive during refresh-oriented operations instead of being blanket-disabled. |
| Startup registration | `[~]` | Registry startup toggle exists. Current startup target still depends on development-style executable layout. |
| Single-instance startup command forwarding | `[x]` | Secondary launches now forward `--open`, `--overlay`, and `--settings` into the running primary instance. `--tray-only` remains a cold-start / startup-registration path. |
| Codex Desktop / CLI path settings | `[x]` | Config model, CLI, Settings UI, detection, and launch fallback are wired. GUI launch re-syncs active account into `config.toml` / `auth.json` before starting Codex, Desktop launch cleans inherited Codex/Electron internal environment variables, and Desktop detection prefers the newest WindowsApps / MSIX Codex package instead of a stale version-pinned path. |
| Activation behavior setting | `[x]` | `WriteConfigOnly` and `LaunchNewCodex` are both wired. |
| OpenAI manual / aggregate mode | `[~]` | `ManualSwitch` works directly. `AggregateGateway` currently performs activation-time OpenAI auto-routing; it is not yet a live request proxy. |
| GitHub Releases update check | `[ ]` | Not implemented. |
| Real installer / self-contained publish | `[~]` | `package.ps1` 可生成包含本地 `.dotnet` 运行时的便携发布目录与 zip 包；正式 self-contained / installer（MSIX / MSI）仍未实现。 |

## Verification Status

Latest verified commands:

```powershell
.\build.ps1
.\test.ps1
git diff --check
```

Latest known result:

```text
build: succeeded
tests: 39 passed
diff-check: clean
```

Current automated coverage includes:

- `CODEX_HOME` resolution
- duplicate environment key casing such as `Path` / `PATH`
- TOML preservation
- compatible-provider activation
- compatible-provider API key auth format and native provider config
- compatible-provider auth snapshot preservation for history continuity
- compatible-provider Codex-facing provider-id aliasing
- compatible-provider built-in openai alias via `openai_base_url`
- compatible-provider launch environment injection
- compatible-provider connectivity probe with `/v1` suggestion
- local API trusted loopback CORS allowlist
- OAuth activation writes Codex-compatible top-level `last_refresh`
- transaction rollback
- OAuth manual callback parsing
- OAuth session isolation for manual fallback and post-save reset
- OAuth flow rotation cancels stale loopback listeners before restarting localhost capture
- usage scanner read-only behavior
- usage scanner locked active session tolerance
- usage attribution by switch intervals
- switch-journal provider-id migration
- aggregate-gateway reroute behavior
- aggregate-gateway official-quota preference behavior
- aggregate-gateway manual-mode passthrough
- aggregate-gateway healthy-account preference over reauth-needed accounts
- launch-service write-only skip behavior
- launch-service desktop start path
- desktop locator current-cli package inference
- desktop locator latest packaged-version preference
- desktop locator packaged-app autodetection without configured path
- portable packaging script and zip output
- manual account order persistence
- manual account reorder complete-payload enforcement
- remaining-quota display formatting for 5h and weekly windows
- official OpenAI quota refresh success mapping
- official OpenAI quota unauthorized handling
- compatible-account CSV secret import
- OAuth CSV default secret exclusion

## Manual Test Commands

Use the project-root scripts. Do not assume the generated `bin\Debug\net8.0-windows\CodexBar.Win.exe` can be run directly on machines without a suitable .NET runtime.

Open Settings:

```powershell
.\run-win.ps1 --settings
```

Start tray / main window:

```powershell
.\run-win.ps1
```

CLI config check:

```powershell
.\run-cli.ps1 config show
```

Create portable package:

```powershell
.\package.ps1
```

Usage summary and attribution check:

```powershell
.\run-cli.ps1 scan-accounts
```

Inspect Codex Desktop / CLI detection:

```powershell
.\run-cli.ps1 locate-codex
```

Preview OpenAI aggregate-gateway routing decision:

```powershell
.\run-cli.ps1 resolve-openai
```

Fetch OpenAI official plan/quota snapshots:

```powershell
.\run-cli.ps1 refresh-openai-usage
```

Enable aggregate mode in app config:

```powershell
.\run-cli.ps1 config set --openai-account-mode aggregate-gateway
```

## Recent Change Log

### 2026-04-20

- Bumped project version metadata to `0.2.0` and aligned the current release candidate around the native-window rebuild track.
- Added `docs/NATIVE_WINDOW_REBUILD.md` to lock the Windows-native runtime model to tray host + `MainFlyout` + independent `Overlay` + popup dialogs.
- Reworked `CodexBar.Win` so tray state, main flyout, overlay, and settings window are coordinated from the native app shell instead of being treated as unrelated test windows.
- Added a native `OverlayWindow` with shared active-account state, compact/expanded presentation, refresh, and launch actions.
- Refreshed the native `FlyoutWindow` layout around the Figma interaction model and wired popup windows as independent dialog surfaces instead of taskbar-style pages.
- Added `Edit Account` popup dialog and connected it end-to-end (main flyout edit action -> API update/probe path).
- Kept popup-window interaction model for `Settings`, `OAuth`, `Add Compatible Provider`, and `Edit Account` (no route-page replacement).
- Added single-instance command forwarding so `--open`, `--overlay`, and `--settings` are handled by the running primary instance instead of being limited to cold start.

### 2026-04-18

- Refactored `README.md` to focus on software users and moved thread-collaboration details out of the main readme.
- Added GitHub push rules to `docs/THREAD_WORKFLOW.md`.
- Added a reusable prompt that tells the main thread how future pushes should be gated.
- Bumped project version metadata to `0.1.3` to keep workflow/versioning in sync.
- Moved `Codex Provider ID` into an advanced compatibility section in the add/edit Provider UI so the default third-party API path stays simpler.
- Completed final packaged manual verification for `third-party API -> open existing history session -> restore chat`, and confirmed new chats still work in the latest portable build.
- Recorded the `v0.1.3` sync checkpoint with remote `main` at commit `451cd04` and a clean `codexbar-win` working tree at that checkpoint.
- Recorded that local tag `v0.1.3` still exists only locally and must be checked as a separate push item before closing the release.
- Updated the main-thread release checklist so remote `main` and version-tag push status are tracked separately.
- Bumped project version metadata to `0.1.2` and aligned package naming/documentation with the new candidate version.
- Generated portable package artifacts for `v0.1.2`.

### 2026-04-17

- Added `docs/THREAD_WORKFLOW.md` to formalize main-thread vs feature-thread collaboration rules.
- Added a reusable main-thread prompt and a recommended feature-thread prompt.
- Documented standard handoff format, review flow, and release-check workflow for multi-thread development.
- Bumped project version metadata to `0.1.1` to keep documentation/versioning in sync.
- Added Last 7 Days token totals to the usage dashboard and CLI account scan output.
- Hardened usage scanning so locked active session files do not fail the main flyout refresh.
- Fixed compatible-provider activation to use Codex `auth_mode = apikey` and native `[model_providers.<id>]` entries.
- Fixed compatible-provider activation to preserve the existing OAuth identity snapshot when available, reducing missing-history behavior in Codex Desktop after switching to third-party APIs.
- Split compatible-provider internal IDs from Codex-facing provider IDs, defaulting compatible Codex-facing IDs to `openai` to fit Desktop history filtering.
- Switched compatible-provider `openai` aliasing to top-level `openai_base_url` instead of forbidden `[model_providers.openai]`.
- Fixed compatible-provider launch so the active API key is injected into launched Codex processes as `OPENAI_API_KEY`.
- Added compatible-provider Provider ID and Codex Provider ID editing with local journal and secret-reference migration.
- Added compatible-provider API connectivity probing to the main flyout and CLI.
- Cleaned inherited Codex/Electron internal environment variables before starting Codex Desktop from the GUI.
- Updated Codex Desktop discovery to prefer the newest WindowsApps / MSIX package when settings still point at an older versioned path.
- Added `package.ps1` for reproducible portable Windows packaging with a bundled local `.dotnet` runtime.
- Added visible action-status feedback in the main flyout for refresh, switch, save, delete, and launch flows.
- Disabled main-flyout action controls while refresh, switch, save, delete, and launch operations are in progress.
- Prevented background auto-refresh from queueing while the UI is already busy.
- Standardized project version metadata to `0.1.0`.
- Rewrote the public-facing repository documentation (`README.md`, `CHANGELOG.md`) for the initial GitHub upload.
- Collected build outputs, test outputs, and disposable cache folders into `MANUAL_DELETE_BEFORE_UPLOAD_0.1.0`.

## Known Issues And Follow-Ups

- `[!]` Tray icon still uses the default application icon and may be hidden by Windows overflow settings.
- `[!]` Aggregate gateway is currently an activation-time router, not a live local proxy or lease-based gateway.
- `[!]` OpenAI plan/quota snapshots rely on a first-party web endpoint whose response shape may change.
- `[!]` Usage-based sorting and per-account attribution depend on successful switch journal records; older historical sessions can remain unattributed.
- `[!]` Usage scanner reports tokens, but real cost estimation is still placeholder.
- `[!]` Startup registration should eventually point to a stable packaged launcher rather than a development path.

## Maintenance Rule

For every meaningful feature change, keep these files in sync:

- `Directory.Build.props`
- `README.md`
- `CHANGELOG.md`
- `docs/IMPLEMENTATION_PROGRESS.md`
