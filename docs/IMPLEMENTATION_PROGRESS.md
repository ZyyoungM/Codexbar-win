# CodexBar Windows Implementation Progress

Last updated: 2026-04-17

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

The current implementation is a working MVP with a deliberately simple WPF shell for manual testing. Backend services are intended to survive later UI replacement.

## Feature Status

| Area | Status | Notes |
| --- | --- | --- |
| Shared `.codex` compatibility | `[x]` | Resolves `CODEX_HOME`, falls back to `%USERPROFILE%\.codex`, and recognizes `config.toml`, `auth.json`, `sessions`, and `archived_sessions`. |
| Shared history pool preservation | `[x]` | Activation updates active config/auth state only. Tests assert session files are not rewritten. |
| TOML / auth compatibility | `[x]` | Lightweight TOML editing preserves unrelated content. OAuth `auth.json` output now includes Codex-required top-level `last_refresh`. |
| Atomic activation write | `[x]` | `config.toml` and `auth.json` are written through transaction/rollback flow. |
| OpenAI OAuth browser flow | `[x]` | PKCE, browser auth, localhost callback capture, and manual callback/code fallback are wired. |
| OpenAI OAuth account naming | `[x]` | Account label/email/sub are backfilled from `id_token` when available. |
| OpenAI official plan / quota snapshot | `[~]` | Read-only refresh works for OAuth accounts. UI shows remaining quota, next reset time, refresh timestamp, and refresh failures. Source endpoint is official-hosted but undocumented. |
| Multiple OpenAI OAuth accounts | `[x]` | Multiple OpenAI OAuth accounts can be stored, displayed, switched, and re-activated. |
| OpenAI-compatible providers | `[x]` | Provider ID/name/base URL plus account/API key are supported. |
| Multiple API keys under same provider | `[x]` | Supported by reusing provider ID with different account IDs. |
| Account edit UI | `[x]` | Temporary UI can edit labels; compatible providers can edit name/base URL/API key. |
| Manual account ordering | `[x]` | `ManualOrder` is persisted and temporary UI supports Up/Down. |
| Usage-based ordering | `[~]` | Ordering prefers official OpenAI quota pressure when present, then local usage history. Attribution still depends on recorded successful switch journal entries. |
| CSV export/import | `[x]` | Backend and temporary Settings UI exist. Default export excludes secrets; explicit secret export is supported. |
| Windows Credential Manager | `[x]` | API keys and OAuth tokens are persisted through Credential Manager. |
| Local usage scanner | `[~]` | Scans `sessions` and `archived_sessions`, exposes Today / Last 7 Days / Last 30 Days / Lifetime totals, attributes sessions by switch intervals, and auto-refreshes every minute while the main flyout is open. Cost pricing is still placeholder. |
| Locked session file handling | `[x]` | Scanner uses shared read and skips unreadable active files. |
| Tray host | `[~]` | Basic tray icon, left-click flyout, right-click menu, settings entry, and Launch Codex action exist. Icon/art and packaging still need work. |
| Temporary WPF shell | `[x]` | Main, Settings, OAuth, Add Compatible, and Edit windows are resizable and scrollable. UI is localized to Chinese for easier manual testing. |
| Main flyout action feedback | `[x]` | Refresh / switch / save / delete / launch flows show in-progress and completion/failure feedback. Controls are temporarily disabled while operations are running. |
| Startup registration | `[~]` | Registry startup toggle exists. Current startup target still depends on development-style executable layout. |
| Codex Desktop / CLI path settings | `[x]` | Config model, CLI, Settings UI, detection, and launch fallback are wired. GUI launch re-syncs active account into `config.toml` / `auth.json` before starting Codex. |
| Activation behavior setting | `[x]` | `WriteConfigOnly` and `LaunchNewCodex` are both wired. |
| OpenAI manual / aggregate mode | `[~]` | `ManualSwitch` works directly. `AggregateGateway` currently performs activation-time OpenAI auto-routing; it is not yet a live request proxy. |
| GitHub Releases update check | `[ ]` | Not implemented. |
| Real installer / self-contained publish | `[ ]` | Not implemented. Current launch scripts are development/test aids. |

## Verification Status

Latest verified commands:

```powershell
.\test.ps1
.\run-cli.ps1 help
```

Latest known result:

```text
tests: 22 passed
cli: help command runs successfully
```

Current automated coverage includes:

- `CODEX_HOME` resolution
- duplicate environment key casing such as `Path` / `PATH`
- TOML preservation
- compatible-provider activation
- OAuth activation writes Codex-compatible top-level `last_refresh`
- transaction rollback
- OAuth manual callback parsing
- usage scanner read-only behavior
- usage attribution by switch intervals
- aggregate-gateway reroute behavior
- aggregate-gateway official-quota preference behavior
- aggregate-gateway manual-mode passthrough
- aggregate-gateway healthy-account preference over reauth-needed accounts
- launch-service write-only skip behavior
- launch-service desktop start path
- manual account order persistence
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

### 2026-04-17

- Added Last 7 Days token totals to the usage dashboard and CLI account scan output.
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
