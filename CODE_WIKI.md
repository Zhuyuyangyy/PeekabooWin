# PeekabooWin Code Wiki

> Windows Desktop Automation Kit — 基于 .NET 8 的 Windows 原生桌面自动化框架

---

## 目录

- [1. 项目概览](#1-项目概览)
- [2. 整体架构](#2-整体架构)
- [3. 项目结构与模块划分](#3-项目结构与模块划分)
- [4. 核心模块详解](#4-核心模块详解)
  - [4.1 Agent 模块](#41-agent-模块)
  - [4.2 Memory 模块](#42-memory-模块)
  - [4.3 Planning 模块](#43-planning-模块)
  - [4.4 Safety 模块](#44-safety-模块)
  - [4.5 Perception 模块](#45-perception-模块)
  - [4.6 Capture 模块](#46-capture-模块)
  - [4.7 Input 模块](#47-input-模块)
  - [4.8 OCR 模块](#48-ocr-模块)
  - [4.9 UIAutomation 模块](#49-uiautomation-模块)
  - [4.10 Windows 模块](#410-windows-模块)
  - [4.11 Verification 模块](#411-verification-模块)
  - [4.12 Trace 模块](#412-trace-模块)
  - [4.13 Infrastructure 模块](#413-infrastructure-模块)
  - [4.14 Exceptions 模块](#414-exceptions-模块)
  - [4.15 Models 模块](#415-models-模块)
- [5. CLI 入口层](#5-cli-入口层)
- [6. API Server 层](#6-api-server-层)
- [7. 依赖关系图](#7-依赖关系图)
- [8. 数据流与执行管线](#8-数据流与执行管线)
- [9. 构建与运行](#9-构建与运行)
- [10. 测试体系](#10-测试体系)
- [11. 版本演进](#11-版本演进)

---

## 1. 项目概览

**PeekabooWin** 是 macOS 版 [Peekaboo](https://github.com/nicepkg/peekaboo) 的 Windows 对应实现，提供 Windows 原生桌面自动化能力。其核心定位是：

- **窗口感知**：枚举、聚焦、截图 Windows 窗口
- **UI 自动化**：通过 UI Automation (UIA) 访问控件树、查找/点击元素
- **OCR 识别**：基于 Tesseract 的屏幕文字识别
- **Agent Runtime**：自然语言任务 → 规则/LLM 解析 → 自动执行
- **视觉技能记忆**：从成功执行中提取可复用的 UI 操作技能
- **安全门控**：风险评估 + 跨应用迁移防护

### 技术栈

| 维度 | 技术选型 |
|------|---------|
| 语言 | C# (.NET 8) |
| 目标平台 | `net8.0-windows10.0.17763.0` |
| UI 自动化 | Win32 UI Automation (UIA) |
| 截图 | GDI BitBlt (WinForms) |
| 输入模拟 | SendInput (P/Invoke) |
| OCR | Tesseract (`eng.traineddata`) |
| LLM | MiniMax API (`MiniMax-M2.7`) |
| DI 容器 | `Microsoft.Extensions.DependencyInjection` |
| HTTP 服务 | ASP.NET Core Minimal API |
| 测试 | xUnit 2.6.2 |
| 输出格式 | JSON 结构化输出 |

---

## 2. 整体架构

```
┌──────────────────────────────────────────────────────────────────┐
│                        外部调用入口                               │
│  ┌──────────────────┐              ┌──────────────────────────┐  │
│  │  PeekabooWin.Cli │              │  PeekabooWin.ApiServer   │  │
│  │  (CLI 命令行)     │              │  (HTTP API 服务)          │  │
│  └────────┬─────────┘              └────────────┬─────────────┘  │
│           │                                      │                │
│  ┌────────▼──────────────────────────────────────▼─────────────┐  │
│  │                    PeekabooWin.Core                          │  │
│  │                                                              │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────────┐  │  │
│  │  │  Agent   │  │  Memory  │  │ Planning │  │   Safety   │  │  │
│  │  │ 编排引擎  │  │ 技能记忆  │  │ VACP规划  │  │  风险门控   │  │  │
│  │  └────┬─────┘  └────┬─────┘  └────┬─────┘  └─────┬──────┘  │  │
│  │       │             │             │               │          │  │
│  │  ┌────▼─────────────▼─────────────▼───────────────▼──────┐  │  │
│  │  │              Perception / Verification / Trace         │  │  │
│  │  │         元素定位 / 执行验证 / 追踪记录                   │  │  │
│  │  └──────────────────────┬────────────────────────────────┘  │  │
│  │                         │                                    │  │
│  │  ┌──────────────────────▼────────────────────────────────┐  │  │
│  │  │    Capture / Input / OCR / UIAutomation / Windows     │  │  │
│  │  │          底层 Win32 / UIA / GDI / SendInput            │  │  │
│  │  └───────────────────────────────────────────────────────┘  │  │
│  │                                                              │  │
│  │  ┌───────────────────────────────────────────────────────┐  │  │
│  │  │    Infrastructure / Exceptions / Models                │  │  │
│  │  │    日志 / TraceID / 临时文件 / 异常体系 / 数据模型       │  │  │
│  │  └───────────────────────────────────────────────────────┘  │  │
└──────────────────────────────────────────────────────────────────┘
```

### 架构分层原则

| 层级 | 职责 | 对应命名空间 |
|------|------|-------------|
| 入口层 | 命令路由、HTTP 端点 | `PeekabooWin.Cli`, `PeekabooWin.ApiServer` |
| 编排层 | 任务解析、执行循环、恢复策略 | `PeekabooWin.Core.Agent` |
| 智能层 | 技能记忆、迁移控制、VACP 闭环规划 | `PeekabooWin.Core.Memory`, `Planning` |
| 安全层 | 风险评估、跨域拦截 | `PeekabooWin.Core.Safety` |
| 感知层 | 元素定位、候选排序、执行验证 | `PeekabooWin.Core.Perception`, `Verification` |
| 能力层 | 截图、输入、OCR、UIA、窗口管理 | `Capture`, `Input`, `Ocr`, `UIAutomation`, `Windows` |
| 基础层 | 日志、异常、模型、临时文件 | `Infrastructure`, `Exceptions`, `Models`, `Trace` |

---

## 3. 项目结构与模块划分

```
PeekabooWin/
├── PeekabooWin.sln                          # 解决方案文件（3个项目）
├── src/
│   ├── PeekabooWin.Cli/                     # CLI 入口项目
│   │   ├── Program.cs                       # 最小入口（~107行）
│   │   ├── Bootstrap/
│   │   │   ├── CommandRouter.cs             # 命令→Handler 路由映射
│   │   │   └── ServiceRegistration.cs       # DI 容器注册
│   │   ├── Commands/
│   │   │   ├── ICommandHandler.cs           # Handler 接口
│   │   │   ├── WindowCommandHandler.cs      # 窗口/截图/输入命令
│   │   │   ├── UiaCommandHandler.cs         # UIA 控件树命令
│   │   │   ├── OcrCommandHandler.cs         # OCR 识别命令
│   │   │   ├── AgentCommandHandler.cs       # Agent 任务命令
│   │   │   ├── SkillCommandHandler.cs       # 技能管理命令
│   │   │   ├── ServerCommandHandler.cs      # HTTP 服务启动
│   │   │   └── CliHelpers.cs               # CLI 辅助工具
│   │   └── ApiServer.cs                     # 内嵌 HTTP API 服务器
│   │
│   ├── PeekabooWin.Core/                    # 核心逻辑库
│   │   ├── Agent/                           # Agent 编排引擎
│   │   ├── Memory/                          # 视觉技能记忆系统
│   │   ├── Planning/                        # VACP 闭环规划
│   │   ├── Safety/                          # 风险门控
│   │   ├── Perception/                      # 元素感知与排序
│   │   ├── Verification/                    # 执行验证
│   │   ├── Capture/                         # 屏幕截图
│   │   ├── Input/                           # 输入模拟
│   │   ├── Ocr/                             # OCR 文字识别
│   │   ├── UIAutomation/                    # UI Automation
│   │   ├── Windows/                         # 窗口管理
│   │   ├── Trace/                           # 执行追踪
│   │   ├── Infrastructure/                  # 基础设施
│   │   ├── Exceptions/                      # 异常体系
│   │   └── Models/                          # 数据模型
│   │
│   └── PeekabooWin.ApiServer/              # 独立 ASP.NET Core API 服务
│       └── Program.cs                       # Minimal API 入口
│
├── tests/
│   └── PeekabooWin.Core.Tests/             # xUnit 单元测试
│
├── benchmarks/
│   └── RealDesktop30/                       # 30 真实桌面场景基准测试
│
├── tessdata/                                # Tesseract 语言数据
│   └── eng.traineddata
│
├── artifacts/                               # 截图产物
└── docs/                                    # 版本规格与 Demo 证据
```

---

## 4. 核心模块详解

### 4.1 Agent 模块

> 命名空间：`PeekabooWin.Core.Agent`

Agent 模块是整个系统的编排核心，负责将自然语言任务解析为可执行步骤并驱动执行循环。

#### AgentOrchestrator

**职责**：核心编排器，驱动「解析 → 风险评估 → 执行 → 验证 → 恢复」的完整闭环。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `RunAsync` | `Task<AgentTaskResponse> RunAsync(AgentTaskRequest, CancellationToken)` | 主执行循环：解析任务 → 逐步执行 → 风险门控 → 验证 → 恢复 |

**执行流程**：
1. 创建 `ExecutionTrace` 追踪对象
2. 调用 `TaskParser.ParseTaskAsync` 解析任务为步骤列表
3. 逐步执行：
   - **风险门控**：对 `type/click/hotkey/ocr-click` 动作调用 `ActionRiskGate.Evaluate`
   - **元素定位**：对 `click/click-element/find/ocr-click` 等动作调用 `ElementCandidateRanker.Rank`
   - **前置状态捕获**：截图 + OCR 获取执行前状态
   - **动作执行**：调用 `ActionExecutor.ExecuteActionAsync`
   - **执行验证**：调用 `ActionVerifier.VerifyAsync` 对比前后状态
   - **失败恢复**：调用 `RecoveryPlanner.PlanRecovery` 制定恢复策略
4. 超时/取消处理（`CancellationTokenSource` + `TimeoutMs`）

**依赖**：`TaskParser`, `ActionExecutor`, `VacpSkillIntegration`, `VacpTraceLogger`, `ActionRiskGate`, `RecoveryPlanner`, `ActionVerifier`, `ElementCandidateRanker`, `CaptureService`, `OcrService`, `UIAutomationService`, `WindowService`, `TempFileManager`

#### TaskParser

**职责**：将自然语言任务解析为 `List<AgentStep>`，支持规则解析和 LLM 解析两种模式。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `ParseTaskAsync` | `Task<List<AgentStep>> ParseTaskAsync(string task, string? context, CancellationToken)` | 先尝试规则解析，失败则调用 LLM |
| `TryRuleBasedParse` | `List<AgentStep> TryRuleBasedParse(string lowerTask, string originalTask)` | 基于正则的规则解析 |
| `TryLLMParseAsync` | `Task<List<AgentStep>> TryLLMParseAsync(string task, string? context, CancellationToken)` | 调用 MiniMax API 解析 |
| `GetLastParseMetadata` | `ParseTaskMetadata GetLastParseMetadata()` | 获取最近一次解析的元数据 |

**解析模式**：

| 模式 | 触发条件 | 说明 |
|------|---------|------|
| `rule_based` | 正则匹配成功 | 支持 click/type/press/open/screenshot/ocr/inspect/find 等模式 |
| `llm` | 规则解析失败 + API Key 可用 | 调用 MiniMax M2.7 模型 |
| `regex_fallback` | 规则解析失败 + 无 API Key | 返回 error 步骤 |
| `llm_failed` | LLM 返回不可解析内容 | 降级返回 error 步骤 |
| `llm_timeout` / `llm_error` | LLM 调用异常 | 降级返回 error 步骤 |

**支持的工具列表**（15个）：`list-windows`, `focus-window`, `screenshot`, `click`, `click-rel`, `is-focused`, `find-on-screen`, `ocr-click`, `type`, `press`, `hotkey`, `inspect`, `find`, `click-element`, `ocr`

#### ActionExecutor

**职责**：根据动作名称和参数字典执行具体的 UI 操作。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `ExecuteActionAsync` | `Task<(bool success, string result)> ExecuteActionAsync(string action, Dictionary<string,string> args, CancellationToken)` | 分发执行各类 UI 操作 |

**支持的动作**：`click`, `click-rel`, `type`, `press`, `hotkey`, `screenshot`, `focus-window`, `list-windows`, `inspect`, `find`, `click-element`, `click-element-guess`, `ocr`, `ocr-find`, `ocr-click`, `find-on-screen`, `is-focused`, `window-info`

**依赖**：`WindowService`, `CaptureService`, `InputService`, `OcrService`, `UIAutomationService`, `TempFileManager`

#### RecoveryPlanner

**职责**：在动作执行失败时制定恢复策略。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `PlanRecovery` | `RecoveryPlan PlanRecovery(RecoveryContext context)` | 根据失败上下文决定恢复方案 |

**恢复策略**：

| 策略 | 触发条件 | 恢复动作 |
|------|---------|---------|
| `RefocusAndRetry` | 窗口失焦 | 重新聚焦窗口 → 重试原动作 |
| `RelocateAndRetry` | 元素未找到 | 重新截图/OCR → 重新定位 → 重试 |
| `SimpleRetry` | 其他失败 | 直接重试原动作 |
| `Abort` | 超过最大重试次数 | 终止执行 |

#### AgentService

**职责**：对外暴露的 Agent 服务门面，封装 `AgentOrchestrator`。

**关键方法**：`ExecuteTaskAsync(AgentTaskRequest, CancellationToken)` → 委托给 `AgentOrchestrator.RunAsync`

#### VacpPlannerWithSkills

**职责**：V0.8 引入的技能引导 VACP 规划器，在 VACP 执行前搜索匹配技能并注入 `SkillHint`。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `PlanWithSkills` | `Task<VacpResult> PlanWithSkills(VacpRequest, string task, CancellationToken)` | 技能引导的 VACP 执行 |

#### VacpSkillIntegration

**职责**：封装技能相关的所有逻辑，作为 Agent 与 Memory 模块的桥梁。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `BuildWindowSignatureAsync` | `Task<WindowSignature> BuildWindowSignatureAsync(string windowKeyword)` | 构建当前窗口签名 |
| `SearchWithContextAsync` | `Task<List<SkillSearchResult>> SearchWithContextAsync(string task, WindowSignature)` | 上下文感知技能搜索 |
| `BeforePlanning` | `SkillHint? BeforePlanning(string task, WindowSignature?)` | 规划前技能匹配 |
| `AfterSuccess` | `void AfterSuccess(ExecutionTrace)` | 成功后提取技能 |

---

### 4.2 Memory 模块

> 命名空间：`PeekabooWin.Core.Memory`

Memory 模块实现了完整的视觉技能记忆系统，包括技能提取、存储、检索、匹配、回放和跨应用迁移控制。

#### VisualSkill

**职责**：视觉技能的核心数据模型，表示从成功 VACP 轨迹中提取的可复用 UI 操作技能。

| 属性 | 类型 | 说明 |
|------|------|------|
| `SkillId` | `string` | 唯一标识（12位 GUID） |
| `Name` | `string` | 技能名称 |
| `AppPattern` | `string` | 应用匹配模式（如 `notepad.exe`） |
| `ScreenType` | `string` | 屏幕类型（`edit`/`dialog`/`web-form`） |
| `TriggerConditions` | `List<string>` | 触发条件列表 |
| `ProcedureSteps` | `List<string>` | 序列化动作步骤 |
| `RiskLevel` | `string` | 风险等级（`L0`/`L1`/`L2`） |
| `RiskDomain` | `string` | 风险域（`neutral`/`payment`/`admin`等） |
| `ContextAnchors` | `List<string>` | 上下文锚点 |
| `SuccessRate` | `double` | 成功率（增量更新） |
| `UsageCount` | `int` | 使用次数 |
| `Scope` | `SkillScope?` | V0.9 跨应用迁移元数据 |

**关键方法**：`RecordUsage(bool success)` — 增量更新成功率

#### VisualSkillStore

**职责**：技能持久化存储管理，支持 JSON 文件读写。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `GetAll()` | 获取所有技能 |
| `GetById(string id)` | 按 ID 获取技能 |
| `Add(VisualSkill)` | 添加技能 |
| `Remove(string id)` | 删除技能 |
| `Search(string? appPattern, string? screenType)` | 按应用/屏幕类型搜索 |
| `SeedDemoSkills()` | 写入预设演示技能 |
| `Load()` / `Save()` | 从/向 `skills.json` 持久化 |

#### VisualSkillExtractor

**职责**：从成功的 VACP 任务追踪中提取视觉技能。

**关键方法**：`ExtractFromTrace(ExecutionTrace)` — 根据追踪中的行为和状态推断技能属性并创建 `VisualSkill`

#### VisualSkillRetriever

**职责**：根据应用模式和屏幕类型检索相关视觉技能，计算置信度。

**关键方法**：`Retrieve(string? appPattern, string? screenType)` — 返回匹配的技能列表

#### SkillRetriever

**职责**：V0.8 基于文本的任务搜索和技能匹配，支持多维评分。

**关键方法**：`Search(string taskText, string? appPattern)` — 计算匹配分数并返回排序结果

#### SkillMatchScore

**职责**：定义技能与任务匹配分数的组成。

| 维度 | 权重 | 说明 |
|------|------|------|
| `AppMatch` | 0.30 | 应用模式匹配度 |
| `TextMatch` | 0.25 | OCR 文本 vs 触发条件 |
| `ActionSequenceMatch` | 0.20 | 动作序列对齐度 |
| `RiskMatch` | 0.15 | 风险等级兼容度 |
| `RecencyFactor` | 0.10 | 使用频率因子 |

**可用性判定**：`IsUsable = Total >= 0.6 && RiskMatch >= 0.5`

#### SkillExecutionPolicy

**职责**：定义技能执行策略和过滤规则。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `IsAppMatch(VisualSkill, string)` | 应用模式匹配检查 |
| `IsHighRisk(VisualSkill)` | 高风险任务检查 |
| `CanUse(VisualSkill, string)` | 综合可用性判断 |

#### SkillHint

**职责**：V0.8 技能提示，注入 VACP 规划器引导候选排序。

| 属性 | 说明 |
|------|------|
| `SuggestedElementLabels` | 建议的元素标签 |
| `SuggestedActionType` | 建议的动作类型 |
| `PreferredRiskLevel` | 偏好风险级别 |

#### SkillReplayEngine

**职责**：V0.10 技能回放引擎，支持 dry-run 和真实执行。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `ReplayAsync(skillId, dryRun, execute, window)` | 回放指定技能 |
| `ExecuteStepAsync(step, windowTitle)` | 执行单步动作 |

**回放流程**：窗口聚焦 → 逐步执行动作 → 风险评估 → 结果记录 → 生成 `SkillReplayReport`

#### SkillReplayReport

**职责**：技能回放结果报告。

| 属性 | 说明 |
|------|------|
| `SkillId` / `SkillName` | 技能标识 |
| `Steps` | 每步回放记录 |
| `OverallSuccess` | 整体成功状态 |
| `VerificationResult` | 验证结果 |

#### AppProfile

**职责**：应用程序配置和行为分析，描述当前窗口的上下文特征。

| 属性 | 说明 |
|------|------|
| `AppId` | 应用标识（进程名小写） |
| `AppName` | 应用名称 |
| `ProcessName` | 进程名 |
| `WindowType` | 窗口类型 |
| `InputMode` | 输入模式 |
| `RiskDomain` | 风险域 |
| `KnownAnchors` | 已知锚点 |
| `VisitCount` | 访问计数 |

**关键方法**：
- `FromWindowSignature(WindowSignature)` — 从窗口签名构建
- `IsCompatibleWith(SkillScope?)` — 与技能作用域兼容性检查
- `Touch()` — 更新访问时间

#### WindowSignature

**职责**：窗口上下文指纹，用于实时识别当前窗口的特征。

| 属性 | 说明 |
|------|------|
| `WindowTitle` | 窗口标题 |
| `ProcessName` | 进程名 |
| `WindowType` | 分类窗口类型 |
| `InputMode` | 分类输入模式 |
| `RiskDomain` | 分类风险域 |
| `VisibleTexts` | OCR 可见文本 |
| `Profile` | 关联的 AppProfile |
| `AnchorCandidates` | 锚点候选 |

**分类逻辑**：
- `WindowType`：`browser`（Edge/Chrome/Firefox）、`editor`（Notepad/Wordpad）、`dialog`、`unknown`
- `InputMode`：`web_textbox`、`edit_field`、`dialog_input`、`unknown`
- `RiskDomain`：`payment`、`external_ai_chat`、`admin`、`neutral`

**关键方法**：`FromProcessAndTitle(processName, title)` — 工厂方法；`SimilarityTo(other)` — 签名相似度计算

#### SkillScope / SkillScopeValidator

**职责**：V0.9 技能作用域定义与校验，控制技能的跨应用迁移范围。

| SkillScope 属性 | 说明 |
|-----------------|------|
| `SupportedApps` | 允许的应用列表（`*` = 全部） |
| `RequiredAnchors` | 必需锚点 |
| `ForbiddenDomains` | 禁止的风险域 |
| `MinRiskLevel` | 最低风险等级要求 |

**校验方法**：`Validate(AppProfile)` → 返回 `APP_MISMATCH` / `ANCHOR_MISSING` / `DOMAIN_FORBIDDEN` 或 `null`（通过）

#### NegativeTransferGuard

**职责**：V0.9 负面迁移防护，防止高风险跨域技能迁移。

**关键方法**：`Evaluate(GuardContext)` → `GuardResult`

**拦截规则**：

| 规则 | 条件 | 动作 |
|------|------|------|
| 高危动词检测 | 任务含高危动词 + 技能等级 ≤ L1 | BLOCK |
| 高危目标检测 | 任务含高危目标 + 技能等级 = L0 | HUMAN_REVIEW |
| 禁止域迁移 | `external_ai_chat` → `payment`/`admin` | BLOCK |
| 跨域风险 | 技能域 ≠ 应用域 | BLOCK |
| 技能等级不足 | 高风险应用需要 L2 技能 | HUMAN_REVIEW |

#### SkillTransferController

**职责**：V0.9 技能迁移决策控制器，综合作用域校验、负面迁移防护和锚点覆盖检查。

**关键方法**：`Decide(TransferContext)` → `TransferDecision`

**决策流程**：
1. 作用域校验（`SkillScopeValidator`）
2. 负面迁移防护（`NegativeTransferGuard`）
3. 锚点覆盖检查（`AnchorMatcher`）
4. 匹配分数加权调整
5. 最终决策：`INJECT`（≥0.75）/ `HUMAN_REVIEW`（≥0.50）/ `BLOCK`（<0.50）

#### AnchorMapping

**职责**：语义锚点 ↔ OCR 文本映射，静态工具类。

**映射表**：按 `(WindowType, anchorName)` 映射到 OCR 搜索文本列表。

**关键方法**：
- `GetSearchTexts(WindowType, string)` — 获取锚点对应的搜索文本
- `ScoreAnchorMatch(string, WindowType, string)` — 计算锚点匹配分数

#### VisualAnchor / AnchorMatcher

**职责**：视觉锚点定义与匹配。

**标准锚点**：`input_box`, `send_btn`, `ok_btn`, `cancel_btn`, `close_btn`, `edit_region`

**AnchorMatcher 关键方法**：
- `MatchAnchor(anchorType, appMode, visibleTexts)` — 匹配单个锚点
- `CheckCoverage(requiredAnchors, appMode, visibleTexts)` — 检查锚点覆盖率

---

### 4.3 Planning 模块

> 命名空间：`PeekabooWin.Core.Planning`

#### VacpPlanner

**职责**：VACP（Vision-Action Closed-loop Planner）闭环规划器，实现「感知 → 定位 → 规划 → 风险 → 执行 → 验证」的完整闭环。

**关键方法**：`Execute闭环(VacpRequest)` → `VacpResult`

**VACP Pipeline**：
1. **Screen Capture** — 截图当前屏幕
2. **Vision Perceive** — 视觉感知生成 Screen State Graph
3. **Build Candidates** — 从图构建动作候选
4. **Element Grounding** — 元素定位评分
5. **Rank Candidates** — 候选排序
6. **Risk Gating** — 风险门控
7. **Execute** — 执行最佳动作
8. **Verification** — 前后截图对比验证（失败自动重试一次）

#### ActionCandidate

**职责**：动作候选数据模型。

| 属性 | 说明 |
|------|------|
| `ActionType` | 动作类型（`click`/`type`） |
| `TargetElement` | 目标 UI 元素 |
| `InputText` | 输入文本 |
| `Description` | 描述 |
| `GroundingScore` | 定位评分 |
| `ModelScore` | 模型评分 |

#### VacpTraceLogger / VacpTraceRecord

**职责**：VACP 执行追踪日志记录。

---

### 4.4 Safety 模块

> 命名空间：`PeekabooWin.Core.Safety`

#### ActionRiskGate

**职责**：风险感知动作门控，基于加权评分模型决定动作是否允许执行。

**风险评分公式**：

```
Risk = 0.30 × OperationRisk
     + 0.25 × PageRisk
     + 0.20 × Irreversibility
     + 0.15 × DataSensitivity
     + 0.10 × Uncertainty
```

**决策阈值**：

| 风险分数 | 决策 | 说明 |
|---------|------|------|
| < 0.3 | `Allow` | 自动执行 |
| [0.3, 0.6) | `Confirm` | 需人工确认 |
| ≥ 0.6 | `Block` | 默认阻断 |

**评分维度**：

| 维度 | 计算逻辑 |
|------|---------|
| `OperationRisk` | 高危操作=1.0, click=0.2, type=0.4, hotkey=0.5, scroll=0.1, 其他=0.3 |
| `PageRisk` | 高风险页面=1.0, dialog=0.6, browser=0.3, 其他=0.1 |
| `Irreversibility` | 不可逆操作=1.0, 长文本输入=0.6, 其他=0.0 |
| `DataSensitivity` | 含敏感关键词=1.0, 密码字段=1.0, 其他=0.0 |
| `Uncertainty` | 定位分数<0.5=0.8, <0.75=0.4, <0.85=0.2, 其他=0.0 |

**关键方法**：`Evaluate(ActionRiskContext)` → `RiskDecision`；`ComputeRisk(ActionRiskContext)` → `double`

---

### 4.5 Perception 模块

> 命名空间：`PeekabooWin.Core.Perception`

#### ElementCandidateRanker

**职责**：对 UI 元素候选进行排序，提升元素定位准确性。

**关键方法**：`Rank(CandidateRankRequest)` → `CandidateRankResult`

#### ElementGroundingScore

**职责**：元素定位评分器，评估目标文本与 UI 元素的匹配程度。

**关键方法**：`Score(UiElement, GroundingQuery)` → `double`

#### UiElement

**职责**：UI 元素统一数据模型。

| 属性 | 说明 |
|------|------|
| `Label` / `Name` | 元素标签/名称 |
| `Type` | 元素类型 |
| `BBox` | 边界框（`BoundingBox`） |
| `Confidence` | 置信度 |
| `Source` | 来源（`uia`/`ocr`） |

---

### 4.6 Capture 模块

> 命名空间：`PeekabooWin.Core.Capture`

#### CaptureService

**职责**：屏幕截图服务，基于 GDI BitBlt 实现全屏和窗口截图。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `CaptureScreen(string outputPath)` | 全屏截图 |
| `CaptureWindow(IntPtr hwnd, string outputPath)` | 窗口截图 |

---

### 4.7 Input 模块

> 命名空间：`PeekabooWin.Core.Input`

#### InputService

**职责**：输入模拟服务，封装 SendInput API 实现鼠标/键盘操作。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `Click(int x, int y)` | 鼠标点击 |
| `SendInputAsync(string text)` | 文本输入 |
| `SendHotkey(string keys)` | 快捷键组合 |

#### StableTyper

**职责**：稳定文本输入器，通过逐字符延迟输入确保文本可靠输入。

**关键方法**：`TypeSlowly(string text)` — 逐字符缓慢输入

---

### 4.8 OCR 模块

> 命名空间：`PeekabooWin.Core.Ocr`

#### OcrService

**职责**：光学字符识别服务，基于 Tesseract 引擎。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `RecognizeImageAsync(string imagePath)` | 识别图片中的文字 |
| `RecognizeScreenAsync()` | 识别当前屏幕 |

**依赖**：`tessdata/eng.traineddata` 语言数据文件

---

### 4.9 UIAutomation 模块

> 命名空间：`PeekabooWin.Core.UIAutomation`

#### UIAutomationService

**职责**：Windows UI Automation 封装，提供控件树访问和元素操作。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `Inspect(string windowKeyword)` | 检查窗口 UIA 控件树 |
| `FindByName(string window, string name)` | 按名称查找元素 |
| `FindByControlType(string window, string controlType)` | 按控件类型查找 |
| `ClickElement(string window, string name)` | 点击 UI 元素 |

#### SeeService

**职责**：UI 元素信息获取服务，封装 UIA 查询。

**关键方法**：`GetUIElementsAsync(string windowKeyword)` — 获取窗口 UI 元素列表

---

### 4.10 Windows 模块

> 命名空间：`PeekabooWin.Core.Windows`

#### WindowService

**职责**：窗口管理服务，封装 Win32 API 实现窗口枚举、聚焦、查找。

**关键方法**：

| 方法 | 说明 |
|------|------|
| `GetWindowsAsync()` | 枚举所有可见窗口 |
| `FindWindow(string keyword)` | 按关键词查找窗口 |
| `FocusWindow(string keyword)` | 聚焦窗口 |
| `GetActiveWindowAsync()` | 获取当前活动窗口 |

---

### 4.11 Verification 模块

> 命名空间：`PeekabooWin.Core.Verification`

#### ActionVerifier

**职责**：动作执行结果验证器，对比执行前后的屏幕状态。

**关键方法**：`VerifyAsync(VerificationRequest, CancellationToken)` → `VerificationResult`

#### BeforeAfterVerifier

**职责**：前后截图对比验证器，用于 VACP 闭环验证。

**关键方法**：`Verify(byte[] before, byte[] after, VerificationContext)` → `BeforeAfterVerificationResult`

---

### 4.12 Trace 模块

> 命名空间：`PeekabooWin.Core.Trace`

#### ExecutionTrace

**职责**：执行追踪记录，记录完整的任务执行过程。

| 属性 | 说明 |
|------|------|
| `TraceId` | 追踪 ID |
| `Task` | 任务描述 |
| `StartedAt` / `CompletedAt` | 起止时间 |
| `ParserMode` | 解析模式 |
| `Decision` / `RiskLevel` | 风险决策 |
| `GroundingScore` | 定位评分 |
| `StepTraces` | 各步骤追踪 |
| `TotalSteps` / `SuccessfulSteps` / `FailedSteps` / `BlockedSteps` | 步骤统计 |
| `RecoveryAttempts` | 恢复尝试次数 |

**子结构**：`StepTrace`（步骤追踪）、`RiskGateTrace`（风险门控追踪）、`VerificationTrace`（验证追踪）、`RecoveryTrace`（恢复追踪）、`CandidateRankTrace`（候选排序追踪）

---

### 4.13 Infrastructure 模块

> 命名空间：`PeekabooWin.Core.Infrastructure`

#### PekaLogger

**职责**：结构化日志记录器，输出 JSON 格式日志到本地文件。

| 方法 | 说明 |
|------|------|
| `Debug(source, message)` | Debug 级别日志 |
| `Info(source, message)` | Info 级别日志 |
| `Warn(source, message, ex?)` | Warning 级别日志 |
| `Error(source, message, ex?)` | Error 级别日志 |

**日志路径**：`%LocalAppData%/PeekabooWin/logs/peekaboo-win-{yyyyMMdd}.log`

**日志格式**：`{ ts, level, trace_id, source, message, exception }`

#### TraceIdProvider

**职责**：追踪 ID 提供器，基于 `AsyncLocal<string>` 实现请求级追踪。

| 方法 | 说明 |
|------|------|
| `Current` | 获取当前追踪 ID（自动生成） |
| `Generate()` | 生成新追踪 ID（8位 GUID） |
| `BeginNew()` | 开始新的追踪上下文 |

#### TempFileManager

**职责**：临时文件管理，统一管理截图等临时文件的创建和清理。

---

### 4.14 Exceptions 模块

> 命名空间：`PeekabooWin.Core.Exceptions`

#### 异常体系

```
PeekabooException (abstract)
├── WindowNotFoundException     # WINDOW_NOT_FOUND
├── ElementNotFoundException    # ELEMENT_NOT_FOUND
├── OcrUnavailableException     # OCR_UNAVAILABLE
├── CaptureFailedException      # CAPTURE_FAILED
├── RiskBlockedException        # RISK_BLOCKED
└── SkillReplayException        # SKILL_REPLAY_FAILED
```

每个异常包含：
- `ErrorCode` — 机器可读错误码
- `Message` — 人类可读描述
- `Hint` — 修复建议

---

### 4.15 Models 模块

> 命名空间：`PeekabooWin.Core.Models`

#### 核心数据模型

| 类 | 说明 |
|----|------|
| `AgentTaskRequest` | Agent 任务请求（task, context, max_steps, dry_run, timeout_ms） |
| `AgentTaskResponse` | Agent 任务响应（steps, success, trace, parser_mode, llm_enabled） |
| `AgentStep` | 执行步骤（thought, action, args, result, success, error） |
| `ToolDescriptor` | 工具描述（name, description, parameters） |
| `CommandResult` | 通用命令结果（success, command, data, error, error_code, hint, trace_id） |
| `WindowInfo` | 窗口信息（handle, title, process, class_name, rect, visible, enabled） |
| `OcrResult` | OCR 识别结果（text, words, language, confidence, engine） |
| `OcrWord` | OCR 单词（text, bounding_box, confidence） |
| `CaptureResult` | 截图结果 |
| `SeeResult` | UI 元素查看结果 |
| `UIAElementInfo` | UIA 元素信息 |

#### 枚举类型

| 枚举 | 值 | 说明 |
|------|---|------|
| `RiskDomain` | `Safe, Dangerous, Payment, Messaging, Admin` | 风险域分类 |
| `InputMode` | `TextInput, Mixed, ButtonClick, Unknown` | 输入模式 |
| `WindowType` | `Edit, WebBrowser, Dialog, SystemSettings, FileExplorer, Unknown` | 窗口类型 |

---

## 5. CLI 入口层

> 项目：`PeekabooWin.Cli`

### Program.cs

最小入口（~107行），职责：
1. 解析命令行参数
2. 调用 `ServiceRegistration.ConfigureServices()` 构建 DI 容器
3. 通过 `CommandRouter.Resolve(command)` 获取 Handler
4. 执行 `handler.ExecuteAsync(args)`

### ServiceRegistration

DI 容器配置，所有服务注册为 **Singleton**，CommandHandler 注册为 **Transient**。

**注册清单**：

| 服务 | 生命周期 |
|------|---------|
| `WindowService`, `CaptureService`, `InputService`, `UIAutomationService`, `OcrService` | Singleton |
| `VisualSkillStore`, `VacpSkillIntegration`, `ActionRiskGate` | Singleton |
| `RecoveryPlanner`, `ActionVerifier`, `ElementCandidateRanker`, `SkillReplayEngine` | Singleton |
| `TempFileManager`, `HttpClient`, `TaskParser`, `ActionExecutor` | Singleton |
| `VacpTraceLogger`, `AgentOrchestrator`, `AgentService` | Singleton |
| `CommandRouter` | Singleton |
| `WindowCommandHandler`, `UiaCommandHandler`, `OcrCommandHandler` | Transient |
| `AgentCommandHandler`, `SkillCommandHandler`, `ServerCommandHandler` | Transient |

### CommandRouter

命令 → Handler 类型映射，支持 22 个命令：

| 命令组 | 命令 |
|--------|------|
| 窗口操作 | `list-windows`, `focus-window`, `screenshot`, `click`, `type`, `press`, `hotkey`, `window-info`, `click-rel`, `is-focused` |
| UIA 操作 | `inspect`, `find`, `click-element`, `find-by-control-type` |
| OCR 操作 | `ocr`, `find-on-screen`, `ocr-click` |
| Agent | `agent` |
| 技能管理 | `skill-list`, `skill-replay`, `skill-seed`, `skill-search`, `skill-search-context`, `skill-use-preview`, `skill-execute-guided` |
| 服务 | `server` |

### ICommandHandler

```csharp
public interface ICommandHandler
{
    Task<int> ExecuteAsync(string[] args);
}
```

### ApiServer（内嵌 HTTP 服务器）

基于 `Microsoft.AspNetCore.Http` 的轻量 HTTP 服务器，提供 RESTful API 端点，支持 CORS。

---

## 6. API Server 层

> 项目：`PeekabooWin.ApiServer`

独立的 ASP.NET Core Web API 服务，基于 Minimal API 模式。

**端点**：

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/health` | 健康检查 |
| GET | `/windows` | 列出窗口 |
| POST | `/click` | 鼠标点击 |
| POST | `/type` | 文本输入 |
| POST | `/agent` | Agent 任务执行 |
| POST | `/skill-search` | 技能搜索 |

**配置**：`appsettings.json` — 日志级别 + 允许主机列表

---

## 7. 依赖关系图

### 项目间依赖

```
PeekabooWin.ApiServer ──→ PeekabooWin.Core
PeekabooWin.Cli ────────→ PeekabooWin.Core
PeekabooWin.Core.Tests ─→ PeekabooWin.Core
```

### NuGet 依赖

| 项目 | 包 | 版本 | 用途 |
|------|---|------|------|
| PeekabooWin.Cli | `Microsoft.Extensions.DependencyInjection` | 8.0.1 | DI 容器 |
| PeekabooWin.Cli | `Microsoft.AspNetCore.Http` | 2.2.2 | 内嵌 HTTP 服务 |
| PeekabooWin.Core.Tests | `xunit` | 2.6.2 | 测试框架 |
| PeekabooWin.Core.Tests | `xunit.runner.visualstudio` | 2.5.4 | 测试运行器 |
| PeekabooWin.Core.Tests | `Microsoft.NET.Test.Sdk` | 17.8.0 | 测试 SDK |

### Core 内部模块依赖

```
Agent ──→ Memory, Planning, Safety, Perception, Verification, Trace, Capture, Ocr, UIAutomation, Windows, Infrastructure
Memory ──→ Models, Perception
Planning ──→ Safety, Perception, Capture, Input, Memory, Verification
Safety ──→ Perception
Perception ──→ Models
Verification ──→ Capture, Ocr, UIAutomation, Infrastructure
```

---

## 8. 数据流与执行管线

### Agent 任务执行管线

```
用户输入: "open notepad and type hello"
        │
        ▼
┌─ TaskParser.ParseTaskAsync ─────────────────────────┐
│  1. TryRuleBasedParse (正则匹配)                      │
│     ↓ 失败                                           │
│  2. TryLLMParseAsync (MiniMax API)                   │
│     → List<AgentStep>                                │
└─────────────────────────────────────────────────────┘
        │
        ▼
┌─ AgentOrchestrator.RunAsync ─────────────────────────┐
│  for each step:                                      │
│    ┌─ Risk Gate (ActionRiskGate.Evaluate) ──────┐    │
│    │  Risk < 0.3 → ALLOW                        │    │
│    │  0.3 ≤ Risk < 0.6 → CONFIRM               │    │
│    │  Risk ≥ 0.6 → BLOCK                        │    │
│    └────────────────────────────────────────────┘    │
│         │ ALLOW                                      │
│         ▼                                            │
│    ┌─ Element Grounding ────────────────────────┐    │
│    │  UIA + OCR → CandidateRanker → BestMatch  │    │
│    └────────────────────────────────────────────┘    │
│         │                                            │
│         ▼                                            │
│    ┌─ Before-state Capture ─────────────────────┐    │
│    │  Screenshot + OCR → beforeOcrText          │    │
│    └────────────────────────────────────────────┘    │
│         │                                            │
│         ▼                                            │
│    ┌─ ActionExecutor.ExecuteActionAsync ────────┐    │
│    │  click / type / hotkey / ...               │    │
│    └────────────────────────────────────────────┘    │
│         │ 成功                    │ 失败             │
│         ▼                        ▼                   │
│    ┌─ Verification ──┐    ┌─ RecoveryPlanner ──┐    │
│    │ Before vs After │    │ Refocus / Relocate │    │
│    │ Score → Pass?   │    │ → Retry            │    │
│    └─────────────────┘    └────────────────────┘    │
│                                                      │
│  → ExecutionTrace 记录全流程                          │
│  → VacpSkillIntegration.AfterSuccess 提取技能        │
└──────────────────────────────────────────────────────┘
        │
        ▼
  AgentTaskResponse (JSON)
```

### VACP 闭环管线

```
VacpRequest ──→ Screen Capture ──→ Vision Perceive ──→ Build Candidates
                                                              │
                 Verification ←── Execute ←── Risk Gate ←── Rank + Ground
                     │
                     ├── Success → VacpResult
                     └── Failure → Retry Once → VacpResult
```

### 技能迁移决策管线

```
TransferContext ──→ SkillScopeValidator ──→ NegativeTransferGuard ──→ AnchorMatcher
                                                                           │
                    TransferDecision ←── Score Weighting ←─────────────────┘
                    (INJECT / HUMAN_REVIEW / BLOCK)
```

---

## 9. 构建与运行

### 前置条件

- .NET 8 SDK（Windows）
- Windows 10 17763+ 或 Windows 11

### 构建

```bash
dotnet build PeekabooWin.sln -c Release
```

### 运行 CLI

```bash
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- <command> [args]
```

### 发布

```bash
dotnet publish -c Release -o publish
./publish/PeekabooWin.Cli.exe <command>
```

### 启动 HTTP API 服务

```bash
# 方式一：CLI 命令
peekaboo-win server --port 8080

# 方式二：独立 API 服务
dotnet run --project src/PeekabooWin.ApiServer/PeekabooWin.ApiServer.csproj

# 方式三：批处理
start_api_server.bat
```

### 环境变量

| 变量 | 说明 |
|------|------|
| `MINIMAX_API_KEY` | MiniMax LLM API Key（可选，不设置则仅使用规则解析） |

### 常用命令示例

```bash
peekaboo-win list-windows
peekaboo-win screenshot --screen --out screen.png
peekaboo-win click --x 500 --y 300
peekaboo-win type "hello world"
peekaboo-win hotkey --keys "ctrl+a"
peekaboo-win inspect --window "notepad"
peekaboo-win ocr --image screen.png
peekaboo-win agent --task "open notepad and type hello"
peekaboo-win skill-list
peekaboo-win skill-search --task "notepad enter text"
peekaboo-win skill-replay --id vs_notepad_edit --dry-run
```

---

## 10. 测试体系

### 测试框架

- **xUnit** 2.6.2
- **Microsoft.NET.Test.Sdk** 17.8.0

### 运行测试

```bash
dotnet test tests/PeekabooWin.Core.Tests/PeekabooWin.Core.Tests.csproj
```

### 测试文件清单

| 测试文件 | 测试目标 |
|---------|---------|
| `ActionRiskGateTests.cs` | `ActionRiskGate` 风险评估 |
| `ActionVerifierModelTests.cs` | `ActionVerifier` 执行验证 |
| `AgentRuntimeApiModelTests.cs` | Agent Runtime API 模型 |
| `ApiResponseModelTests.cs` | API 响应模型 |
| `AppProfileTests.cs` | `AppProfile` 应用配置 |
| `AsyncCancellationTests.cs` | 异步取消处理 |
| `ElementCandidateRankerTests.cs` | `ElementCandidateRanker` 元素排序 |
| `ExecutionTraceTests.cs` | `ExecutionTrace` 追踪记录 |
| `NegativeTransferGuardTests.cs` | `NegativeTransferGuard` 负面迁移防护 |
| `OcrResultTests.cs` | `OcrResult` OCR 结果 |
| `ParserFallbackTraceTests.cs` | 解析器降级追踪 |
| `RecoveryIntegrationTests.cs` | 恢复策略集成测试 |
| `RecoveryPlannerTests.cs` | `RecoveryPlanner` 恢复规划 |
| `SkillReplayEngineTests.cs` | `SkillReplayEngine` 技能回放 |
| `SkillScopeValidatorTests.cs` | `SkillScopeValidator` 作用域校验 |
| `TaskParserTests.cs` | `TaskParser` 任务解析 |
| `TimeoutTests.cs` | 超时处理 |

### 基准测试

`benchmarks/RealDesktop30/` 包含 30 个真实桌面场景的基准测试用例（RD-001 ~ RD-050），用于评估 Agent 在真实环境中的表现。

```bash
powershell -File benchmarks/RealDesktop30/run_benchmark.ps1
```

---

## 11. Guard 集成（SkillTransferController → AgentOrchestrator）

> **背景**：`SkillTransferController.Decide()` 原本只在 `ApiServer/Program.cs` 的 HTTP 端点被调用，Agent 主执行循环完全绕过了它。这是 V0.9 架构中最大的断裂点。

### 集成方案

**接入位置**：`AgentOrchestrator.RunAsync` step 执行循环，`if (ElementTargetActions.Contains(step.Action))` 块的最开头。

**接入逻辑**：

```
每个 ElementTargetActions 步骤执行前：
  1. 构建 TransferContext
     - Skill ← SearchWithContextAsync(task, windowKeyword) 检索到的 topSkill
     - CurrentApp ← AppProfile.FromWindowSignature(sig)
     - VisibleTexts ← sig.VisibleTexts（复用已有的 OCR text，不重复截图）
  2. 调用 _skillTransferController.Decide(transferContext)
  3. 记录 stepTrace.TransferDecision
  4. switch (decision.Result):
       INJECT → 继续执行
       BLOCK → step 标记失败，跳出（agent 回退到正常规划）
       HUMAN_REVIEW → CLI 无人交互 → 降级为 BLOCK
```

### 改动清单

| 文件 | 改动 |
|------|------|
| `Bootstrap/ServiceRegistration.cs` | 注册 `SkillTransferController` 为 Singleton |
| `Agent/AgentOrchestrator.cs` | 注入 `_skillTransferController`，在 step loop 插入迁移门控 |
| `Trace/ExecutionTrace.cs` | 新增 `TransferDecisionTrace` 类 + `StepTrace.TransferDecision` 字段 |
| `Tests/SkillTransferGuardIntegrationTests.cs` | 新增集成测试（3个用例） |

### 集成测试覆盖

| 测试 | 验证 |
|------|------|
| `RunAsync_ClickStep_TransferDecisionFieldExistsInStepTrace` | click/click-element-guess 步骤有 TransferDecision 记录 |
| `RunAsync_ScreenshotStep_TransferDecisionNotSet` | screenshot 步骤无 TransferDecision（不在 ElementTargetActions） |
| `RunAsync_InspectStep_TransferDecisionNotSet` | inspect 步骤无 TransferDecision |

### 追踪证据

每次 Guard 触发，ExecutionTrace 的 `StepTrace.TransferDecision` 字段会记录：

```json
{
  "SkillId": "vs_notepad_edit",
  "SkillName": "Notepad Edit Skill",
  "Action": "INJECT",
  "Reason": "Compatible domain, sufficient coverage",
  "BlockReason": null,
  "SkillMatchScore": 0.82,
  "CoverageScore": 0.95
}
```

---

## 12. 版本演进

| 版本 | 里程碑 | 核心特性 |
|------|--------|---------|
| V0.1 | 窗口窥视 + 绘图 + 鼠标键盘 | `list-windows`, `screenshot`, `click`, `type`, `hotkey` |
| V0.2 | UI Automation 控件树 | `inspect`, `find`, `click-element` |
| V0.3 | OCR 文字识别底座 | `ocr`, `find-on-screen` |
| V0.4 | LLM Agent Runtime | `agent --task`, 规则解析 + MiniMax LLM |
| V0.5 | HTTP API 服务 | `server --port`, RESTful API |
| V0.6 | VACP 闭环规划 | 风险门槛 + 执行验证 + 失败恢复 |
| V0.7 | Visual Skill Memory | 成功轨迹自动提取为 VisualSkill |
| V0.8 | Skill-Guided Execution | 多维评分 + SkillHint + SkillExecutionPolicy |
| V0.9 | Multi-App Skill Generalization | AppProfile + WindowSignature + SkillScope + NegativeTransferGuard |
| V0.10 | Engineering Hardening | DI 容器 + async/await + PeekabooException + SkillReplayEngine |
| V0.11 | Benchmark + Trace Schema | RealDesktop30 基准测试 |
| V0.12 | Async Blocking Audit | 异步阻塞审计 |
| V0.13 | Visual Robustness | 视觉鲁棒性增强 |
| V0.14 | Agent Runtime API | Agent Runtime API v1 Schema |
| V0.15 | Guard Integration | SkillTransferController 接入 AgentOrchestrator 主循环 |
