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
peekaboo-win inspect --window "notepad" --depth 5

# 按名称/类型/AutomationId 查找控件
peekaboo-win find --window "notepad" --name "文件"
peekaboo-win find --window "notepad" --role button
peekaboo-win find --window "notepad" --role edit
peekaboo-win find --window "notepad" --automation-id "EditBox"

# 通过控件名点击（不靠坐标）
peekaboo-win click-element --window "notepad" --name "保存"
```

## 版本路线

- [x] V0.1: 窗口枚举 + 截图 + 鼠标键盘
- [x] V0.2: UI Automation 控件树 ← current
- [ ] V0.3: OCR 兜底（Tesseract / Windows OCR）
- [ ] V0.4: LLM Agent Runtime（自然语言任务）
- [ ] V0.5: HTTP API 服务（给 Hermes/OpenClaw 调用）
- [ ] V0.6: AgentShield 安全门控
