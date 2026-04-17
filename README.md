# CodexBar for Windows

当前版本：`v0.1.0`

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
- 账号切换时原子写入 `config.toml` / `auth.json`
- Windows Credential Manager 持久化 token / API key
- usage 扫描与统计：
  - 今日
  - 近 7 天
  - 近 30 天
  - 累计
- OpenAI 官方套餐 / 剩余额度只读刷新
- OpenAI 聚合网关模式的激活时路由
- GUI 中直接“启动 Codex”
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

当前初始版本 `0.1.0` 还没有完整的 self-contained 安装包，所以需要分两种情况：

#### 1）运行仓库里的构建输出

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

#### 2）后续正式发布版本

建议：

- 做 `self-contained publish`
- 或者把运行时与启动脚本一起打包

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
- 如果可选显示名留空，会自动回退为对应的 ID

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
