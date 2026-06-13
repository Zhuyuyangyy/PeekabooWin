---
name: peekaboo-win
description: >-
  Use this skill whenever the user wants to automate Windows desktop tasks using PeekabooWin.
  This includes: taking screenshots of windows or the full screen, scanning visible text with
  OCR and screen coordinates, clicking on text elements in any application, UI Automation (UIA)
  element inspection and invocation, typing text, pressing keys, and running multi-step desktop
  automation workflows. Works on Win32, WPF, XAML, Electron, Chromium, UWP, and legacy Shell apps.
description_zh: >-
  当用户需要使用 PeekabooWin 自动化 Windows 桌面任务时使用此技能。包括：截取窗口或全屏截图、
  用 OCR 扫描可见文字及屏幕坐标、点击任意应用中的文字元素、UI Automation 元素检查和调用、
  输入文本、按键、运行多步骤桌面自动化工作流。支持 Win32、WPF、XAML、Electron、Chromium、UWP
  和传统 Shell 应用。
---

# PeekabooWin - Windows Desktop Automation

## Overview

PeekabooWin is a C# .NET 8 Windows Desktop Automation Kit located at `D:\GITHUB\PeekabooWin`.
It provides screenshot capture, OCR-based text detection, UI Automation (UIA), and input simulation
for automating any Windows application.

## CLI Usage

All commands are run via:

```bash
cd D:\GITHUB\PeekabooWin
dotnet run --project src/PeekabooWin.Cli -- <command> [args]
```

### Core Commands

**screenshot** - Capture a window or full screen:
```bash
# Capture specific window (title substring match)
dotnet run --project src/PeekabooWin.Cli -- screenshot --window "Notepad"

# Capture full screen
dotnet run --project src/PeekabooWin.Cli -- screenshot
```
Returns: `{ path, width, height, window_title, scale_factor }`

**ocr-scan** - Scan all visible text with physical screen coordinates (works on ANY app):
```bash
dotnet run --project src/PeekabooWin.Cli -- ocr-scan --window "Edge"
dotnet run --project src/PeekabooWin.Cli -- ocr-scan  # full screen
```
Returns: `{ element_count, elements: [{ text, screen_x, screen_y, screen_cx, screen_cy, width, height }] }`
All coordinates are in **physical pixels** (DPI-scaled).

**ocr-click** - Click on a text element found by OCR:
```bash
dotnet run --project src/PeekabooWin.Cli -- ocr-click --text "File" --window "Notepad"
```
Returns: `{ text, clicked_x, clicked_y, rel_x, rel_y }`

**find-on-screen** - Locate text on screen without clicking:
```bash
dotnet run --project src/PeekabooWin.Cli -- find-on-screen --text "Submit" --window "Chrome"
```

**click** - Click at specific coordinates:
```bash
dotnet run --project src/PeekabooWin.Cli -- click --x 500 --y 300
```

**type** - Type text into the focused element:
```bash
dotnet run --project src/PeekabooWin.Cli -- type --text "Hello World"
```

**press** - Press a key (esc, enter, tab, backspace, delete):
```bash
dotnet run --project src/PeekabooWin.Cli -- press --key enter
```

**hotkey** - Execute a hotkey combination:
```bash
dotnet run --project src/PeekabooWin.Cli -- hotkey --keys "ctrl+s"
```

**uia-inspect** - Inspect UIA elements in a window:
```bash
dotnet run --project src/PeekabooWin.Cli -- uia-inspect --window "Notepad"
```

**uia-invoke** - Invoke (click) a UIA element by name:
```bash
dotnet run --project src/PeekabooWin.Cli -- uia-invoke --name "OK" --window "Dialog"
```

## Coordinate System

CRITICAL: PeekabooWin uses **physical pixel coordinates** throughout.

- `scale_factor` on user's machine is **1.5** (150% DPI)
- `WindowService.GetWindowRect()` returns **logical** (DPI-virtualized) coordinates
- OCR, UIA, and screenshots operate in **physical** pixel space
- All coordinate conversions between logical and physical use: `physical = logical * dpi_scale`

When using `ocr-scan` or `ocr-click`, the system automatically handles coordinate conversion.
All returned `screen_x`, `screen_y`, `screen_cx`, `screen_cy` values are physical screen pixels.

## Application Compatibility

| App Type | UIA Support | OCR Support | Recommended Approach |
|----------|------------|-------------|---------------------|
| Win32/WPF/XAML | Full | Full | UIA first, OCR fallback |
| Chromium (Edge/Chrome) | Partial | Full | ocr-scan + ocr-click |
| Electron (VS Code, etc.) | Minimal | Full | ocr-scan + ocr-click |
| UWP (Settings, etc.) | Minimal | Full | ocr-scan + ocr-click |
| Legacy Shell (Explorer) | Minimal | Full | ocr-scan + ocr-click |

## Automation Workflow

1. **Identify target window**: Use `screenshot --window "title"` to confirm the window exists and is visible
2. **Scan for elements**: Use `ocr-scan --window "title"` to find all text and their coordinates
3. **Interact**: Use `ocr-click --text "target" --window "title"` to click, or `type`/`press`/`hotkey` for input
4. **Verify**: Take another `screenshot` to confirm the action succeeded

## Important Notes

- Window matching uses **title substring** (case-insensitive), not process name
- GDI BitBlt captures what's **visible on screen** - if a window is overlapped, the capture includes overlapping content
- OCR scan is more reliable than UIA for non-Win32 applications
- Always verify click results with a follow-up screenshot
- The `scale_factor` field in screenshot results indicates the DPI scale (1.5 on this machine)
