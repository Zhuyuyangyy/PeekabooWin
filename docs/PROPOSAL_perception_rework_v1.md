# PeekabooWin 感知层重构方案 V1

> 版本: draft-2026-06-11
> 目标: 用多模态 LLM 视觉 grounding 替代截屏→OCR→坐标的间接感知链路，大幅提升元素定位和点击控制的准确率

---

## 问题诊断

当前 PeekabooWin 的感知-执行链路可以概括为：截屏 → Tesseract/WinRT OCR → 文本匹配定位 → 坐标计算 → SendInput 点击。这条链路在每一层都有不可逆的信息损耗，叠加之后端到端准确率很低。

具体问题拆解如下：

### P1: OCR 作为主感知手段，天花板太低

`OcrService` 基于 Windows.Media.Ocr（WinRT），只能识别文字，无法理解 UI 语义。一个没有文字的图标按钮、一个只有图形的 toggle switch，OCR 完全看不到。而且 `OcrService` 的 confidence 始终硬编码为 1.0，下游的 `ElementGroundingScore` 无法区分可靠识别和猜测。

### P2: 两条感知路径没有融合

`UiElement.Source` 可以是 `uia` 或 `ocr`，但两者在 `ElementCandidateRanker` 里用的是同一套评分逻辑。UIA 给的元素有精确的 BoundingBox 和 ControlType，OCR 给的只有文字区域——这两类信号的质量完全不同，不应该用同一权重去评。

### P3: 坐标点击是间接的

`ActionExecutor` 的 `click-element` 动作流程是：UIA 找到元素 → 取 BBox 中心点 → `InputService.Click(x, y)`。这个过程中 UIA 已经拿到了 `AutomationElement`，完全可以直接调用 `InvokePattern.Invoke()`，根本不需要走坐标。只有 UIA 拿不到的场景（游戏、Electron、自绘控件）才需要退化到坐标点击。

### P4: 截屏验证太粗

`ActionVerifier` 用全屏 OCR 文本对比来判断"点击是否生效"。任何文字变化都算成功，哪怕是完全无关的变化。`BeforeAfterVerifier` 的 `ComputeElementStateChange` 是 stub（始终返回 0.5），`ComputeVisualChange` 用 `GetPixel()` 做像素比较且不做对齐。

### P5: 无 DPI 感知

`CaptureService` 用 `GetSystemMetrics` 取屏幕尺寸，`InputService` 用 `SetCursorPos` 设置坐标——两者都没有 DPI 缩放处理。在 150%/200% 缩放下，截屏拿到的像素坐标和 `SetCursorPos` 使用的逻辑坐标不一致，直接导致点击偏移。

---

## 改进方案总览

### 核心思路

将感知层从「截屏 → OCR → 文本匹配」改为「UIA 优先 → 多模态 LLM 兜底」的双层架构。

```
                    感知请求
                       │
              ┌────────▼────────┐
              │  UIA 快速通道     │ ← 优先走 UIA，直接拿到控件句柄
              │  (本地, <50ms)   │
              └────────┬────────┘
                       │ UIA 找不到？
              ┌────────▼────────┐
              │  LLM 视觉 grounding│ ← 截屏发给多模态 LLM，返回结构化元素
              │  (远程, 1-3s)     │
              └────────┬────────┘
                       │
              ┌────────▼────────┐
              │  统一元素模型     │ ← 不管哪条路来的，都归一化为 GroundedElement
              └────────┬────────┘
                       │
              ┌────────▼────────┐
              │  执行策略选择     │
              │  UIA Invoke >    │ ← 有句柄就直接 Invoke，没有才走坐标
              │  坐标点击        │
              └────────┬────────┘
                       │
              ┌────────▼────────┐
              │  LLM 视觉验证    │ ← 执行后截屏，让 LLM 判断是否成功
              └─────────────────┘
```

### 架构变更概览

