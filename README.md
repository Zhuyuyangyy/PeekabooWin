# PeekabooWin - Windows Desktop Automation Kit

> 参考 macOS Peekaboo 架构，重写 Windows 原生自动化层。

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
- **截图**: GDI BitBlt
- **输入**: SendInput
- **输出**: JSON 结构化

## 构建

```bash
dotnet build PeekabooWin.sln -c Release
```

## V0.1 命令（已验收）

```bash
# 窗口管理
peekaboo-win list-windows
peekaboo-win list-windows --keyword "chrome"
peekaboo-win focus-window --window "notepad"

# 截图
peekaboo-win screenshot --screen --out screen.png
peekaboo-win screenshot --window "notepad" --out notepad.png

# 输入
peekaboo-win click --x 500 --y 300
peekaboo-win type "hello world"
peekaboo-win hotkey --keys "ctrl+a"
peekaboo-win hotkey --keys "alt+f4"
```

## V0.2 命令（UI Automation 控件树）

```bash
# 遍历窗口控件树
peekaboo-win inspect --window "notepad"
peekaboo-win inspect --window "notepad" --max-depth 5

# 按名称/类型/AutomationId 查找控件
peekaboo-win find --window "notepad" --name "文件"
peekaboo-win find --window "notepad" --control-type button
peekaboo-win find --window "notepad" --control-type edit
peekaboo-win find --window "notepad" --automation-id "EditBox"

# 通过控件名点击（不靠坐标）
peekaboo-win click-element --window "notepad" --name "保存"
```

## V0.3 命令（OCR 文字识别兜底）

```bash
# OCR 识别截图中的文字
peekaboo-win ocr --screen --out ocr_screen.png
peekaboo-win ocr --window "Clash Verge" --out ocr_window.png

# 在截图中搜索文字并点击
peekaboo-win ocr --window "Clash Verge" --text "设置" --click
peekaboo-win ocr --screen --text "关闭" --click

# 指定语言（默认 chi_sim+eng）
peekaboo-win ocr --window "notepad" --lang eng --text "Save"
```

> OCR 基于 Tesseract 5.x，训练数据（chi_sim+eng）需放置在 `tessdata/` 目录。

## V0.4 命令（Agent 自然语言任务）

```bash
# 自然语言任务执行
peekaboo-win agent --task "click 100 200"
peekaboo-win agent --task "type hello world"
peekaboo-win agent --task "press enter"
peekaboo-win agent --task "press ctrl+c"
peekaboo-win agent --task "focus notepad"
peekaboo-win agent --task "inspect notepad"
peekaboo-win agent --task "take a screenshot"
peekaboo-win agent --task "list windows"

# 可选参数
peekaboo-win agent --task "click 100 200" --dry-run
peekaboo-win agent --task "复杂任务" --max-steps 3
peekaboo-win agent --task "任务" --context "额外上下文"
```

> Agent 基于规则解析，无 API Key 时也能工作。设置 `MINIMAX_API_KEY` 环境变量可启用 LLM 解析复杂任务。

## V0.5 命令（HTTP API 服务）

```bash
# 启动 HTTP API 服务器
peekaboo-win server --port 8080

# API 调用示例
curl http://localhost:8080/health
curl http://localhost:8080/windows
curl -X POST http://localhost:8080/click -d "{\"x\":100,\"y\":200}" -H "Content-Type: application/json"
curl -X POST http://localhost:8080/agent -d "{\"task\":\"click 100 200\"}" -H "Content-Type: application/json"
curl -X POST http://localhost:8080/type -d "{\"text\":\"hello world\"}" -H "Content-Type: application/json"
```

> HTTP API 供 OpenClaw/Hermes 等外部 Agent 驱动 Windows 桌面自动化。
> 所有端点返回 JSON，支持 CORS 跨域请求。

## 版本路线

- [x] V0.1: 窗口枚举 + 截图 + 鼠标键盘
- [x] V0.2: UI Automation 控件树
- [x] V0.3: OCR 文字识别兜底
- [x] V0.4: LLM Agent Runtime（自然语言任务）
- [x] V0.5: HTTP API 服务（给 Hermes/OpenClaw 调用）← current
- [ ] V0.6: AgentShield 安全门控