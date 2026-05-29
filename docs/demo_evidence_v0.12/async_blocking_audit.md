# PeekabooWin V0.12 异步阻塞调用审计报告

## 1. 审计概述

本报告对 PeekabooWin 源代码中所有同步阻塞调用进行全面审计，涵盖以下类别：

- `GetAwaiter().GetResult()` — 在同步上下文中强制等待异步操作完成
- `.Result`（Task 类型）— 阻塞获取 Task 结果
- `.Wait()` — 阻塞等待 Task 完成
- `Thread.Sleep()` — 阻塞当前线程

审计结果汇总：

| 类别 | 数量 |
|------|------|
| `GetAwaiter().GetResult()` | 3 |
| `.Result`（Task 类型） | 0 |
| `.Wait()` | 0 |
| `Thread.Sleep()` | 4 |
| **合计** | **7** |

其中可修复 5 项，不可修复 2 项。

## 2. GetAwaiter().GetResult() 审计

### 2.1 AgentService.cs:439

```csharp
CallMiniMaxAsync(systemPrompt, userPrompt, apiKey).GetAwaiter().GetResult()
```

- **所在方法**：`TryLLMParse()`
- **原因**：`ParseTask()` 返回 `List<AgentStep>`（同步签名），内部调用 LLM 必须阻塞等待结果
- **修复方案**：将 `ParseTask` 改为 `ParseTaskAsync`，沿调用链向上传播异步
- **状态**：V0.12 可修复

### 2.2 TaskParser.cs:352

```csharp
CallMiniMaxAsync(systemPrompt, userPrompt, apiKey).GetAwaiter().GetResult()
```

- **所在方法**：`TryLLMParse()`
- **原因**：与 AgentService.cs 相同，`ParseTask()` 为同步签名，LLM 调用被迫阻塞
- **修复方案**：将 `ParseTask` 改为 `ParseTaskAsync`，沿调用链向上传播异步
- **状态**：V0.12 可修复

### 2.3 VacpSkillIntegration.cs:27

```csharp
BuildWindowSignatureAsync(windowTitle).GetAwaiter().GetResult()
```

- **所在方法**：`BuildWindowSignature()` 同步包装方法
- **原因**：`SearchWithContext()` 为同步方法，其调用的 `BuildWindowSignature()` 不得不阻塞等待异步版本
- **修复方案**：将 `SearchWithContext` 改为 `SearchWithContextAsync`，沿调用链向上传播异步
- **状态**：V0.12 可修复

## 3. Thread.Sleep() 审计

### 3.1 SkillReplayEngine.cs:68

```csharp
Thread.Sleep(300)
```

- **用途**：聚焦窗口后等待窗口聚焦动画完成
- **修复方案**：替换为 `await Task.Delay(300, cancellationToken)`
- **状态**：V0.12 可修复

### 3.2 SkillReplayEngine.cs:138

```csharp
Thread.Sleep(200)
```

- **用途**：回放步骤之间等待 UI 更新
- **修复方案**：替换为 `await Task.Delay(200, cancellationToken)`
- **状态**：V0.12 可修复

### 3.3 InputService.cs:102

```csharp
Thread.Sleep(50)
```

- **用途**：`Click()` 方法中 `SetCursorPos` 与 `SendInput` 之间的延迟
- **原因**：Win32 API 时序要求，50ms 是确保光标位置更新后再发送点击输入的最小可靠延迟
- **修复方案**：替换为 `Task.Delay(50).Wait()` 并不更优，应保留 `Thread.Sleep` 并添加文档说明
- **状态**：不可修复 — Win32 SendInput 要求同步时序，`Thread.Sleep(50)` 是此场景下的正确做法

### 3.4 InputService.cs:136

```csharp
Thread.Sleep(50)
```

- **用途**：`RightClick()` 方法中 `SetCursorPos` 与 `SendInput` 之间的延迟
- **原因**：与 Click() 相同的 Win32 时序要求
- **状态**：不可修复 — 同上

## 4. 修复计划

| 编号 | 文件 | 行号 | 阻塞调用 | 修复方案 | 优先级 |
|------|------|------|----------|----------|--------|
| 1 | AgentService.cs | 439 | `GetAwaiter().GetResult()` | `ParseTask` → `ParseTaskAsync`，传播异步 | 高 |
| 2 | TaskParser.cs | 352 | `GetAwaiter().GetResult()` | `ParseTask` → `ParseTaskAsync`，传播异步 | 高 |
| 3 | VacpSkillIntegration.cs | 27 | `GetAwaiter().GetResult()` | `SearchWithContext` → `SearchWithContextAsync` | 高 |
| 4 | SkillReplayEngine.cs | 68 | `Thread.Sleep(300)` | → `await Task.Delay(300, cancellationToken)` | 中 |
| 5 | SkillReplayEngine.cs | 138 | `Thread.Sleep(200)` | → `await Task.Delay(200, cancellationToken)` | 中 |

修复顺序建议：

1. 先修复第 1、2 项（AgentService + TaskParser），二者共享同一调用链，需同步修改
2. 修复第 3 项（VacpSkillIntegration），独立调用链
3. 修复第 4、5 项（SkillReplayEngine），简单替换

## 5. 无法修复的项及原因

| 文件 | 行号 | 阻塞调用 | 原因 |
|------|------|----------|------|
| InputService.cs | 102 | `Thread.Sleep(50)` | Win32 `SendInput` API 要求同步时序：必须先 `SetCursorPos` 设置光标位置，等待位置生效后再发送点击事件。50ms 是经测试验证的最小可靠延迟。使用 `Task.Delay` 反而引入不必要的异步开销，且无法保证时序确定性。 |
| InputService.cs | 136 | `Thread.Sleep(50)` | 同上，`RightClick()` 方法中 `SetCursorPos` 与 `SendInput` 之间的 Win32 时序要求。 |

这两处 `Thread.Sleep` 属于平台原生 API 的硬性时序约束，不是代码设计缺陷，保留是合理且正确的选择。

## 6. 修复后预期结果

完成 V0.12 修复后：

- 阻塞调用总数：7 → 2
- 消除的阻塞调用：5 项（3 个 `GetAwaiter().GetResult()` + 2 个 `Thread.Sleep`）
- 剩余阻塞调用：2 项，均为 `InputService.cs` 中的 `Thread.Sleep(50)`，属于 Win32 API 时序约束，已文档化并有充分理由保留
- 异步传播收益：`ParseTaskAsync` 和 `SearchWithContextAsync` 的引入将使 LLM 调用和窗口签名构建不再阻塞线程池线程，提升并发性能
- 取消令牌支持：`SkillReplayEngine` 中的 `Task.Delay` 替换将支持 `cancellationToken`，使回放过程可中途取消