| 当前模块 | 改造方向 | 改动幅度 |
|---------|---------|---------|
| `OcrService` | 降级为辅助角色，不再作为主感知手段 | 保留，不删 |
| `CaptureService` | 保留，增加 DPI 感知 + DXGI 可选 | 中改 |
| `UIAutomationService` | 提升为第一感知通道，增加深度遍历 | 大改 |
| `ElementCandidateRanker` | 重写，按 source 分级评分 | 重写 |
| `ElementGroundingScore` | 重写，适配新的统一元素模型 | 重写 |
| `InputService` | 增加 DPI 缩放 + 坐标校验 | 小改 |
| `ActionVerifier` | 用 LLM 视觉验证替代 OCR 文本对比 | 重写 |
| **新增** `LlmGroundingService` | 多模态 LLM 视觉 grounding 服务 | 新建 |
| **新增** `PerceptionRouter` | 感知路由器，协调 UIA 和 LLM | 新建 |
| **新增** `GroundedElement` | 统一元素模型 | 新建 |
| **新增** `DpiContext` | DPI 感知上下文 | 新建 |

---

## Phase 1: 基础设施层 (DPI + Capture 改造)

### 1.1 DpiContext — DPI 感知基础

**目标**: 统一坐标系，消除 DPI 缩放导致的坐标偏移。

```csharp
// 新增: PeekabooWin.Core.Infrastructure.DpiContext
public class DpiContext
{
    // 获取指定窗口的 DPI 缩放因子
    // Win10 1607+ 用 GetDpiForWindow，低版本 fallback 到 GetDeviceCaps
    public double GetScaleFactor(IntPtr hwnd);
    
    // 逻辑坐标 → 物理像素
    public (int px, int py) LogicalToPhysical(int lx, int ly, double scale);
    
    // 物理像素 → 逻辑坐标
    public (int lx, int ly) PhysicalToLogical(int px, int py, double scale);
    
    // 获取主屏缩放因子
    public double GetPrimaryScale();
}
```

**改造影响**:
- `CaptureService.CaptureScreen` 返回的 `CaptureResult` 新增 `ScaleFactor` 字段
- `InputService.Click` 在 `SetCursorPos` 之前做 DPI 缩放转换
- 所有 BoundingBox 坐标统一为逻辑坐标（DPI-independent），只在最终执行时转换

### 1.2 CaptureService 增强

**目标**: 截屏能力升级，支持 DXGI Desktop Duplication（可选），为 LLM grounding 提供高质量图片。

```csharp
public class CaptureService
{
    // 现有方法保留，增加 DPI 感知
    public CaptureResult CaptureScreen(string outputPath); // 新增 ScaleFactor 返回
    
    // 新增: 截取指定区域（给 LLM 发局部图，减少 token 消耗）
    public CaptureResult CaptureRegion(int x, int y, int w, int h, string outputPath);
    
    // 新增: 返回 base64 编码的 PNG（直接给 LLM API 用，避免文件 I/O）
    public string CaptureScreenAsBase64(int maxWidth = 1920, int quality = 80);
    
    // 新增: 降采样（LLM 不需要原始分辨率，1920px 够用）
    public byte[] DownsampleForLlm(string imagePath, int maxWidth = 1920);
}
```

**关键设计决策**:
- 保留 GDI BitBlt 作为默认方案（兼容性好）
- DXGI Desktop Duplication 作为可选方案（能捕获 DirectX/硬件加速内容）
- 新增 `CaptureScreenAsBase64` 避免每次都要写文件再读文件

---

## Phase 2: UIA 第一通道 (优先级最高的改造)

### 2.1 UIAutomationService 深度改造

**目标**: 让 UIA 成为默认感知手段，覆盖尽可能多的场景。

**当前问题**:
- `maxDepth` 默认 4，复杂 UI 可能遍历不全
- `CollectElements` 静默吞异常，部分子树跳过无感知
- `_elementIdCounter` 非线程安全
- `InvokeElement` 里 `new InputService()` 绕过 DI

**改造内容**:

