# Main Thread Handoff: OpenAI Workspace Switching and Compatible Token Reset

Version: `v0.3.4`
Date: 2026-05-06
Feature branch: `codex/openai-workspace-switching`
Base before this thread: `4f6ea4b Release v0.3.3 flyout density updates`

## 1. 本次目标

- 支持同一 OpenAI 登录账号下的多个 ChatGPT/Codex workspace 同时收纳、展示、切换和刷新官方额度。
- 修正 Team / Business 等 workspace 在官方套餐、账号显示名和主浮窗标签中的识别。
- 在第三方兼容 API 的编辑账号窗口中增加本地 token 计数重置能力。
- 继续遵守 shared `.codex` 兼容边界：不拆分、不迁移、不重写 `sessions` / `archived_sessions`。

## 2. 实际完成

- OpenAI OAuth 保存逻辑升级为 `OAuth 登录身份 + workspace/account id` 维度；同一邮箱下的 Personal / Team / Business / Enterprise / Edu 可保存为多张 OpenAI 卡片。
- OAuth 登录窗口支持 workspace 发现、选择、重启登录和目标 workspace mismatch 防护；如果官方只返回当前空间，仍保存当前 workspace，并允许后续再次授权添加其它空间。
- `AccountRecord` 新增 workspace 元数据：`WorkspaceId`、`WorkspaceName`、`WorkspaceType`、`SeatType`、`QuotaScopeKey`，并保留 `OpenAiAccountId` 用于官方上下文。
- 激活 OpenAI workspace 时写入目标 workspace/account id 到 `auth.json` 的 `tokens.account_id`，只影响新 Codex 会话。
- 官方额度刷新使用目标 workspace 上下文，带 `ChatGPT-Account-Id`，并保存 quota scope；Team 套餐映射为 `AccountTier.Team`。
- 主浮窗、OAuth 弹窗、账号显示 formatter 和 frontend rebuild 已同步 workspace 名称、Team 标签、shared quota 标记、reauth 状态和相关提示。
- 聚合路由把每个 workspace 当作候选账号，但遇到相同 `QuotaScopeKey` 时避免把同额度池误判为可释放容量。
- CSV 导入 / 导出保留 OpenAI workspace 元数据；默认仍不导出 OAuth token 或 API Key。
- 第三方兼容 API 编辑窗口新增“重置 token 计数”；保存后只写入本地 `TokenCountResetAt`，usage 归因只统计该时间之后开启的 session。
- 本地 API 与 frontend rebuild 增加 `resetTokenCount` 字段，且 OpenAI OAuth 账号会拒绝该字段，避免误用。

## 3. 修改文件

- 版本 / 文档：`Directory.Build.props`、`README.md`、`README.en.md`、`CHANGELOG.md`、`docs/IMPLEMENTATION_PROGRESS.md`、`docs/MAIN_THREAD_HANDOFF_v0.3.4.md`
- OpenAI OAuth / workspace：`src/CodexBar.Auth/*`、`src/CodexBar.Api/OAuthSessionManager.cs`、`src/CodexBar.Api/OAuthSessionDependencies.cs`、`src/CodexBar.Win/OAuthDialog.*`
- 核心模型 / CSV / quota：`src/CodexBar.Core/Models.cs`、`src/CodexBar.Core/AccountCsvService.cs`、`src/CodexBar.Core/OpenAiQuotaPolicy.cs`
- 激活 / usage / aggregate：`src/CodexBar.CodexCompat/CodexActivationService.cs`、`UsageAttributionService.cs`、`OpenAiAggregateGatewayService.cs`、`UsageScanner.cs`
- 原生 WPF UI：`src/CodexBar.Win/MainFlyoutViewModel.cs`、`FlyoutWindow.*`、`EditAccountWindow.*`
- 本地 API / frontend rebuild：`src/CodexBar.Api/FrontendBackendService.cs`、`FrontendContracts.cs`、`frontend-rebuild/src/app/*`
- 回归测试：`tests/CodexBar.Tests/Program.cs`、`tests/CodexBar.Tests/ApiRegressionTests.cs`

## 4. 测试

- 命令：`.\.dotnet\dotnet.exe run --project .\tests\CodexBar.Tests\CodexBar.Tests.csproj`
- 结果：74 项全部 PASS
- 命令：`.\.dotnet\dotnet.exe build .\CodexBar.Win.sln -c Release`
- 结果：成功，0 warnings / 0 errors
- 命令：`npm run build`（目录：`frontend-rebuild`）
- 结果：Vite build 成功；验证产生的 `node_modules` / `dist` 已清理，未保留构建产物
- 命令：`git diff --check`
- 结果：通过，仅 Git 换行提示

## 5. 手动验证

- 用户已反馈：同一 OpenAI 账号下的 Plus / Team workspace 现在能同时收纳，不再只保存最后一次 OAuth。
- 需要 main thread 做发布前 smoke test：重新 OAuth 添加 Personal + Team，确认主浮窗显示多张卡片、Team 标签、官方额度刷新、切换后 `auth.json` 的 `tokens.account_id` 指向目标 workspace。
- 需要 main thread 做发布前 smoke test：编辑第三方兼容 API 账号，点击“重置 token 计数”并保存，确认 usage 归因从保存时间之后重新统计。

## 6. 已知问题 / 延后项

- OpenAI 官方额度接口仍是 read-only best-effort，且官方端点未公开稳定契约；UI 只作为辅助排序和展示，不作为精确账单系统。
- 如果官方 OAuth 回调 / discovery 只返回当前 workspace，CodexBar 只能保存当前空间；用户需要再次授权来添加另一个空间。
- token 计数重置按 session start 时间过滤；如果用户在一个长时间运行的 session 中途重置，该 session 不会被拆开重算。
- `npm ci` 仍报告现有前端依赖树有 1 个 high severity vulnerability；本线程未运行 `npm audit fix --force`，避免引入无关依赖升级。
- 本次没有生成 `CodexBar-portable-win-x64-v0.3.4.zip`，发布包仍需 main thread 在验收后单独打包。

## 7. 发布建议

- 是否建议纳入下个版本：建议纳入 `v0.3.4`。
- 是否需要更新版本号：已从 `v0.3.3` bump 到 `v0.3.4`。
- 是否已更新 README / CHANGELOG / IMPLEMENTATION_PROGRESS：已更新。
- 是否建议直接推送：不建议 feature thread 直接推正式仓库；请 main thread 先做代码审查、两条 smoke test、再决定 package / tag / push。

## 8. Main Thread 下一步

- 审查 workspace key / quota scope / OAuth flow 相关 diff，重点看保存同邮箱多 workspace 是否会覆盖旧记录。
- 做 Personal + Team OAuth smoke test，并检查日志里的 `oauth.openai.save` workspace 字段。
- 做兼容 Provider token reset smoke test。
- 通过后运行 `.\package.ps1` 生成 `v0.3.4` 便携包，再决定是否创建 tag / release。
