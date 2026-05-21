@echo off
REM PeekabooWin V0.1 Demo - Notepad automation
REM 用记事本验证所有核心能力

set BASE=src\PeekabooWin.Cli\bin\Debug\net8.0-windows\PeekabooWin.Cli.exe
if not exist "%BASE%" (
    echo Build required: cd src\PeekabooWin.Cli ^&^& dotnet build
    exit /b 1
)

echo === PeekabooWin V0.1 Demo ===
echo.

REM 1. 列出所有窗口
echo [1] list-windows
dotnet run -- list-windows
echo.

REM 2. 打开记事本
echo [2] Starting Notepad...
start notepad.exe
timeout /t 2 /nobreak >nul

REM 3. 截取记事本窗口
echo [3] screenshot --window notepad
dotnet run -- screenshot --window "notepad" --out "%USERPROFILE%\Desktop\notepad_test.png"
echo.

REM 4. 聚焦记事本
echo [4] focus-window
dotnet run -- focus-window --window "notepad"
timeout /t 1 /nobreak >nul

REM 5. 输入文本
echo [5] type "Hello from PeekabooWin!"
dotnet run -- type "Hello from PeekabooWin!"
echo.

REM 6. 全选复制
echo [6] ctrl+a (select all)
dotnet run -- hotkey --keys "ctrl+a"
echo.

REM 7. 关闭记事本
echo [7] alt+f4 (close)
dotnet run -- hotkey --keys "alt+f4"
timeout /t 1 /nobreak >nul

REM 8. 不保存关闭
echo [8] Enter (dont save)
dotnet run -- hotkey --keys "enter"

echo.
echo === Demo Complete ===