```csharp
public class UIAutomationService
{
    // 改造: 注入 InputService，不再内联 new
    public UIAutomationService(WindowService windowService, InputService inputService);
    
    // 改造: 自适应深度遍历
    // 策略: 先用 maxDepth=3 快速扫一遍，如果结果不够再用 maxDepth=6 深度扫
    public UIAInspectResult Inspect(string windowKeyword, int maxDepth = 0); // 0 = adaptive
    
    // 新增: 语义搜索——按自然语言描述找元素
    // 内部用关键词扩展 + 模糊匹配，比如 "发送按钮" 能匹配 Name="Send" / Name="发送"
    public List<UIAElementInfo> FindBySemantic(string windowKeyword, string description);
    
    // 新增: 获取元素的完整路径（用于 LLM 理解 UI 结构）
    public string GetElementPath(string windowKeyword, string elementName);
    
    // 改造: InvokeElement 返回详细的执行结果，不只是 bool
    public InvokeResult InvokeElement(AutomationElement el);
}

public class InvokeResult
{
    public bool Success { get; set; }
    public InvokeMethod Method { get; set; } // InvokePattern / ValuePattern / CoordinateClick
    public string? ErrorDetail { get; set; }
}
```

**自适应深度策略**:
```
1. 第一遍 maxDepth=3，获取元素列表
2. 如果目标窗口是 dialog 类型，且元素数 < 5 → 扩大到 maxDepth=6 再扫一遍
3. 如果遍历过程中任何一个子树抛异常 → 记录到 InspectResult.PartialSubtrees
4. 返回结果中标注 TraversalComplete = true/false
```

### 2.2 UIA 直点（消除坐标中间层）

**当前问题**: `click-element` 动作拿到 `AutomationElement` 后，不直接 Invoke，而是取 BBox 中心点走坐标点击。

**改造**:

```csharp
// ActionExecutor 中 click-element 的新执行逻辑
public async Task<(bool, string)> ExecuteClickElement(string window, string name)
{
    // 1. 优先尝试 UIA InvokePattern
    var element = _uiaService.FindByName(window, name).FirstOrDefault();
    if (element != null)
    {
        var result = _uiaService.InvokeElement(element.AutomationElement);
        if (result.Success) return (true, $"Invoked via {result.Method}");
    }
    
    // 2. UIA Invoke 失败，退化到坐标点击（此时才需要截屏+定位）
    // 这里会触发 PerceptionRouter，由 LLM 做视觉 grounding
    var grounded = await _perceptionRouter.GroundElement(window, name);
    if (grounded != null)
    {
        _inputService.Click(grounded.ClickX, grounded.ClickY);
        return (true, "Clicked via coordinate fallback");
    }
    
    return (false, "Element not found via UIA or vision");
}
```

---

## Phase 3: LLM 视觉 Grounding (核心新增模块)

### 3.1 LlmGroundingService — 多模态 LLM 感知服务

**目标**: 当 UIA 无法定位目标时，截屏发给多模态 LLM，让 LLM 直接返回结构化的 UI 元素信息。

**API 协议设计**:

```csharp
// 新增: PeekabooWin.Core.Perception.LlmGroundingService
public class LlmGroundingService
{
    private readonly ILlmClient _llmClient;
    private readonly CaptureService _captureService;
    
    // 核心方法: 截屏 + 发给 LLM + 解析返回的结构化元素
    public async Task<LlmGroundingResult> GroundElementsAsync(
        GroundingRequest request, 
        CancellationToken ct);
    
    // 单元素定位: "找到'确定'按钮在哪"
    public async Task<GroundedElement?> FindElementAsync(
        string windowKeyword, 
        string description, 
        CancellationToken ct);
    
    // 全场景理解: "当前屏幕是什么状态，有哪些可交互元素"
    public async Task<ScreenUnderstanding> UnderstandScreenAsync(
        string windowKeyword, 
        CancellationToken ct);
}
```

**LLM Prompt 设计（核心）**:

