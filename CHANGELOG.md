# 更新日志

## v0.1.3

补充 GitHub 推送规则与主线发布职责说明。

### 主要内容

- 在 `docs/THREAD_WORKFLOW.md` 中增加推送规则
- 明确 `main thread` 负责最终审查、版本收口和推送决策
- 明确 feature thread 默认不直接推送正式仓库
- 补充可直接写给 `main thread` 的推送规则 prompt

## v0.1.2

第三方 API 稳定性补丁版本，重点修复兼容 Provider 启动链路、历史会话兼容与账号编辑体验。

### 修复

- usage 扫描遇到正在被 Codex 写入并锁定的 session 文件时，不再导致主面板整次刷新失败。
- 兼容 Provider 激活时写入 Codex 兼容的 `auth_mode = apikey` 登录态，并根据目标 provider 选择合适的配置写法。
- 从 GUI/CLI 启动兼容 Provider 时会把当前账号密钥注入为子进程 `OPENAI_API_KEY`，修复 Codex 提示缺少环境变量的问题。
- 兼容 Provider 激活时会保留现有 OpenAI OAuth 身份快照，减少切到第三方 API 后历史会话列表丢失的问题。
- 兼容 Provider 现在拆分 CodexBar 内部 Provider ID 与写入 Codex 的 Provider ID，默认以 `openai` 写入 Codex 来适配 Desktop 历史过滤。
- 当兼容 Provider 映射到 Codex 内置 `openai` 时，改写顶层 `openai_base_url`，不再生成被 Codex 拒绝的 `[model_providers.openai]`。
- 从 GUI 启动 Codex Desktop 时清理继承的 Codex/Electron 内部环境变量，避免 Desktop 被误当作内部子进程启动。
- Codex Desktop 路径探测会优先解析最新的 WindowsApps / MSIX 安装版本，不再被旧版本包路径卡住。

### 新增

- 主面板增加“探测 API”按钮，可检查兼容 Provider 的 `/models` 连通性，并提示常见的 `/v1` Base URL 修正。
- 增加 `package.ps1`，可生成包含本地 `.dotnet` 运行时的便携 Windows 发布目录和 zip 压缩包。
- 兼容 Provider 账号编辑现在支持修改 `Provider ID` 和 `Codex Provider ID`，并同步迁移本地切换日志与密钥引用。

## v0.1.1

补充项目级 thread 协作工作流文档。

### 主要内容

- 新增 `docs/THREAD_WORKFLOW.md`
- 写明 `main thread` 与 `feature thread` 的职责边界
- 写明 thread 之间的交接信息模板
- 增加可直接复用的 `main thread` prompt
- 在 README 中补充项目协作工作流说明

## v0.1.0

初始 Windows 版本整理发布。

### 主要内容

- 完成 CodexBar Windows 原生 MVP 迁移
- 保持共享 `.codex` 历史池语义
- 支持 OpenAI OAuth 登录与多账号管理
- 支持 OpenAI-compatible Provider 与多 API Key
- 支持原子写入 `config.toml` / `auth.json`
- 支持 OpenAI 官方额度只读刷新
- 支持 usage 扫描与今日 / 7 日 / 30 日 / 累计统计
- 支持主面板直接启动 Codex
- 支持双击程序直接打开主窗口
- 支持主面板操作状态反馈与忙碌时临时禁用按钮
- 完成中文 README、版本初始化与项目整理
- 已将测试产物和缓存归拢到待手动删除目录，便于首次上传 GitHub
