# PeekabooWin V0.2 UIA Automation Smoke Test

## 测试环境
- OS: Windows
- .NET SDK: 8.0
- 测试窗口: 记事本 (notepad.exe)

## 构建
```bash
dotnet build PeekabooWin.sln -c Release
```

## V0.2 测试用例

### 1. inspect 命令
```bash
# 打开记事本后执行
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- inspect --window "记事本"
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- inspect --window "notepad" --depth 5
```

**验收标准**:
- [ ] 返回 JSON，含 window_title, element_count, root_elements
- [ ] 每个元素含 id, name, control_type, bounding_box, is_enabled
- [ ] 控件树嵌套 Children 结构
- [ ] 能识别 Button, Edit, Menu, MenuItem 等常见控件

### 2. find 命令
```bash
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- find --window "记事本" --role button
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- find --window "记事本" --name "文件"
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- find --window "记事本" --role edit
```

**验收标准**:
- [ ] 返回匹配元素列表，含完整控件信息
- [ ] count 字段准确
- [ ] 找不到时返回空列表（不报错）

### 3. click-element 命令
```bash
# 先打开记事本
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- click-element --window "记事本" --name "文件"
```

**验收标准**:
- [ ] 能触发菜单打开（记事本菜单）
- [ ] 返回 success: true

## V0.1 回归测试
```bash
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- list-windows
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- screenshot --screen --out artifacts/screen_v02.png
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- click --x 500 --y 300
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- type "V0.2 test"
dotnet run --project src/PeekabooWin.Cli/PeekabooWin.Cli.csproj -- hotkey --keys "ctrl+a"
```

## 已知限制
1. UIA 对部分应用（游戏、Electron 某些区域、Canvas）不可用 → V0.3 OCR 兜底
2. ClickElement 对无 InvokePattern 的控件降级为坐标点击
3. Windows OCR API 需要 MSIX 打包身份，V0.3 优先用 Tesseract