```
你是一个 Windows 桌面 UI 分析助手。我将给你一张 Windows 屏幕截图。

请分析图片中的 UI 元素，以 JSON 格式返回：

{
  "screen_type": "editor|browser|dialog|settings|file_explorer|unknown",
  "window_title_estimate": "推测的窗口标题",
  "elements": [
    {
      "id": "el_001",
      "type": "button|textbox|checkbox|dropdown|link|icon|menu|tab|slider|...",
      "label": "元素上的可见文字或功能描述",
      "bbox": { "x": 100, "y": 200, "width": 80, "height": 30 },
      "click_point": { "x": 140, "y": 215 },
      "confidence": 0.95,
      "state": "enabled|disabled|focused|checked|unchecked|empty|filled",
      "description": "这是一个用于提交的蓝色主按钮"
    }
  ],
  "interactive_summary": "该界面包含一个文本输入框、两个按钮（确定/取消）和一个下拉选择框"
}

重要规则：
1. bbox 坐标必须是像素坐标，基于图片的实际分辨率
2. click_point 应该是元素的可点击中心点
3. 只返回你能明确看到的元素，不要猜测
4. 对于图标按钮，用功能描述作为 label（如 "关闭", "最小化"）
5. confidence 反映你对该元素识别的确信度

当前任务: {task_description}
```

**返回模型**:

```csharp
public class LlmGroundingResult
{
    public string ScreenType { get; set; }
    public string WindowTitleEstimate { get; set; }
    public List<GroundedElement> Elements { get; set; }
    public string InteractiveSummary { get; set; }
    public double LatencyMs { get; set; }
    public string ModelUsed { get; set; }
}

public class GroundedElement
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string Label { get; set; }
    public BoundingBox BBox { get; set; }
    public (int X, int Y) ClickPoint { get; set; }
    public double Confidence { get; set; }
    public string State { get; set; }
    public string Description { get; set; }
    public string Source => "llm_vision"; // 标记来源
}
```

### 3.2 LLM 客户端抽象

**目标**: 支持多种多模态 LLM 后端，不锁死供应商。

```csharp
// 新增接口: PeekabooWin.Core.Agent.ILlmVisionClient
public interface ILlmVisionClient
{
    Task<string> ChatWithImageAsync(
        string systemPrompt, 
        string userPrompt, 
        byte[] imageBytes, 
        string mediaType = "image/png",
        CancellationToken ct = default);
}

// 实现: OpenAI 兼容接口 (GPT-4o, Qwen-VL, etc.)
public class OpenAiVisionClient : ILlmVisionClient
{
    // 使用 OpenAI 兼容的 chat/completions API
    // 支持 base_url 配置，可以对接任何 OpenAI 兼容服务
    public OpenAiVisionClient(string baseUrl, string apiKey, string model);
}

// 实现: 本地 fallback（不依赖外部 API）
public class LocalVisionFallback : ILlmVisionClient
{
    // 用 Windows OCR + 简单规则生成结构化输出
    // 质量不如 LLM，但离线可用
    public Task<string> ChatWithImageAsync(...);
}
```

**配置方式**:
```json
// appsettings.json
{
  "LlmVision": {
    "Provider": "openai_compatible",
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "${VISION_API_KEY}",
    "Model": "gpt-4o",
    "MaxTokens": 4096,
    "Temperature": 0.1,
    "TimeoutMs": 10000,
    "FallbackToLocal": true
  }
}
```

### 3.3 Prompt 优化策略

**Token 节约**:
- 截图降采样到 1920px 宽度（大多数 LLM 的视觉编码器在这个分辨率已经足够）
- JPEG 压缩 quality=75（LLM 不需要无损图片）
- 如果目标窗口已知，只截窗口区域而不是全屏
- 首次全场景理解后，缓存 screen_type，后续只做增量元素定位

**Prompt 分层**:
- **快速定位 prompt**: 只问"XX 按钮在哪"，要求返回单个元素的坐标。Token 少、响应快。
- **全场景理解 prompt**: 要求列出所有可交互元素。Token 多但信息全，结果缓存复用。
- **验证 prompt**: 执行后截屏，问"之前的 XX 操作成功了吗"。

**缓存策略**:
```csharp
public class PerceptionCache
{
    // 基于截图 hash + 任务描述的缓存 key
    // TTL = 5秒（UI 状态可能很快变化）
    // 同一次 agent 执行中，相同截图不重复发 LLM
    
    public LlmGroundingResult? Get(string screenshotHash, string taskDescription);
    public void Set(string screenshotHash, string taskDescription, LlmGroundingResult result);
    public void Invalidate(); // 执行动作后立即失效
}
```

