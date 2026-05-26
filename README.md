# PeekabooWin - Windows Desktop Automation Kit

> 对应 macOS Peekaboo 架构，重心放在 Windows 原生自动化层。

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

## 架构图

```
┌─────────────────────────────────────────────────────┐
│                   PeekabooWin.Cli                   │
│  CommandRouter → ICommandHandler (DI-injected)      │
└──────────────┬──────────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────────┐
│              PeekabooWin.Core                        │
│  ┌──────────────┐  ┌────────────┐  ┌─────────────┐ │
│  │   Agent/     │  │   Memory/  │  │Infrastructure│ │
│  │ Orchestrator │  │ VisualSkill│  │  PekaLogger  │ │
│  │ TaskParser   │  │ SkillReplay│  │  TraceIdProv │ │
│  │ ActionExec   │  │ SkillScope │  │  TempFileMgr │ │
│  │ RiskGate     │  │ AppProfile │  │  UIAutomation│ │
│  │ VACP Integ   │  │ WindowSig  │  │  Win32 API   │ │
│  │ TraceLogger  │  │ AnchorMap  │  │  Exceptions  │ │
│  └──────────────┘  └────────────┘  └─────────────┘ │
└──────────────────────────────────────────────────────┘
```

## 版本里程碑

- [x] V0.1: 窗口窥视 + 绘图 + 鼠标键盘
- [x] V0.2: UI Automation 控件树
- [x] V0.3: OCR 文字识别底座
- [x] V0.4: LLM Agent Runtime（自然语言任务）
- [x] V0.5: HTTP API 服务（供 Hermes/OpenClaw 调用）
- [x] V0.6: VACP — Vision-Action Closed-loop Planner（风险门槛 + 执行验证 + 失败恢复）
  - V0.6.1: Risk Gate evidence（高风险投资中断）
  - V0.6.2: OCR-grounded AI interaction（豆包网页 AI 交互闭环）
- [x] **V0.8: Skill-Guided Execution（多维评分 + 执行策略 + SkillHint）**
  - skill-search / skill-use-preview / skill-execute-guided
  - SkillMatchScore（AppMatch / TextMatch / ActionMatch / RiskMatch / Recency）
  - SkillExecutionPolicy（L0 高风险任务自动拦截）
  - SkillHint 注入 VacpRequest（视觉 ranking，不 bypass VACP）
- [x] **V0.9: Multi-App Skill Generalization（跨应用迁移 + 安全边界）**
  - AppProfile + WindowSignature 实时窗口上下文
  - SkillScope + SkillScopeValidator（App/WindowType/Anchor/风险域校验）
  - AnchorMapping（语义锚点 ↔ OCR 文本映射）
  - Negative Transfer Guard（高风险跨域迁移自动拦截）
  - skill-search-context（窗口指纹 + AppProfile + 锚点候选搜索）
  - Demo11: 跨应用文本输入（Notepad → Doubao Web，score=0.78，INJECT）
  - Demo12: 跨窗口弹窗确认（Save Dialog → Error Dialog，blocked by forbidden domain）
  - Demo13: 高风险转移拦截（L0 skill on payment app = BLOCK）
- [x] **V0.10: Engineering Hardening（工程硬化）**
  - V0.10.0: DI 容器 + ICommandHandler + CommandRouter（Program.cs 1159→107 行）
  - V0.10.1: async/await 全链路 + TempFileManager（16→4 GetAwaiter）
  - V0.10.2: PeekabooException + error_code/hint/trace_id + PekaLogger（19 处 catch{} 消除）
  - V0.10.3: SkillReplayEngine 真执行（dry-run/execute/risk-gate）
  - V0.10.4: AgentOrchestrator 统一路径（TaskParser → RiskGate → ActionExecutor → Trace）

## 项目结构

```
PeekabooWin/
├── src/
│   ├── PeekabooWin.Cli/          # CLI 入口
│   │   ├── Program.cs            # 最小入口（~107 行）
│   │   ├── Bootstrap/            # DI 注册 + CommandRouter
│   │   └── Commands/             # ICommandHandler 实现
│   └── PeekabooWin.Core/         # 核心逻辑
│       ├── Agent/                # AgentOrchestrator + TaskParser + ActionExecutor + VACP
│       ├── Memory/               # SkillReplayEngine + VisualSkill + SkillScope
│       ├── Models/               # CommandResult + 枚举类型
│       ├── Exceptions/           # PeekabooException + 6 子类
│       └── Infrastructure/       # PekaLogger + TraceIdProvider + TempFileManager + Win32/UIA
├── tests/
│   └── PeekabooWin.Core.Tests/   # xUnit 测试（30 tests）
└── docs/
```

## 相关文档

- [V0.9 技术规格](./docs/V0.9_SPEC.md)
- [V0.9 冒烟测试](./docs/releases/V0.9_SMOKE_TEST.md)
- [Demo 11: 跨应用文本输入](./docs/demo/Demo11_CrossApp_TextInput_Transfer.md)
- [Demo 12: 跨窗口弹窗确认](./docs/demo/Demo12_CrossApp_DialogConfirm_Transfer.md)
- [Demo 13: 高风险转移拦截](./docs/demo/Demo13_HighRisk_Blocking.md)
- [已知问题](./docs/known_issues.md)
