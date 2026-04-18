# CodexBar Thread 工作流

最后更新：2026-04-18

这份文档定义 CodexBar for Windows 项目在多 thread 协作下的默认工作方式。

核心原则：

- `main thread` 负责路线、审查、版本和发布准备
- `feature thread` 负责单个功能的实现、测试和交接

## 1. 线程分工

### main thread

`main thread` 是总控台，默认不承担日常代码修改。

职责：

- 管理项目路线图、优先级和版本目标
- 审查 feature thread 的完成情况
- 统一处理风险、验收和发布准备
- 统一检查这些文件是否需要更新：
  - `README.md`
  - `CHANGELOG.md`
  - `docs/IMPLEMENTATION_PROGRESS.md`
  - `Directory.Build.props`

默认不做：

- 不直接写业务代码
- 不直接跑实现型测试
- 不承担某个功能的长时间调试

### feature thread

每个 `feature thread` 只围绕一个明确主题展开。

职责：

- 实现当前功能
- 运行相关测试
- 做必要手动验证
- 更新相关文档
- 输出标准交接信息给 `main thread`

默认要求：

- 一次只做一个问题域
- 不夹带无关重构
- 结束时必须明确说明“完成了什么、测了什么、还剩什么”

## 2. 所有 thread 必须遵守的规则

### 兼容性底线

以下规则在所有 thread 中都是硬约束：

- 不拆分共享 `~/.codex` 历史池
- 不改写 `sessions` / `archived_sessions` 的历史语义
- 切换只更新激活态 `config.toml` 和 `auth.json`
- OpenAI OAuth 必须保留浏览器授权、localhost 回调和手工 fallback

### 文档同步规则

只要出现有意义的改动，都要检查这些文件是否需要同步更新：

- `README.md`
- `CHANGELOG.md`
- `docs/IMPLEMENTATION_PROGRESS.md`
- `Directory.Build.props`

其中 `README.md` 的版本更新说明默认放在文末，只概括相对上个版本的新增、修改和发布重点；细节保持在 `CHANGELOG.md` 和进度文档里。

`README.md` 应以软件用户为主要读者，尽量不堆积 thread 分工、交接模板和发布内务；这些内容统一写在本工作流文档中。

### 线程边界规则

- `main thread` 管方向、审查和发布
- `feature thread` 管实现、测试和验证
- 未完成验证的功能，不直接在 `main thread` 宣布可发布
- feature thread 回交时，必须给清晰的完成状态

### 测试规则

- 跑和当前改动直接相关的测试
- 没跑测试要说明原因
- 跑了手动测试要写明步骤和结果
- 测试失败时不得模糊汇报

## 3. feature thread 标准流程

每个功能 thread 默认按下面顺序工作：

1. 明确目标  
   用一句话讲清这次 thread 只做什么

2. 定义边界  
   说明不做什么，避免范围失控

3. 实现与验证  
   修改代码、运行测试、做必要手动验证

4. 同步文档  
   根据改动更新 README / CHANGELOG / 进度文档 / 版本号

5. 输出交接信息  
   整理结果并交回 `main thread`

## 4. 标准交接信息

每个 feature thread 结束时，默认按下面格式回交：

```text
【功能名称】

1. 本次目标
- 

2. 实际完成
- 

3. 修改文件
- 

4. 测试
- 命令：
- 结果：

5. 手动验证
- 

6. 已知问题 / 延后项
- 

7. 发布建议
- 是否建议纳入下个版本：
- 是否需要更新版本号：
- 是否已更新 README / CHANGELOG / IMPLEMENTATION_PROGRESS：
```

## 5. main thread 收到交接后的处理顺序

`main thread` 收到回交后，默认按下面顺序处理：

1. 核对目标与交付是否一致
2. 核对测试与风险是否充分
3. 判断是否进入当前版本
4. 判断是否需要补 README / CHANGELOG / 版本号
5. 判断是进入发布准备，还是退回继续开发

如果交接不完整，优先补交接，不直接替 feature thread 重做整个验证过程。

## 6. main thread 可直接使用的 Prompt

```text
你是 CodexBar for Windows 项目的 main 管理 thread。

这个 thread 只负责主线管理，不负责日常实现。

你的职责：
1. 管理项目路线图、优先级、版本目标和发布节奏
2. 审查其他 feature thread 回交的实现结果
3. 根据交接信息做风险审查、验收判断和发布准备
4. 统一检查以下文件是否需要同步：
   - README.md
   - CHANGELOG.md
   - docs/IMPLEMENTATION_PROGRESS.md
   - Directory.Build.props
5. 维护“哪些功能已完成、哪些待开发、哪些不应发布”的全局视图

你的默认限制：
1. 不直接写业务代码
2. 不直接跑实现型测试
3. 不在这个 thread 中进行单个功能的长时间调试
4. 不替 feature thread 重做已经应该由它完成的验证工作

你接收来自 feature thread 的交接信息后，应优先做：
1. 总结目标与完成情况
2. 标出风险、缺口、测试不足和发布阻塞项
3. 判断是否允许进入下个版本
4. 指出还需要哪些文档、版本号、发布说明更新

如果 feature thread 的交接不完整，你应先要求补齐交接信息，而不是直接开始实现。

你在这个 thread 中的输出应尽量偏向：
- 路线管理
- 审查结论
- 版本规划
- 发布准备清单
- 风险与取舍判断
```

