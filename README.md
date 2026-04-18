# CodexBar for Windows

当前版本：`v0.1.3`

这是 macOS 项目 [`lizhelang/codexbar`](https://github.com/lizhelang/codexbar) 的 Windows 原生移植版。它不是重建 Codex，也不是逐文件翻译 Swift/macOS 代码，而是围绕 Codex 的本地配置切换、OpenAI OAuth、多账号管理、兼容 Provider、usage 统计和托盘入口做的 Windows 工具。

## 项目目标

这个项目最核心的目标只有一条：

在**不拆分共享 `.codex` 历史池**的前提下，安全切换当前激活的 provider/account。

也就是说：

- 共用同一个 `CODEX_HOME` / `~/.codex`
- 共用同一个 `sessions` 和 `archived_sessions`
- 切换账号时只更新当前活跃的 `config.toml` 与 `auth.json`
- 不复制历史、不重写历史、不按账号创建独立 `.codex`

## 兼容性约束

本项目默认遵守以下兼容规则：

- 优先读取 `CODEX_HOME`
- 如果没有设置 `CODEX_HOME`，则回退到 `%USERPROFILE%\.codex`
- 历史目录只读使用：
  - `~/.codex/sessions`
  - `~/.codex/archived_sessions`
- 账号切换只影响新会话，不回写旧会话
- OpenAI OAuth 使用外部浏览器 + localhost 回调
- 自动回调失败时支持手工粘贴完整 callback URL 或单独 `code`

## 当前已实现

- Windows 托盘宿主
- 主面板、设置页、OAuth 窗口、兼容 Provider 窗口、编辑账号窗口
- OpenAI OAuth 登录与多 OpenAI 账号保存
- OpenAI-compatible Provider 管理
- 同一 Provider 下挂多组 API Key
- OpenAI-compatible Provider 连通探测与 `/v1` Base URL 提示
- 账号切换时原子写入 `config.toml` / `auth.json`
- Windows Credential Manager 持久化 token / API key
- 兼容 Provider 启动 Codex 时自动注入 `OPENAI_API_KEY`
- 兼容 Provider 激活时保留现有 OpenAI OAuth 身份快照，尽量维持 Codex 历史会话可见性
- usage 扫描与统计：
  - 今日
  - 近 7 天
  - 近 30 天
  - 累计
- OpenAI 官方套餐 / 剩余额度只读刷新
- OpenAI 聚合网关模式的激活时路由
- GUI 中直接“启动 Codex”（会优先定位最新的 WindowsApps / MSIX Desktop 包）
- GUI 操作状态反馈：
  - 正在刷新
  - 正在切换账号
  - 正在启动 Codex
  - 完成 / 失败结果提示
- 操作进行中，主面板相关按钮会临时禁用
- 主面板打开期间每 1 分钟自动刷新 usage / 面板状态

## 当前未完成或延后

- GitHub Releases 更新检测
- 自更新 / 安装器
- 真正的实时代理型聚合网关
- 更完整的成本估算
- 最终版 UI 重构

## 仓库结构

- `src/CodexBar.Core`
  - 核心模型、应用配置、switch journal、通用接口
- `src/CodexBar.CodexCompat`
  - `CODEX_HOME`、TOML / auth 写入、事务回滚、usage 扫描
- `src/CodexBar.Auth`
  - OpenAI OAuth、PKCE、localhost 回调、Credential Manager
- `src/CodexBar.Runtime`
  - 单实例、自启动、Codex 路径探测、日志
- `src/CodexBar.Cli`
  - CLI 诊断与测试入口
- `src/CodexBar.Win`
  - WPF 托盘与窗口壳层
- `tests/CodexBar.Tests`
  - 控制台测试项目
- `docs`
  - 进度文档和项目说明

## 协作工作流

这个项目默认采用“`main thread` 管主线，`feature thread` 管开发”的协作方式。

- `main thread`
  - 负责路线图、优先级、版本规划、审查和发布准备
  - 默认不直接改业务代码，不承担日常实现测试
- `feature thread`
  - 负责单个功能的实现、测试、手动验证和回交
  - 每个 thread 尽量只做一个问题域

详细规则、交接模板和可直接复用的 prompt 见：

- `docs/THREAD_WORKFLOW.md`

以后是否允许推送、何时推送、由谁推送，也统一按这个工作流文档执行，默认由 `main thread` 做最终发布和推送决策。

## 运行环境

### 一、源码运行 / 未编译版

必需：

- Windows 10 或 Windows 11
- PowerShell 5.1+ 或 PowerShell 7+
- 以下二选一：
  - 已安装全局 `.NET 8 SDK`
  - 你自己准备好的本地 `.dotnet` 目录
- 默认浏览器可正常打开 OpenAI OAuth 登录页面

推荐：

- 已安装 Codex Desktop 或 Codex CLI
- 能访问 OpenAI 登录与官方额度接口

### 二、已编译版本

当前版本 `0.1.3` 已提供仓库内可复现的便携打包脚本：

```powershell
.\package.ps1
```

默认产物位置：

- 目录包：`artifacts\package\CodexBar-portable-win-x64-v0.1.3\`
- 压缩包：`artifacts\package\CodexBar-portable-win-x64-v0.1.3.zip`

#### 1）推荐：运行打包后的便携版本

优点：

- 不依赖机器全局安装 `.NET`
- 包内自带本地 `.dotnet` 运行时
- 推荐直接使用 `start-codexbar.cmd` 与 `open-settings.cmd`

#### 2）运行仓库里的构建输出

必需：

- Windows 10 或 Windows 11
- 如果机器没有全局 `.NET`，则需要本地 `.dotnet` 运行时

建议：

- 优先通过这些入口启动，而不是直接双击 `bin` 里的 exe：
  - `start-codexbar.cmd`
  - `run-win.ps1`
  - `open-settings.cmd`

说明：

- 当前仓库形态下，不要假设任意机器都能直接双击 `src\CodexBar.Win\bin\...\CodexBar.Win.exe` 成功运行
- 没有全局 `.NET` 时，应优先使用仓库脚本或自行准备本地 `.dotnet`

#### 3）后续正式发布版本

建议：

- 可以继续做真正的 `self-contained publish`
- 后续再补 installer（例如 MSIX / MSI）

正式发布版的理想目标应该是：

- 用户不需要预装全局 `.NET`
- 用户双击即可运行

## 快速开始

### 使用全局 .NET 8 SDK

构建：

```powershell
dotnet build .\CodexBar.Win.sln
```

测试：

```powershell
dotnet run --project .\tests\CodexBar.Tests\CodexBar.Tests.csproj
```

启动主程序：

```powershell
dotnet run --project .\src\CodexBar.Win\CodexBar.Win.csproj
```

CLI 帮助：

```powershell
dotnet run --project .\src\CodexBar.Cli\CodexBar.Cli.csproj -- help
```

### 使用仓库内本地运行时

构建：

```powershell
.\build.ps1
```

测试：

```powershell
.\test.ps1
```

启动主程序：

```powershell
.\run-win.ps1
```

打包便携发布包：

```powershell
.\package.ps1
```

打开设置：

```powershell
.\run-win.ps1 --settings
```

CLI 帮助：

```powershell
.\run-cli.ps1 help
```

## 手动测试建议

### 1. 验证 Codex 路径探测

```powershell
.\run-cli.ps1 locate-codex
```

如果设置里保存的是旧版 `WindowsApps` 路径，当前版本会自动优先切到最新安装的 Codex Desktop 包。

### 2. 验证 usage 扫描

```powershell
.\run-cli.ps1 scan-accounts
```

### 3. 验证 OpenAI 聚合路由

```powershell
.\run-cli.ps1 resolve-openai
```

### 4. 验证 OpenAI 官方额度刷新

```powershell
.\run-cli.ps1 refresh-openai-usage
```

### 5. 验证兼容 Provider 连通性

```powershell
.\run-cli.ps1 probe-compatible
```

## 第三方 API 中转站接入说明

在 GUI 的“添加兼容 Provider”窗口里：

必填项：

- `* Provider ID`
- `* Base URL`
- `* 账号 ID`
- `* API Key`

非必填项：

- `Provider 名称（可选）`
- `账号显示名（可选）`

说明：

- 对第三方服务商来说，真正外部提供的一般是 `Base URL` 和 `API Key`
- 但在本工具里，`Provider ID` 和 `账号 ID` 是本地唯一标识，也属于必填
- `Provider ID` 是 CodexBar 内部主键，不能和官方 `openai` Provider 重名；第三方 API 默认通过 `Codex Provider ID = openai` 写入 Codex，以适配 Codex Desktop 的历史过滤
- 当 `Codex Provider ID = openai` 时，CodexBar 不会写 `[model_providers.openai]`，而是写顶层 `openai_base_url`，避免触发 Codex 的内置 Provider 保留 ID 校验
- 如果可选显示名留空，会自动回退为对应的 ID
- `Base URL` 通常需要填写到 OpenAI 兼容接口根路径，例如 `https://api.example.com/v1`
- 如果不确定中转站路径是否正确，可以在主面板选中账号后点击“探测 API”
- 切换到兼容 Provider 并从 GUI 启动 Codex 时，CodexBar 会从 Windows Credential Manager 读取该账号的 API Key，并作为 `OPENAI_API_KEY` 注入到新启动的 Codex 进程
- 切换到兼容 Provider 时，CodexBar 会尽量保留当前 `auth.json` 里的 OpenAI OAuth 身份快照，避免只因改成 `apikey` 模式就把现有本地历史会话“看丢”
- 如果 Codex Desktop 已经在运行，请先完全退出 Codex Desktop，再从 CodexBar 启动；环境变量只会进入新进程，重新录入 API Key 不会改变已经运行中的 Codex 进程
- 兼容 Provider 的 `Provider ID` 现在可以在“编辑账号”里修改；如果想让 Codex 按官方 `openai` Provider 显示历史，请保持内部 `Provider ID` 唯一，并把 `Codex Provider ID` 设为 `openai`

## 启动行为说明

- 双击程序默认直接打开主窗口
- 托盘左键点击可以切换主窗口显示
- 如果启用了“开机自启动”，则会使用 `--tray-only`，登录 Windows 时默认静默进入托盘，不强制弹出主窗口

## 仓库整理约定

- 测试产物、构建缓存和临时文件会统一整理到 `MANUAL_DELETE_BEFORE_UPLOAD_*` 目录，方便手动确认后删除
- 本次 `v0.1.0` 首次整理使用的目录名为 `MANUAL_DELETE_BEFORE_UPLOAD_0.1.0`
- `.dotnet`、`bin/`、`obj/`、本地缓存目录都不应作为 GitHub 版本库内容提交
- 功能行为有变更时，除了改代码，也要同步更新：
  - `README.md`
  - `CHANGELOG.md`
  - `docs/IMPLEMENTATION_PROGRESS.md`

## 版本与发布约定

从 `0.1.0` 开始，版本号以根目录的 `Directory.Build.props` 为准。

以后每次发布或上传 GitHub 前，至少同时更新：

- `Directory.Build.props` 里的版本号
- `README.md` 里的当前版本和说明
- `CHANGELOG.md`

如果功能行为有明显变化，也必须同步更新：

- `docs/IMPLEMENTATION_PROGRESS.md`

## 初始版本说明

本次整理后的仓库内容作为初始发布版本：

- `v0.1.0`

对应目标：

- 可以继续本地开发
- 可以上传到 GitHub 作为后续迭代起点
- 代码、文档、版本信息保持同步

本次 `v0.1.3` 主要补充了 GitHub 推送规则，以及 `main thread` / `feature thread` 在发布和推送时的职责约定。
