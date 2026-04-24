[![ZH-CN](https://img.shields.io/badge/lang-ZH--CN-0F6CBD)](./README.md)
[![English](https://img.shields.io/badge/lang-English-0F6CBD)](./README.en.md)

# CodexBar for Windows

Current version: `v0.3.2`

CodexBar for Windows is a Windows-native port of the macOS project [`lizhelang/codexbar`](https://github.com/lizhelang/codexbar). The goal is not to rebuild Codex itself, but to provide a smoother Windows entry point for switching accounts and providers while letting you manage official OpenAI accounts and third-party compatible APIs **without splitting the local `.codex` history pool**.

One-line summary:

**A Windows tray utility that lets you switch Codex accounts or third-party APIs without losing session history, with a compact overlay for quota and usage management.**

## Who Is It For

- You use Codex Desktop or Codex CLI on Windows
- You want to switch quickly between multiple OpenAI accounts
- You need to connect OpenAI-compatible providers or third-party API gateways
- You do not want to split `~/.codex` into multiple isolated copies just to switch accounts

## UI Preview

The day-to-day experience of CodexBar for Windows mainly revolves around two UI surfaces:

- **Main flyout**: the primary high-frequency entry point for account/provider management, launching Codex, checking the active target, and opening settings.
- **Mini overlay**: a lightweight always-available view for quickly checking the active provider, usage summary, and latest refresh state; when expanded, it also shows more detailed usage information and the current mode.

| Main flyout | Mini overlay (default) | Mini overlay (expanded) |
| --- | --- | --- |
| ![Main flyout UI](docs/images/preview-main-window.png) | ![Mini overlay collapsed view](docs/images/preview-mini-window-collapsed.png) | ![Mini overlay expanded view](docs/images/preview-mini-window-expanded.png) |

## Quick Start

### Option 1: Recommended portable package (download and run)

If you just want to use CodexBar directly, the recommended path is to download `CodexBar-portable-win-x64-v0.3.2.zip` from the release page.

After downloading the archive, you can get started in 3 steps:

1. Extract `CodexBar-portable-win-x64-v0.3.2.zip`
2. Open the extracted folder
3. Double-click `start-codexbar.cmd`

Notes:

- If `.NET 8 SDK` is already installed on the machine, you can also launch `CodexBar.Win.exe` directly
- The portable package already includes a local `.NET` runtime, so you do not need to install a global `.NET` runtime just to run it
- After the first launch, CodexBar will stay resident as a tray utility; if you do not see the main window, check the system tray area
- If you want to configure accounts, providers, or the overlay first, double-click `open-settings.cmd`

### Option 2: Run from source

If you are a developer or you are validating the repository locally, you can run it directly from source:

Build:

```powershell
.\build.ps1
```

Launch the main app:

```powershell
.\run-win.ps1
```

Open Settings:

```powershell
.\run-win.ps1 --settings
```

If the machine already has a global `.NET 8 SDK`, you can also use:

```powershell
dotnet run --project .\src\CodexBar.Win\CodexBar.Win.csproj
```

## Core Capabilities

- Manage multiple OpenAI OAuth accounts
- Manage multiple OpenAI-compatible providers and multiple API keys
- Atomically write `config.toml` / `auth.json` when switching accounts
- Keep the shared `sessions` / `archived_sessions` history pool intact so session history is not lost
- View local usage stats (today / last 7 days / last 30 days / lifetime)
- Refresh official OpenAI plan / quota information in read-only mode
- Launch or explicitly restart Codex from the GUI and inject the active API key for compatible providers
- Quickly view and switch the current account/API from the tray right-click menu
- Export/import account configuration and session-history ZIP archives from Settings
- Support basic tray interactions, paged Settings, the OAuth login window, and the compatible-provider management window

## Compatibility Commitments

This is the most important behavior boundary of the project:

- Share the same `CODEX_HOME` / `~/.codex`
- Share the same `sessions` and `archived_sessions`
- Only update the currently active `config.toml` and `auth.json` on switch
- Do not copy history, rewrite history, or split `.codex` by account

## Environment Requirements
1. `.NET 8 SDK x64`, already bundled in the portable package
2. `Node.js + npm`, Codex can help install it

## Common Usage

### 1. Use an official OpenAI account

1. Open Settings
2. Choose Sign in with OpenAI
3. Complete the OAuth authorization flow in the browser
4. Return to CodexBar, select the target account, and activate it
5. Launch Codex from CodexBar

If the browser callback does not complete automatically, you can still use the manual callback URL / `code` fallback.

### 2. Connect a third-party compatible provider

In the "Add Compatible Provider" window, you usually need to fill in:

- `Provider ID`
- `Base URL`
- `Account ID`
- `API Key`

Notes:

- `Base URL` usually points to the root of an OpenAI-compatible API, for example `https://api.example.com/v1`
- If you are not sure whether the address is correct, click "Probe API" in the main panel
- When you switch to a compatible provider and launch Codex from CodexBar, the active API key is injected into the new Codex process
- To preserve session visibility as much as possible, compatible-provider activation tries to keep the current OpenAI OAuth identity snapshot when available

### 3. View usage and plan information

The current version supports:

- Local usage scanning: today / last 7 days / last 30 days / lifetime
- Read-only refresh of official OpenAI plan and remaining quota information

These numbers are meant to help you decide which account to switch to, not to serve as a precise billing system.

### 4. Export / import session history

The Settings page includes a "History ZIP" export/import flow:

- The default archive name is `codexbar-history-*.zip`
- The archive only contains `sessions/`, `archived_sessions`, and `session_index.jsonl`
- It does not contain `config.toml`, `auth.json`, account CSV data, OAuth tokens, or API keys
- Import merges history: identical files are skipped, and path conflicts are saved as `.imported-N.jsonl`
- Import does not change the currently active account; it only affects the recoverable Codex session list
- Close running Codex processes before importing when possible, so external processes are not writing the index at the same time

The CLI exposes the same flow:

```powershell
dotnet run --project .\src\CodexBar.Cli\CodexBar.Cli.csproj -- export-history --path .\codexbar-history.zip
dotnet run --project .\src\CodexBar.Cli\CodexBar.Cli.csproj -- import-history --path .\codexbar-history.zip
```

## Usage Notes

- **Switching only affects new sessions.** Running Codex processes are not rewritten in place.
- **If Codex Desktop is already open, the main flyout launch path asks before restarting it.** After confirmation, CodexBar closes the current window and background process, then starts a fresh Codex instance; environment variables only flow into new processes.
- **If the machine does not have a global `.NET`, do not double-click the exe under `bin` directly.** Prefer the portable-package launcher scripts or the repository scripts.
- **Compatible-provider connectivity probing is based on `/models`.** If probing fails, first check whether the `Base URL` is missing `/v1`.
- **Browser access to the local API is restricted to trusted loopback origins only.** The current allowlist is `http://127.0.0.1:5057` / `http://localhost:5057` / `http://127.0.0.1:5173` / `http://localhost:5173` / `http://127.0.0.1:4173` / `http://localhost:4173`; this keeps the local API itself and the frontend rebuild dev/preview entry points working while blocking arbitrary web pages from reading or mutating the local API across origins.

## Developer Notes

If you are here to contribute code or inspect implementation details, start with:

- Detailed change log: [CHANGELOG.md](./CHANGELOG.md)
- Implementation status: [docs/IMPLEMENTATION_PROGRESS.md](./docs/IMPLEMENTATION_PROGRESS.md)
- Native window migration notes: [docs/NATIVE_WINDOW_REBUILD.md](./docs/NATIVE_WINDOW_REBUILD.md)
- Collaboration / handoff / release rules: [docs/THREAD_WORKFLOW.md](./docs/THREAD_WORKFLOW.md)

## Acknowledgements

The Windows porting work in this project builds on the product direction and implementation exploration of the original macOS project [`lizhelang/codexbar`](https://github.com/lizhelang/codexbar), and the project is grateful for that foundation.

## Version Summary

`README.md` only keeps a short summary of changes relative to the previous version. For full details, see [CHANGELOG.md](./CHANGELOG.md).

### v0.3.2 - 2026-04-24

- Fixed OpenAI OAuth account saves so multiple logins sharing the same OpenAI `account_id` no longer overwrite earlier local account records
- Preserved OpenAI account id metadata across saves, backfill, quota refresh, and account CSV import/export while keeping the shared `.codex` history pool unchanged
- Refreshed the Windows app icon and icon preview assets for the v0.3.2 release

### v0.3.1 - 2026-04-24

- Fixed a portable-package crash path where launching Codex Desktop from the main flyout could immediately exit; the portable launcher now starts `CodexBar.Win.exe` directly
- Clear additional `.NET` runtime environment variables before starting Codex Desktop so the bundled portable runtime does not leak into child processes
- Regenerated `CodexBar-portable-win-x64-v0.3.1.zip` for the formal patch release

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