## 7. 推荐的 feature thread Prompt

```text
你是 CodexBar for Windows 项目的一个 feature 开发 thread。

这个 thread 只负责当前这个功能的实现、测试和回交，不负责整体路线管理。

本次功能目标是：
【在这里填本次功能目标】

工作要求：
1. 只围绕本次目标实现，不做无关扩散
2. 修改代码后运行相关测试
3. 做必要的手动验证
4. 如果改动影响行为、版本或工作流，同步更新：
   - README.md
   - CHANGELOG.md
   - docs/IMPLEMENTATION_PROGRESS.md
   - Directory.Build.props
5. 结束时必须输出标准交接信息，交回 main thread

输出重点：
- 改了什么
- 测了什么
- 还有什么风险
- 是否建议纳入下个版本
```

## 8. 发布前 main thread 检查清单

准备发布时，`main thread` 至少检查：

- 当前版本号是否正确
- 远端 `main` 是否已经推到目标提交
- 对应版本 tag 是否已经单独推送
- `README.md` 是否反映当前能力
- `CHANGELOG.md` 是否覆盖本次发布内容
- `docs/IMPLEMENTATION_PROGRESS.md` 是否和现状一致
- `Directory.Build.props` 是否和当前版本一致
- 高风险功能是否已经过测试
- 是否仍有不适合发布的临时实现
- 是否需要把缓存、构建产物和测试产物清理到待删除目录

## 9. GitHub 推送规则

以后默认按下面规则推送 GitHub：

### 谁负责推送

- 默认由 `main thread` 负责最终推送判断
- `feature thread` 默认不直接推送正式仓库
- 如果某个 feature thread 被明确授权单独推送，必须先说明这是例外流程

### 什么时候允许推送

至少同时满足以下条件：

- 当前功能已经完成本轮目标
- 相关测试已经跑过，或未跑原因已明确记录
- `README.md`、`CHANGELOG.md`、`docs/IMPLEMENTATION_PROGRESS.md`、`Directory.Build.props` 已同步
- 不需要提交的缓存、构建产物、临时文件已经排除
- `main thread` 已明确给出“允许推送”或“进入发布准备”的结论

### 推送前必须确认的内容

- 目标版本号
- 本次推送包含哪些功能
- 远端 `main` 是否需要更新，准备推到哪个提交
- 是否需要打 tag
- 对应版本 tag 是否已经创建、本地是否存在、推送后要不要单独确认远端状态
- 是否需要同步更新 Release 说明
- 是否还有已知风险只是暂不阻塞发布

### feature thread 的推送边界

feature thread 完成开发后，默认只做以下事情：

- 提交交接信息
- 说明改了什么、测了什么、剩下什么风险
- 说明是否建议进入下个版本

默认不做：

- 不直接宣布“已经可以正式推送”
- 不绕过 `main thread` 直接把未审查内容推到正式 GitHub 仓库

### main thread 的推送后记录

完成推送后，`main thread` 应记录：

- 推送分支
- 远端 `main` 是否已更新到目标提交
- 对应版本号
- 版本 tag 是否已推送
- 本次发布包含的核心内容
- 仍然保留的非阻塞问题

推荐按下面格式记录：

```text
GitHub 同步状态
- 远端 main：已推送 / 未推送（commit: <sha>）
- 版本 tag：已推送 / 未推送（tag: vX.Y.Z）
- 当前版本号：vX.Y.Z
- README / CHANGELOG / IMPLEMENTATION_PROGRESS / Directory.Build.props：已同步 / 未同步
- 工作区状态：干净 / 非干净
- 备注：
```

## 10. 可直接写给 main thread 的推送规则 Prompt

```text
以后 CodexBar for Windows 项目的 GitHub 推送，默认按下面规则执行：

1. 只有 main thread 负责最终推送决策
2. feature thread 默认只负责实现、测试、验证和交接，不直接推正式仓库
3. 推送前必须确认：
   - 当前功能已经完成本轮目标
   - 相关测试已跑过，或未跑原因已明确说明
   - README.md
   - CHANGELOG.md
   - docs/IMPLEMENTATION_PROGRESS.md
   - Directory.Build.props
     这四类文件已经同步
   - 不需要提交的缓存、构建产物、临时文件已经清理或忽略
4. main thread 需要在推送前明确给出：
   - 是否允许推送
   - 推送对应的版本号
   - 是否需要打 tag / release
5. 推送后 main thread 需要记录：
   - 分支
   - 远端 main 是否已更新到目标提交
   - 版本号
   - tag 是否已推送
   - 发布内容摘要
   - 仍存在但不阻塞发布的问题

如果某个 feature thread 想提前推送，必须先被明确授权，并说明这是例外流程。
```

## 11. 一句话原则

**主线 thread 管方向和发布，功能 thread 管落地和验证。**