---

## Phase 4: 感知路由器 (PerceptionRouter)

### 4.1 设计

**目标**: 统一协调 UIA 和 LLM 两条感知路径，对上层暴露单一接口。

```csharp
// 新增: PeekabooWin.Core.Perception.PerceptionRouter
public class PerceptionRouter
{
    private readonly UIAutomationService _uia;
    private readonly LlmGroundingService _llmGrounding;
    private readonly PerceptionCache _cache;
    private readonly PekaLogger _logger;
    
    // 核心方法: 定位一个元素
    public async Task<PerceptionResult> GroundElement(
        string windowKeyword,
        string elementDescription,
        CancellationToken ct)
    {
        // Phase 1: UIA 快速通道 (<50ms)
        var uiaResult = TryUiaFirst(windowKeyword, elementDescription);
        if (uiaResult.IsConfident) return uiaResult;
        
        // Phase 2: LLM 视觉 grounding (1-3s)
        var llmResult = await TryLlmGrounding(windowKeyword, elementDescription, ct);
        if (llmResult.IsConfident) return llmResult;
        
        // Phase 3: OCR 兜底 (legacy，逐步淘汰)
        return TryOcrFallback(windowKeyword, elementDescription);
    }
    
    // 全场景理解
    public async Task<ScreenUnderstanding> UnderstandScreen(
        string windowKeyword,
        CancellationToken ct);
}

public class PerceptionResult
{
    public GroundedElement? Element { get; set; }
    public PerceptionSource Source { get; set; } // UIA / LLM / OCR / NotFound
    public double Confidence { get; set; }
    public double LatencyMs { get; set; }
    public string? FallbackReason { get; set; } // 如果走了 fallback，记录原因
}

public enum PerceptionSource
{
    UIA,           // UIA 直接命中
    UIA_Fuzzy,     // UIA 模糊匹配
    LLM_Vision,    // LLM 视觉 grounding
    OCR_Fallback,  // 传统 OCR 兜底
    NotFound       // 什么都没找到
}
```

### 4.2 路由策略

```
请求: "点击确定按钮"
│
├─ UIA FindByName("确定")
│   ├─ 找到且唯一 → 返回 (source=UIA, confidence=0.95)
│   ├─ 找到多个 → 用窗口上下文选最佳 → 返回 (source=UIA_Fuzzy, confidence=0.8)
│   └─ 没找到 → 继续
│
├─ UIA FindBySemantic("确定按钮")
│   ├─ 扩展关键词: "确定", "OK", "确认", "Confirm"
│   ├─ 匹配到 → 返回 (source=UIA_Fuzzy, confidence=0.7)
│   └─ 没匹配 → 继续
│
├─ LLM GroundElement("确定按钮")
│   ├─ 截屏 → 发给 LLM → 解析返回
│   ├─ LLM 返回坐标 → 返回 (source=LLM_Vision, confidence=LLM.confidence)
│   └─ LLM 超时/失败 → 继续
│
└─ OCR FindWord("确定")
    ├─ 找到 → 计算中心点 → 返回 (source=OCR_Fallback, confidence=0.5)
    └─ 没找到 → 返回 NotFound
```

### 4.3 统一元素模型

```csharp
// 新增: 替代当前 UiElement 作为主元素模型
public class GroundedElement
{
    public string Id { get; set; }
    public string Type { get; set; }           // button, textbox, checkbox...
    public string Label { get; set; }           // 元素文字/功能描述
    public string? AutomationId { get; set; }   // UIA 的 AutomationId
    
    // 位置和点击
    public BoundingBox BBox { get; set; }
    public (int X, int Y) ClickPoint { get; set; }  // 推荐点击坐标
    
    // 来源和质量
    public PerceptionSource Source { get; set; }
    public double Confidence { get; set; }       // 0.0 ~ 1.0
    
    // UIA 专属字段（Source=UIA 时有值）
    public AutomationElement? RawUiaElement { get; set; }
    public string[]? SupportedPatterns { get; set; }
    
    // LLM 专属字段（Source=LLM_Vision 时有值）
    public string? LlmDescription { get; set; }
    public string? LlmState { get; set; }
    
    // 执行策略建议
    public ClickStrategy PreferredClickStrategy => 
        RawUiaElement != null && SupportedPatterns?.Contains("Invoke") == true
            ? ClickStrategy.UIA_Invoke
            : ClickStrategy.CoordinateClick;
}

public enum ClickStrategy
{
    UIA_Invoke,        // 最可靠：直接调用 InvokePattern
    UIA_Value,         // 次可靠：通过 ValuePattern 设值
    CoordinateClick,   // 退化：SetCursorPos + SendInput
    NoAction           // 无法操作
}
```

