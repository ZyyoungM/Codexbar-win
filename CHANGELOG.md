# 更新日志

## v0.3.2

- 修复 OpenAI OAuth 多账号保存时可能互相覆盖的问题：本地账号主键现在会结合登录身份与 OpenAI account id 派生，避免多个登录身份共享同一 `account_id` 时被 `upsert` 覆盖。
- 保存、回填、额度刷新和账号 CSV 导入 / 导出现在都会保留 OpenAI account id 元数据，兼容既有账号记录并避免拆分 shared `.codex` 历史池。
- 刷新 Windows 程序图标与图标预览资源，作为 v0.3.2 的视觉设计更新一起发布。
- 修复测试 helper 在 Windows apphost 下递归启动完整测试集的问题，确保 single-instance 转发测试稳定运行。

## v0.3.1

- 修复便携包通过主浮窗启动 Codex Desktop 后闪退的问题：便携包启动脚本从 `dotnet.exe CodexBar.Win.dll` 改为直接启动 `CodexBar.Win.exe`
- 启动 Codex Desktop 前额外清理 `.NET` 相关环境变量，避免便携包运行时污染子进程并导致 Desktop 闪退
- 补充桌面启动环境回归测试，并重新生成 `CodexBar-portable-win-x64-v0.3.1.zip` 作为正式补丁包

## v0.3.0

- 主浮窗“切换 / 启动”流程升级：先执行仅切换，只更新 `config.toml` / `auth.json` 并明确只影响新会话；切换后按钮进入启动路径。
- 当检测到 Codex Desktop 已运行时，启动路径会弹出精简确认窗；确认后一次性关闭 Codex 窗口并清理后台进程，再按当前账号重启，不静默强杀，且不会误杀 CodexBar。
- 重启确认窗支持“不再提示”，设置页新增“恢复重启确认弹窗”；重启流程减少等待并保持主浮窗顶部按钮可用。
- 任务栏托盘右键菜单新增快速选择账号 / API：显示当前激活项，列出全部账号/API，官方 OpenAI 账号以 `名称 (5h%/周%)` 显示精简额度，点击只执行仅切换；菜单宽度会按账号内容自适应，避免长账号和分组标题被截断。
- 设置页改为左侧导航分页：`运行路径`、`账号排序与行为`、`导出/导入`、`关于`；删除旧说明卡片和“兼容边界”，并统一“账号 CSV”为“导出/导入账号”语义。
- 新增“关于”页，集中展示版本、项目 GitHub 地址、运行环境、兼容性承诺、应用状态目录、日志目录、`CODEX_HOME`、Codex 配置 / 授权路径，并支持打开目录和复制诊断信息。
- 新增启动 CodexBar 时自动打开小浮窗的设置项。
- 设置页和 CLI 新增历史会话 ZIP 导出 / 导入：只迁移 `sessions/`、`archived_sessions/`、`session_index.jsonl`，不包含 `config.toml`、`auth.json`、OAuth token 或 API Key。
- 历史导入采用合并策略：同内容跳过，路径冲突时另存为 `.imported-N.jsonl`，拒绝不安全 ZIP 路径，并在导入前备份 / 合并 `session_index.jsonl`。
- 本地 API 与 `frontend-rebuild` 同步增加历史会话导入 / 导出接口和 UI；主浮窗额度标签改为按实际官方额度展示，账号行文案从“使用”统一为“切换”。

## v0.2.1

- 修复 OpenAI OAuth 手工 fallback 成功后旧 `localhost:1455` loopback 监听未及时取消的问题，避免轮换到下一次登录 flow 时端口仍被占用
- 原生 `OAuthDialog` 现在会在手工完成、取消关闭和窗口关闭时显式释放 loopback 监听，避免桌面弹窗路径残留同类端口占用
- 调整 `README.md` 和便携包内启动说明，明确 `CodexBar-portable-win-x64-v0.2.1.zip` 解压后可直接双击 `start-codexbar.cmd` / `open-settings.cmd` 使用

## v0.2.0

- 收敛原生运行形态为“托盘 + 主浮窗 + 独立 Overlay + 独立弹窗”，明确不走网页后台壳 / 路由页路线
- 新增 `docs/NATIVE_WINDOW_REBUILD.md`，固定窗口层级、弹窗边界与服务接线边界
- `CodexBar.Win` 新增独立 `OverlayWindow`，并由托盘原生协调主浮窗、Overlay 与 Settings 窗口生命周期
- 主浮窗改为围绕 Figma 交互模型的原生布局，保持弹窗独立，不进入任务栏页化路径
- 原生 `MainFlyout` 已重建为 Figma 信息层级：顶部动作区、路由切换、当前激活摘要、可拖动账号卡片与行内操作
- `Add Compatible`、`OAuth`、`Edit Account`、`Settings` 四个弹窗开始统一到新的原生窗口视觉体系，并补上测试连接 / 授权状态 / 设置分区等关键交互
- `Overlay` 开始按紧凑浮窗模型重建为独立悬浮窗：保留拖动、展开/收起、快速刷新与快速启动，同时改成更接近 Figma 的轻量信息层级
- `--open`、`--overlay`、`--settings` 现在会在单实例已运行时转发到主实例处理；`--tray-only` 继续保留给冷启动 / 开机自启场景
- 本地 `CodexBar.Api` 现在只对受控 loopback origin 开放浏览器 CORS：`127.0.0.1/localhost` 上的 `5057`、`5173`、`4173`；`frontend-rebuild` 开发/预览端口同步固定为 `5173/4173`，避免任意网页跨站读写本地 API
- OpenAI OAuth 手工 fallback 现在始终以本次粘贴的 callback URL / `code` 为准，完整 callback 会校验当前登录 `state`，成功保存后会旋转到新的登录尝试，避免旧 token / 旧回调污染下一次登录
- OpenAI OAuth 在轮换到新的登录 flow 前会先显式取消并释放旧的 `localhost:1455` 监听，避免手工 fallback 成功后旧 loopback 监听继续占端口，导致下一次浏览器回调无法被捕获
- 账号排序写入现在要求 payload 覆盖完整账号集且不能重复；partial / stale 排序请求会明确失败，不再静默丢掉未提交账号

## v0.1.3

补充 GitHub 推送规则与主线发布职责说明。

### 主要内容

- 重构 `README.md` 为用户导向文档，把 thread 协作 / 推送规则从主页说明收回到 `docs/THREAD_WORKFLOW.md`
- 在 `docs/THREAD_WORKFLOW.md` 中增加推送规则
- 明确 `main thread` 负责最终审查、版本收口和推送决策
- 明确 feature thread 默认不直接推送正式仓库
- 补充可直接写给 `main thread` 的推送规则 prompt
- 把远端 `main` 与版本 tag 的同步状态拆成独立检查项，避免“代码已推但 tag 未推”

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
