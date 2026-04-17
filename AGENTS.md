# AGENTS.md

## Project goal
Port the macOS project `codexbar` into a Windows-native utility while preserving its core behavior.

## Non-negotiable compatibility requirements
- Do not split the shared `~/.codex` history pool.
- Preserve the semantics of shared `sessions` and `archived_sessions`.
- Only switch the active provider/account state reflected in `config.toml` and `auth.json`.
- Switching must affect new sessions only; do not assume historical sessions should be rewritten.
- Preserve browser-based OpenAI OAuth with localhost callback capture and a manual paste fallback.

## Working style
- Prefer architecture extraction over line-by-line translation from Swift.
- Separate platform shell code from portable domain logic.
- Propose an MVP first, then defer non-critical features.
- Before implementing UI, define service boundaries and state models.
- When uncertain, produce a migration spec before writing code.

## Expected deliverables
- Windows migration blueprint
- Module boundaries
- State/data model
- MVP plan
- Test plan