---

## Phase 5: 执行层改造

### 5.1 InputService DPI 修复

```csharp
public class InputService
{
    private readonly DpiContext _dpi;
    
    public InputService(DpiContext dpi) { _dpi = dpi; }
    
    public CommandResult Click(int x, int y)
    {
        // 新增: DPI 缩放转换
        var scale = _dpi.GetPrimaryScale();
        var (px, py) = _dpi.LogicalToPhysical(x, y, scale);
        
        // 新增: 屏幕边界校验
        var (screenW, screenH) = GetScreenBounds();
        if (px < 0 || py < 0 || px >= screenW || py >= screenH)
            return CommandResult.Fail("click", $"Coordinates ({px},{py}) out of screen bounds");
        
        SetCursorPos(px, py);
        Thread.Sleep(50);
        // ... 现有 SendInput 逻辑
    }
    
    // 新增: 点击后验证光标位置
    public bool VerifyClickPosition(int expectedX, int expectedY, int tolerance = 5)
    {
        var (cx, cy) = GetCursorPos();
        return Math.Abs(cx - expectedX) <= tolerance && Math.Abs(cy - expectedY) <= tolerance;
    }
}
```

### 5.2 ActionExecutor 改造

**核心改动**: 所有涉及元素定位的动作，统一走 `PerceptionRouter`。

```csharp
public class ActionExecutor
{
    private readonly PerceptionRouter _perception; // 新增
    private readonly InputService _input;
    private readonly UIAutomationService _uia;
    
    // click-element 的新实现
    public async Task<(bool, string)> ExecuteClickElement(string window, string name, CancellationToken ct)
    {
        var result = await _perception.GroundElement(window, name, ct);
        
        if (result.Element == null)
            return (false, $"Element '{name}' not found via any perception channel");
        
        return result.Element.PreferredClickStrategy switch
        {
            ClickStrategy.UIA_Invoke => ExecuteUiaInvoke(result.Element),
            ClickStrategy.UIA_Value => ExecuteUiaValue(result.Element),
            ClickStrategy.CoordinateClick => ExecuteCoordinateClick(result.Element),
            _ => (false, "No viable click strategy")
        };
    }
    
    // ocr-click 重命名为 vision-click，走 LLM 视觉
    public async Task<(bool, string)> ExecuteVisionClick(string description, string window, CancellationToken ct)
    {
        var result = await _perception.GroundElement(window, description, ct);
        if (result.Element == null)
            return (false, $"Element matching '{description}' not found");
        
        return ExecuteCoordinateClick(result.Element);
    }
}
```

---

## Phase 6: 验证层改造 (LLM 视觉验证)

### 6.1 LlmVerificationService

**目标**: 用多模态 LLM 做执行后的视觉验证，替代当前粗糙的 OCR 文本对比。

```csharp
// 新增: PeekabooWin.Core.Verification.LlmVerificationService
public class LlmVerificationService
{
    private readonly ILlmVisionClient _visionClient;
    private readonly CaptureService _captureService;
    
    public async Task<VerificationResult> VerifyActionAsync(
        VerificationRequest request,
        CancellationToken ct)
    {
        // 截取执行后的屏幕
        var afterScreenshot = _captureService.CaptureScreenAsBase64();
        
        // 构建验证 prompt
        var prompt = BuildVerificationPrompt(request);
        
        // 让 LLM 判断
        var response = await _visionClient.ChatWithImageAsync(
            systemPrompt: VERIFICATION_SYSTEM_PROMPT,
            userPrompt: prompt,
            imageBytes: afterScreenshot,
            ct: ct);
        
        return ParseVerificationResponse(response);
    }
}
```

