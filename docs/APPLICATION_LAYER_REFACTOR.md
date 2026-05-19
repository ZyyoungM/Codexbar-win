# Application Layer Refactor

This refactor moves shared account/config workflows out of UI, API, and CLI entrypoints into `CodexBar.Application`.

## New Boundary

- `AppConfigHydrationService` owns config hydration: OAuth identity backfill, manual-order normalization, official usage refresh, and safe merge-back into the latest saved config.
- `AccountActivationWorkflow` owns account activation: aggregate-gateway resolution, Codex state activation, switch journal writes, active selection persistence, and `LastUsedAt`.
- `AccountDashboardProjectionService` owns account dashboard projection for flyout/account-card state.
- `AccountHealthRefreshWorkflow` owns official quota refresh, compatible provider probing, targeted refresh/probe filtering, and combined quota/API refresh.
- `CompatibleProbeResultApplyService` owns probe-result status merge-back.
- `CompatibleDraftProbeWorkflow` owns draft compatible-provider probe construction.
- `GatewayResolutionWorkflow` owns aggregate-gateway preview/resolve entrypoints.

## Entry Point Responsibilities

- WPF view models should trigger workflows, update view state, and format UI activity messages.
- API handlers should map request/response DTOs and call workflows.
- CLI commands should parse options, call workflows, and print output.
- Shared `~/.codex`, `sessions`, `archived_sessions`, `config.toml`, and `auth.json` semantics remain owned by core/runtime/auth services; application workflows only coordinate those existing services.

## Verification

Validated commands for this refactor:

```powershell
.\.dotnet\dotnet.exe build .\tests\CodexBar.Tests\CodexBar.Tests.csproj --no-restore -p:UseSharedCompilation=false
.\.dotnet\dotnet.exe .\tests\CodexBar.Tests\bin\Debug\net8.0-windows\CodexBar.Tests.dll
.\.dotnet\dotnet.exe build .\CodexBar.Win.sln --no-restore -m:1 -p:UseSharedCompilation=false -p:OutDir=D:\Codex\apps\codexbar-win\artifacts\verify-bin\
git diff --check
```

The explicit `OutDir` avoids false build failures when a running `CodexBar.Win.exe` locks the default Debug output.
