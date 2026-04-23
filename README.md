[![ZH-CN](https://img.shields.io/badge/lang-ZH--CN-0F6CBD)](./README.md)
[![English](https://img.shields.io/badge/lang-English-0F6CBD)](./README.en.md)

# CodexBar for Windows

当前版本：`v0.3.0`

CodexBar for Windows 是 macOS 项目 [`lizhelang/codexbar`](https://github.com/lizhelang/codexbar) 的 Windows 原生移植版。它的目标不是重做 Codex，而是在 Windows 上提供一个更顺手的账号与 Provider 切换入口，让你在**不拆分本地 `.codex` 历史池**的前提下管理 OpenAI 官方账号和第三方兼容接口。

一句话说明：

**一个Windows托盘工具，可以在切换codex账号/第三方API的同时，不丢session会话历史记录，并使用小浮窗支持额度/用量管理。**

## 适合谁

- 你在 Windows 上使用 Codex Desktop 或 Codex CLI
- 你希望在多个 OpenAI 账号之间快速切换
- 你需要接入 OpenAI-compatible Provider / 第三方 API 中转站
- 你不想为了切账号而拆出多个独立 `~/.codex`

## 界面预览

CodexBar for Windows 的日常交互主要围绕两类界面展开：

- **主浮窗**：集中承载账号 / Provider 管理、启动 Codex、查看当前激活对象与进入设置，是完整的高频操作入口。
- **小浮窗**：作为轻量常驻视图，适合快速确认当前激活的 Provider、usage 摘要和最近刷新状态；展开后还能继续查看更细的 usage 明细与当前模式。

| 主浮窗 | 小浮窗（默认） | 小浮窗（展开） |
| --- | --- | --- |
| ![主浮窗界面](docs/images/preview-main-window.png) | ![小浮窗默认视图](docs/images/preview-mini-window-collapsed.png) | ![小浮窗展开视图](docs/images/preview-mini-window-expanded.png) |

## 快速开始

### 方式一：推荐使用便携包（下载后即用）

如果你只是想直接使用 CodexBar，推荐优先使用便携包，直接去 release 下载 `CodexBar-portable-win-x64-v0.3.0.zip`。

拿到压缩包后，按下面 3 步即可开始使用：

1. 解压 `CodexBar-portable-win-x64-v0.3.0.zip`
2. 进入解压后的目录
3. 双击 `start-codexbar.cmd`

说明：

- 如果本地已经安装了`.NET 8 SDK`，也可以直接双击`CodexBar.Win.exe`打开
- 便携包内已经带有本地 `.NET`，运行时，不需要额外安装全局 `.NET`
- 首次启动后，CodexBar 会以托盘工具形式常驻；如果没看到主窗口，请留意系统托盘区
- 如果你想先配置账号、Provider 或 Overlay，直接双击 `open-settings.cmd`

### 方式二：从源码直接运行

如果你是开发者或正在本地验证仓库，可以直接运行：

构建：

```powershell
.\build.ps1
```

启动主程序：

```powershell
.\run-win.ps1
```

打开设置：

```powershell
.\run-win.ps1 --settings
```

如果机器已经安装全局 `.NET 8 SDK`，也可以使用：

```powershell
dotnet run --project .\src\CodexBar.Win\CodexBar.Win.csproj
```

## 核心能力

- 管理多个 OpenAI OAuth 账号
- 管理多个 OpenAI-compatible Provider 和多组 API Key
- 切换账号时原子写入 `config.toml` / `auth.json`
- 保持共享 `sessions` / `archived_sessions` 历史池不被拆分，即不丢历史记录
- 查看本地 usage 统计（今日 / 近 7 天 / 近 30 天 / 累计）
- 只读刷新 OpenAI 官方套餐 / 额度信息
- 从 GUI 直接启动或确认重启 Codex，并在兼容 Provider 场景下注入当前 API Key
- 从托盘右键菜单快速查看当前账号/API，并直接切换到任意账号/API
- 在设置页导出 / 导入账号配置和历史会话 ZIP
- 支持基础托盘交互、分页设置页、OAuth 登录窗口和兼容 Provider 管理窗口

## 兼容性承诺

这是这个项目最重要的行为边界：

- 共用同一个 `CODEX_HOME` / `~/.codex`
- 共用同一个 `sessions` 和 `archived_sessions`
- 切换时只更新当前激活态的 `config.toml` 和 `auth.json`
- 不复制历史、不重写历史、不按账号拆分 `.codex`

## 环境依赖
1. `.NET 8 SDK x64`，已经被打包在便携包里面了
2. `Node.js + npm`，可以让codex帮你装

## 常见使用方式

### 1. 使用 OpenAI 官方账号

1. 打开设置页
2. 选择登录 OpenAI
3. 在浏览器里完成 OAuth 授权
4. 回到 CodexBar 选择目标账号并激活
5. 从 CodexBar 启动 Codex

如果浏览器回调没有自动完成，也可以继续使用手工粘贴 callback URL / `code` 的 fallback。

### 2. 接入第三方兼容 Provider

在“添加兼容 Provider”窗口中，通常需要填写：

- `Provider ID`
- `Base URL`
- `账号 ID`
- `API Key`

说明：

- `Base URL` 一般填写到 OpenAI 兼容接口根路径，例如 `https://api.example.com/v1`
- 如果不确定地址是否正确，可以在主面板点击“探测 API”
- 切换到兼容 Provider 并从 CodexBar 启动 Codex 时，当前 API Key 会注入到新启动的 Codex 进程
- 为了尽量保持历史会话可见性，兼容 Provider 激活时会尽可能保留现有 OpenAI OAuth 身份快照

### 3. 查看 usage 和套餐信息

当前版本支持：

- 本地 usage 扫描：今日 / 近 7 天 / 近 30 天 / 累计
- OpenAI 官方套餐与剩余额度只读刷新

这些信息更适合帮助你判断当前该切到哪个账号，而不是作为精确计费系统使用。

### 4. 导出 / 导入历史会话

设置页提供“历史会话 ZIP”导出和导入：

- 导出包名默认为 `codexbar-history-*.zip`
- 只包含 `sessions/`、`archived_sessions/` 和 `session_index.jsonl`
- 不包含 `config.toml`、`auth.json`、账号 CSV、OAuth token 或 API Key
- 导入会合并历史：同内容跳过，路径冲突时另存为 `.imported-N.jsonl`
- 导入不会改变当前激活账号，只影响 Codex 可恢复的历史会话列表
- 建议先关闭正在运行的 Codex，再导入历史包，避免外部进程同时写入索引

CLI 同步提供：

```powershell
dotnet run --project .\src\CodexBar.Cli\CodexBar.Cli.csproj -- export-history --path .\codexbar-history.zip
dotnet run --project .\src\CodexBar.Cli\CodexBar.Cli.csproj -- import-history --path .\codexbar-history.zip
```

## 使用注意事项

- **切换只影响新会话。** 已经在运行中的 Codex 进程不会被强行改写。
- **如果 Codex Desktop 已经打开，主浮窗启动路径会先确认重启。** 确认后会关闭当前窗口和后台进程，再启动新的 Codex；环境变量只会进入新进程。
- **如果机器没有全局 `.NET`，不要直接双击 `bin` 目录里的 exe。** 优先用便携包里的启动脚本，或使用仓库脚本启动。
- **兼容 Provider 的连通性探测基于 `/models`。** 如果探测失败，先检查 `Base URL` 是否缺少 `/v1`。
- **本地 API 的浏览器访问只信任受控 loopback origin。** 当前只允许 `http://127.0.0.1:5057` / `http://localhost:5057` / `http://127.0.0.1:5173` / `http://localhost:5173` / `http://127.0.0.1:4173` / `http://localhost:4173`；这样保留本地 API 自身和前端重建开发/预览入口，同时阻止任意网页跨站读写本地 API。

## 开发者补充

如果你是来参与开发或查看实现细节，建议直接看：

- 详细变更：[CHANGELOG.md](./CHANGELOG.md)
- 实现状态：[docs/IMPLEMENTATION_PROGRESS.md](./docs/IMPLEMENTATION_PROGRESS.md)
- 原生窗口迁移说明：[docs/NATIVE_WINDOW_REBUILD.md](./docs/NATIVE_WINDOW_REBUILD.md)
- 协作 / 交接 / 发布规则：[docs/THREAD_WORKFLOW.md](./docs/THREAD_WORKFLOW.md)

## 致谢

本项目的 Windows 版本移植工作，基于原始 macOS 项目 [`lizhelang/codexbar`](https://github.com/lizhelang/codexbar) 的产品思路与实现探索推进，在此致谢。

## 版本更新摘要

`README.md` 只保留相对上个版本的简要说明，详细变更请看 [CHANGELOG.md](./CHANGELOG.md)。

### v0.3.0 - 2026-04-23

- 主浮窗“切换 / 启动”流程升级：切换只影响新会话；检测到 Codex Desktop 已运行时会确认后关闭窗口和后台进程，再按当前账号重启
- 托盘右键菜单新增快速选择账号 / API，官方账号显示精简额度，例如 `账号名 (50%/90%)`
- 设置页改为左侧分页导航，新增“关于”、自动打开小浮窗、恢复重启确认弹窗等入口
- 新增历史会话 ZIP 导出 / 导入，只迁移 `sessions`、`archived_sessions` 和 `session_index.jsonl`，不触碰账号凭据
- 主浮窗细节同步优化：顶部按钮在启动过程中保持可用，额度标题按实际套餐显示，账号操作文案统一为“切换”

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