**验证 Prompt 模板**:

```
你是一个 Windows 桌面操作验证助手。我刚刚执行了以下操作：

操作类型: {action_type}
目标: {target_description}
预期效果: {expected_outcome}

请看当前屏幕截图，判断操作是否成功。

返回 JSON：
{
  "success": true/false,
  "confidence": 0.0-1.0,
  "reason": "判断依据",
  "observed_state": "你观察到的当前屏幕状态描述",
  "suggestion": "如果失败，建议的下一步操作"
}

验证规则：
1. 对于 click 操作：检查目标元素是否被激活/选中/展开
2. 对于 type 操作：检查输入框中是否出现了预期文字
3. 对于 hotkey 操作：检查是否触发了预期效果（如 ctrl+s 弹出保存对话框）
4. 如果出现错误弹窗或异常状态，判定为失败
```

### 6.2 验证策略分层

不是每个操作都需要 LLM 验证（太慢、太贵），采用分级策略：

| 操作类型 | 验证方式 | 说明 |
|---------|---------|------|
| `focus-window` | 本地验证 | `GetForegroundWindow()` 检查 |
| `type` | 轻量验证 | 检查 UIA ValuePattern 的值是否变化 |
| `click` (UIA Invoke) | 轻量验证 | UIA Invoke 成功即认为成功 |
| `click` (坐标点击) | LLM 验证 | 截屏发给 LLM 判断 |
| `hotkey` | LLM 验证 | 效果多样，需要视觉判断 |
| `screenshot` | 无需验证 | — |

---

## Phase 7: Agent 编排层适配

### 7.1 AgentOrchestrator 简化

当前 `AgentOrchestrator` 有 14 个依赖注入，编排逻辑过于密集。改造方向：

```csharp
public class AgentOrchestrator
{
    // 依赖简化: 感知相关的 5 个依赖合并为 PerceptionRouter
    private readonly TaskParser _taskParser;
    private readonly ActionExecutor _actionExecutor;       // 内部持有 PerceptionRouter
    private readonly PerceptionRouter _perception;         // 统一感知入口
    private readonly LlmVerificationService _verifier;     // 统一验证入口
    private readonly VacpSkillIntegration _skillIntegration;
    private readonly ActionRiskGate _riskGate;
    private readonly RecoveryPlanner _recoveryPlanner;
    private readonly SkillTransferController _skillTransferController;
    private readonly PekaLogger _logger;
    // 9 个依赖 → 结构更清晰
}
```

### 7.2 执行循环改造

```
每个 step 的新执行流程:

1. 风险门控 (不变)
2. 技能迁移检查 (不变)
3. 元素定位:
   - 旧: UIA FindByName → ElementCandidateRanker → 取 BBox 中心
   - 新: PerceptionRouter.GroundElement() → 返回 GroundedElement + 建议的 ClickStrategy
4. 前置状态捕获:
   - 旧: 全屏截图 + OCR
   - 新: 如果需要 LLM 验证才截图，否则跳过（省一次截屏+OCR）
5. 动作执行:
   - 旧: InputService.Click(x, y)
   - 新: 按 ClickStrategy 执行（UIA Invoke / 坐标点击）
6. 验证:
   - 旧: ActionVerifier 做 OCR 文本对比
   - 新: 按操作类型分级验证（见 6.2）
7. 失败恢复 (基本不变，但 RecoveryPlanner 也用 PerceptionRouter)
```

---

## 实施优先级与排期建议

### P0 — 立即执行 (1-2天)

| 任务 | 预期收益 | 工作量 |
|------|---------|--------|
| InputService DPI 修复 | 消除 DPI 缩放导致的点击偏移 | 半天 |
| UIA InvokeElement 修复 DI | 消除 `new InputService()` 反模式 | 1小时 |
| UIA click-element 走 InvokePattern | 最直接的准确率提升 | 半天 |

### P1 — 核心改造 (3-5天)

