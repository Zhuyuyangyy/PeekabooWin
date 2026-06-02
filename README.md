# PeekabooWin - Windows Desktop Automation Kit

> PeekabooWin 是 macOS 版 [Peekaboo](https://github.com/nicepkg/peekaboo) 的 Windows 对应实现，提供 Windows 原生桌面自动化能力。

## Project Status

| 能力 | 状态 | 说明 |
|------|------|------|
| 窗口管理 (list/focus/screenshot) | ✅ 可用 | 基于 Win32 API，稳定 |
| 输入模拟 (click/type/hotkey) | ✅ 可用 | 基于 SendInput，稳定 |
| UI Automation (inspect/find/click-element) | ✅ 可用 | 基于 UIA，稳定 |
| OCR 文字识别 | ✅ 可用 | 基于 Tesseract，需 tessdata |
| HTTP API Server | ✅ 可用 | RESTful API，支持 CORS |
| MCP Server | ✅ 可用 | 15 Tools，stdio 传输 |
| Agent 自然语言任务 | ⚠️ 需要 LLM Key | 规则解析仅支持简单格式（"click 100 200"），自然语言任务需要 `MINIMAX_API_KEY` |
| Skill Memory / Replay | 🧪 实验性 | 代码完整，但未经真实端到端验证 |
| Negative Transfer Guard | 🧪 实验性 | 已接入主循环，但只有单元测试触发过，无真实误迁移案例 |
| VACP 闭环规划 | 🧪 实验性 | 代码完整，但 verification→recovery 链路未经真实场景验证 |

**Live Benchmark 真实数据**（2026-06-02，无 LLM Key）：

| 指标 | 值 |
|------|---|
| L0/L1 任务完成率 | **0%** (0/40) |
| L2 Safety Block 正确率 | **100%** (10/10) |
| 根因 | 无 MINIMAX_API_KEY → regex_fallback → 自然语言任务无法解析 |

> 设置 `MINIMAX_API_KEY` 环境变量后，Agent 任务完成率预期会显著提升。

## 架构对应关系

| Peekaboo (macOS)    | PeekabooWin (Windows)        |
|---------------------|------------------------------|
| AXAccessibility API  | Win32 + UI Automation (UIA)  |
| ScreenCaptureKit     | GDI BitBlt / DXGI Capture    |
| CGEvent / NSEvent   | SendInput + SetCursorPos      |
| Swift Package Manager| .NET 8 SDK                   |

## 技术栈

- **语言**: C# (.NET 8)
- **底层**: Win32 API (P/Invoke)
- **UI自动化**: Windows UI Automation (UIA)
- **绘图**: GDI BitBlt
- **输入**: SendInput
- **输出**: JSON 结构化输出
- **MCP**: TypeScript + @modelcontextprotocol/sdk

## 构建

```bash
dotnet build PeekabooWin.sln -c Release
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- <command>
# 或发布后直接运行
dotnet publish -c Release -o publish
./publish/PeekabooWin.Cli.exe <command>
```

## 命令行一览

### V0.1 窗口管理 + 绘图 + 鼠标键盘
```bash
peekaboo-win list-windows
peekaboo-win list-windows --keyword "chrome"
peekaboo-win focus-window --window "notepad"
peekaboo-win screenshot --screen --out screen.png
peekaboo-win screenshot --window "notepad" --out notepad.png
peekaboo-win click --x 500 --y 300
peekaboo-win type "hello world"
peekaboo-win hotkey --keys "ctrl+a"
peekaboo-win hotkey --keys "alt+f4"
```

### V0.2 UI Automation 控件树
```bash
peekaboo-win inspect --window "notepad"
peekaboo-win inspect --window "notepad" --max-depth 5
peekaboo-win find --window "notepad" --name "文件"
peekaboo-win find --window "notepad" --control-type button
peekaboo-win find --window "notepad" --control-type edit
peekaboo-win find --window "notepad" --automation-id "EditBox"
peekaboo-win click-element --window "notepad" --name "保存"
```

### V0.3 OCR 文字识别底座
```bash
peekaboo-win screenshot --screen --out screen.png
peekaboo-win ocr --image screen.png
peekaboo-win find --window "notepad" --ocr-hint "username
```

### V0.4 LLM Agent Runtime
```bash
peekaboo-win agent --task "open notepad and type hello"
peekaboo-win agent --task "复杂任务" --max-steps 3
peekaboo-win agent --task "任务" --context "额外上下文"
```

> Agent 基于规则解析，无 API Key 时也能工作。设置 `MINIMAX_API_KEY` 环境变量可启用 LLM 分析复杂任务。

### V0.5 HTTP API 服务
```bash
peekaboo-win server --port 8080

curl http://localhost:8080/health
curl http://localhost:8080/windows
curl -X POST http://localhost:8080/click -d "{\"x\":100,\"y\":200}" -H "Content-Type: application/json"
curl -X POST http://localhost:8080/agent -d "{\"task\":\"click 100 200\"}" -H "Content-Type: application/json"
curl -X POST http://localhost:8080/type -d "{\"text\":\"hello world\"}" -H "Content-Type: application/json"
```

> HTTP API 供 OpenClaw/Hermes 等外部 Agent 驱动 Windows 桌面自动化。所有端点返回 JSON，支持 CORS 跨域请求。

### V0.7 Visual Skill Memory — UI 模式记忆
```bash
# 列出所有已提取的视觉技能
peekaboo-win skill-list

# 写入预设演示技能（用于 demo / 测试）
peekaboo-win skill-seed

# 搜索可用技能（V0.8+）
peekaboo-win skill-search --task "notepad enter text"
peekaboo-win skill-search --task "dialog confirm" --app-pattern "notepad"
```

> V0.7 在 VACP 基础上增加 Skill Memory，> - 成功执行的 VACP 轨迹自动提取为 VisualSkill
> - 下次遇到类似屏幕时，检索技能库，直接回放，跳过冗余的视觉感知
> - 支持跨 session 持久化（`skills.json`）

### V0.8 Skill-Guided Execution — 多维评分 + 执行策略 + SkillHint
```bash
# 技能搜索（多维评分）
peekaboo-win skill-search --task "type hello in notepad"

# 技能使用预览（返回 top candidate + usable_count）
peekaboo-win skill-use-preview --task "save file" --app notepad

# 执行引导（SkillHint 注入任务 trace，不 bypass VACP）
peekaboo-win agent --task "type hello" --skill-id vs_notepad_edit --dry-run
```

> SkillHint 注入 VacpRequest（视觉 ranking，不 bypass VACP），SkillMatchScore 包含 AppMatch + TextMatch + ActionMatch + RiskMatch + Recency。L0 高风险任务自动拦截。

### V0.9 Multi-App Skill Generalization — 跨应用技能迁移 + 安全边界
```bash
# 上下文感知技能搜索（V0.9 新增，返回窗口指纹 + AppProfile + 锚点候选）
peekaboo-win skill-search-context --task "type hello" --window notepad

# 搜索结果包含 SkillScope 校验（应用/窗口类型/锚点/风险域）
peekaboo-win skill-search-context --task "confirm dialog" --window "另存为"
```

> V0.9 新增：
> - **AppProfile + WindowSignature**：实时窗口上下文指纹
> - **SkillScope + SkillScopeValidator**：应用/WindowType/锚点/风险域校验
> - **AnchorMapping**：语义锚点 ↔ OCR 文本映射
> - **Negative Transfer Guard**：高风险跨域技能迁移自动拦截（L0 skill + payment/admin = BLOCK）
> - **skill-search-context**：返回 window_signature + app_profile + anchor_candidates + results（含 scope 校验结果）

### V0.10 工程硬化 — DI + Async + 错误模型 + Skill Replay + VACP-Agent 统一
```bash
# Skill Replay（真执行，支持 dry-run）
peekaboo-win skill-replay --id vs_notepad_edit --dry-run
peekaboo-win skill-replay --id vs_dialog_confirm --execute
peekaboo-win skill-replay --id vs_notepad_edit --execute --window "记事本"
```

### V0.15 MCP Server — LLM 原生桌面自动化

PeekabooWin 现在可以作为 MCP Server 使用，让任何支持 MCP 的 LLM 客户端直接调用 Windows 桌面自动化能力。

**启动方式**：

```bash
# 1. 先启动 PeekabooWin API Server
peekaboo-win server --port 8025

# 2. 构建 MCP Server
cd src/peekaboo-mcp
npm install && npm run build
```

**MCP 客户端配置**：

```json
{
  "mcpServers": {
    "peekaboo-win": {
      "command": "node",
      "args": ["/path/to/PeekabooWin/src/peekaboo-mcp/dist/index.js"],
      "env": {
        "PEEKABOO_API_URL": "http://localhost:8025"
      }
    }
  }
}
```

**15 个 MCP Tool**：

| Tool | 说明 |
|------|------|
| `peekaboo_list_windows` | 列出桌面窗口 |
| `peekaboo_focus_window` | 聚焦窗口 |
| `peekaboo_screenshot` | 截图（全屏/指定窗口） |
| `peekaboo_click` | 鼠标点击 |
| `peekaboo_type` | 文本输入 |
| `peekaboo_press_key` | 单键按下 |
| `peekaboo_hotkey` | 快捷键组合 |
| `peekaboo_ocr` | OCR 文字识别 |
| `peekaboo_inspect` | UIA 控件树检查 |
| `peekaboo_agent_run` | Agent 自动任务 |
| `peekaboo_skill_search` | 技能搜索 |
| `peekaboo_skill_list` | 技能列表 |
| `peekaboo_skill_replay` | 技能回放 |
| `peekaboo_risk_evaluate` | 风险评估 |
| `peekaboo_execute` | 通用命令执行 |

> MCP Server 通过 HTTP 桥接 PeekabooWin API Server，使用 stdio 传输协议，兼容 Claude Desktop、Cursor、Windsurf 等所有 MCP 客户端。

## 架构图

```
┌──────────────────────────────────────────────────────────────┐
│                    MCP 客户端 (Claude/Cursor/...)             │
│                  MCP Protocol (stdio)                         │
└──────────────────────────┬───────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────┐
│                  peekaboo-mcp (TypeScript)                    │
│              15 Tools → HTTP → PeekabooWin API               │
└──────────────────────────┬───────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────┐
│                   PeekabooWin.Cli                             │
│  CommandRouter → ICommandHandler (DI-injected)               │
│  ApiServer (HTTP :8025)                                       │
└──────────────────────────┬───────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────┐
│                   PeekabooWin.Core                            │
│  ┌──────────────┐  ┌────────────┐  ┌─────────────┐          │
│  │   Agent/     │  │   Memory/  │  │Infrastructure│          │
│  │ Orchestrator │  │ VisualSkill│  │  PekaLogger  │          │
│  │ TaskParser   │  │ SkillReplay│  │  TraceIdProv │          │
│  │ ActionExec   │  │ SkillScope │  │  TempFileMgr │          │
│  │ RiskGate     │  │ AppProfile │  │  UIAutomation│          │
│  │ VACP Integ   │  │ WindowSig  │  │  Win32 API   │          │
│  │ TransferCtrl │  │ AnchorMap  │  │  Exceptions  │          │
│  └──────────────┘  └────────────┘  └─────────────┘          │
└──────────────────────────────────────────────────────────────┘
```

## CHANGELOG

- **2025-05**: 窗口管理 + 输入模拟 + 截图 (V0.1)
- **2025-05**: UI Automation 控件树 (V0.2)
- **2025-05**: OCR 文字识别 (V0.3)
- **2025-05**: LLM Agent Runtime + HTTP API (V0.4-V0.5)
- **2025-05**: VACP 闭环规划 + 风险门控 (V0.6)
- **2025-05**: Visual Skill Memory (V0.7, 🧪 实验性)
- **2025-05**: Skill-Guided Execution + 多维评分 (V0.8, 🧪 实验性)
- **2025-05**: 跨应用技能迁移 + Negative Transfer Guard (V0.9, 🧪 实验性)
- **2025-05**: 工程硬化 (DI + async + 异常体系 + SkillReplay) (V0.10)
- **2025-06**: MCP Server + Guard 接入主循环 + Live Benchmark

## 项目结构

```
PeekabooWin/
├── src/
│   ├── PeekabooWin.Cli/          # CLI 入口
│   │   ├── Program.cs            # 最小入口（~107 行）
│   │   ├── Bootstrap/            # DI 注册 + CommandRouter
│   │   ├── Commands/             # ICommandHandler 实现
│   │   └── ApiServer.cs          # 内嵌 HTTP API 服务器
│   ├── PeekabooWin.Core/         # 核心逻辑
│   │   ├── Agent/                # AgentOrchestrator + TaskParser + ActionExecutor + VACP + SkillTransferController
│   │   ├── Memory/               # SkillReplayEngine + VisualSkill + SkillScope + NegativeTransferGuard
│   │   ├── Planning/             # VacpPlanner + ActionCandidate
│   │   ├── Safety/               # ActionRiskGate
│   │   ├── Perception/           # ElementCandidateRanker + UiElement
│   │   ├── Verification/         # ActionVerifier + BeforeAfterVerifier
│   │   ├── Capture/              # CaptureService (GDI BitBlt)
│   │   ├── Input/                # InputService + StableTyper
│   │   ├── Ocr/                  # OcrService (Tesseract)
│   │   ├── UIAutomation/         # UIAutomationService + SeeService
│   │   ├── Windows/              # WindowService (Win32)
│   │   ├── Trace/                # ExecutionTrace + TransferDecisionTrace
│   │   ├── Models/               # CommandResult + 枚举类型
│   │   ├── Exceptions/           # PeekabooException + 6 子类
│   │   └── Infrastructure/       # PekaLogger + TraceIdProvider + TempFileManager
│   ├── PeekabooWin.ApiServer/    # 独立 ASP.NET Core API 服务
│   └── peekaboo-mcp/             # TypeScript MCP Server
│       ├── src/index.ts          # 15 MCP Tools
│       ├── package.json
│       └── tsconfig.json
├── tests/
│   └── PeekabooWin.Core.Tests/   # xUnit 测试（134 tests）
├── benchmarks/
│   └── RealDesktop30/            # 50 真实桌面场景基准测试
├── tessdata/                     # Tesseract 语言数据
└── docs/                         # 版本规格与 Demo 证据
```

## 相关文档

- [Code Wiki](./CODE_WIKI.md)
- [V0.9 技术规格](./docs/V0.9_SPEC.md)
- [V0.9 冒烟测试](./docs/releases/V0.9_SMOKE_TEST.md)
- [Demo 11: 跨应用文本输入](./docs/demo/Demo11_CrossApp_TextInput_Transfer.md)
- [Demo 12: 跨窗口弹窗确认](./docs/demo/Demo12_CrossApp_DialogConfirm_Transfer.md)
- [Demo 13: 高风险转移拦截](./docs/demo/Demo13_HighRisk_Blocking.md)
- [已知问题](./docs/known_issues.md)