| 任务 | 预期收益 | 工作量 |
|------|---------|--------|
| GroundedElement 统一模型 | 为后续改造奠基 | 半天 |
| PerceptionRouter 框架 | 统一感知入口 | 1天 |
| LlmGroundingService 实现 | 核心新能力 | 1.5天 |
| OpenAiVisionClient 实现 | LLM 接入 | 1天 |
| PerceptionCache | 减少重复 LLM 调用 | 半天 |

### P2 — 验证层 + 编排层 (2-3天)

| 任务 | 预期收益 | 工作量 |
|------|---------|--------|
| LlmVerificationService | 验证准确率提升 | 1天 |
| ActionExecutor 适配 PerceptionRouter | 全链路贯通 | 1天 |
| AgentOrchestrator 简化 | 代码可维护性 | 1天 |

### P3 — 优化打磨 (2-3天)

| 任务 | 预期收益 | 工作量 |
|------|---------|--------|
| Prompt 工程优化 | LLM grounding 准确率 | 持续 |
| UIA 自适应深度遍历 | UIA 覆盖率提升 | 1天 |
| CaptureService DXGI 可选 | 兼容更多场景 | 1天 |
| 性能基准测试 | 量化改进效果 | 1天 |

---

## 预期效果

### 准确率提升

| 场景 | 当前预估 | 改造后预估 | 提升原因 |
|------|---------|----------|---------|
| 原生 Win32 应用点击 | ~60% | ~95% | UIA Invoke 直接操作，无需坐标 |
| WPF/UWP 应用点击 | ~50% | ~90% | UIA 支持好 + DPI 修复 |
| 浏览器内操作 | ~30% | ~75% | LLM 视觉理解网页布局 |
| Electron 应用 | ~20% | ~70% | LLM 视觉 grounding 补 UIA 缺失 |
| 自绘/游戏界面 | ~10% | ~60% | LLM 视觉理解 + 坐标点击 |
| 中文 UI 元素定位 | ~40% | ~85% | LLM 理解中文语义，不依赖 OCR |

### 性能影响

| 指标 | 当前 | 改造后 | 说明 |
|------|------|--------|------|
| UIA 命中时延迟 | ~50ms | ~50ms | 无变化 |
| 需要 LLM 时延迟 | N/A | 1-3s | LLM 往返时间 |
| 验证延迟 | ~200ms (OCR) | ~100ms (轻量) / 2s (LLM) | 分级验证 |
| Token 消耗 | 0 | ~1000-3000/次 | 取决于截图大小 |

### 兼容性

- 现有 CLI 命令全部保持兼容
- MCP Server 的 15 个 Tool 接口不变
- 内部实现替换对上层透明

---

## 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| LLM API 不可用 | 感知退化到 OCR 水平 | LocalVisionFallback 兜底 + PerceptionCache |
| LLM 返回错误坐标 | 点击打偏 | UIA 优先 + 置信度阈值 + 重试 |
| LLM 延迟高 | Agent 执行变慢 | 缓存 + UIA 快速通道 + 异步超时 |
| Token 成本 | 大量调用时费用高 | 截图降采样 + 局部截取 + 缓存 |
| LLM 返回格式不稳定 | 解析失败 | JSON Schema 约束 + 重试 + fallback |

---

## 配置示例

```json
{
  "Perception": {
    "PreferredChannel": "uia_first",
    "LlmFallback": {
      "Enabled": true,
      "Provider": "openai_compatible",
      "BaseUrl": "https://api.openai.com/v1",
      "Model": "gpt-4o",
      "ApiKey": "${VISION_API_KEY}",
      "TimeoutMs": 10000,
      "MaxRetries": 2,
      "ConfidenceThreshold": 0.7
    },
    "Cache": {
      "Enabled": true,
      "TtlSeconds": 5,
      "MaxEntries": 20
    },
    "Verification": {
      "UseLlmForCoordinateClicks": true,
      "UseLlmForHotkeys": true,
      "SkipVerificationFor": ["screenshot", "list-windows", "is-focused"]
    }
  },
  "Dpi": {
    "AutoDetect": true,
    "OverrideScale": null
  }
}
```
