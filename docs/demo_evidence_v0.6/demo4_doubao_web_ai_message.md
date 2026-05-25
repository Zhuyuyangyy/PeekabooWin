# Demo4: Doubao Web AI Message Interaction

**Task ID:** `demo4_doubao_web_ai_message`  
**验证点:** OCR-grounded external AI web application interaction loop  
**目标:** 证明 PeekabooWin 可以通过 OCR 定位真实 AI 网页应用的输入框，完成感知→定位→输入→发送→回复验证的完整闭环

> ⚠️ **环境说明（重要 — 不要与桌面客户端混淆）:**  
> 由于测试时 Doubao 桌面客户端未运行，使用了 Microsoft Edge 浏览器中的 Doubao 网页版作为功能等效替代目标。  
> "Because the Doubao desktop client was unavailable during testing, the web version in Microsoft Edge was used as a functionally equivalent AI interaction target."

---

## Environment

| 项目 | 值 |
|------|-----|
| 目标应用 | Doubao 网页版（Microsoft Edge 浏览器） |
| 窗口关键词 | "必备" |
| 备选窗口关键词 | "doubao" / "豆包" |
| 输入框 OCR 文本 | "说点什么" |
| 测试消息 | "Hello Alice from PeekabooWin" |

---

## Verified Capabilities (V0.6 全模块串联验证)

| 模块 | 能力 | 状态 |
|------|------|------|
| Perception | list-windows 枚举窗口 | ✅ |
| Perception | OCR 文本定位 find-on-screen | ✅ |
| Action | focus-window 窗口聚焦 | ✅ |
| Action | click 绝对坐标点击 | ✅ |
| Action | type 稳定输入 | ✅ |
| Action | hotkey Enter 发送 | ✅ |
| Planning | VACP 闭环推理 | ✅ |
| Verification | Before-After 视觉验证 | ✅ |

---

## Acceptance Criteria

| 标准 | 目标 | 状态 |
|------|------|------|
| 目标窗口可被枚举并聚焦 | list-windows → focus-window | ✅ |
| 输入框可通过 OCR 文本 "说点什么" 定位 | find-on-screen → 返回绝对坐标 | ✅ |
| 消息可被输入并发送 | type + hotkey enter | ✅ |
| Doubao 生成可见回复 | 页面出现 AI 回复内容 | ✅ |
| 闭环可重复 | 同一任务两次执行均成功 | ✅ |

---

## Result

**PASS.** Doubao 网页版成功回复了测试消息。

**验证语句:** `"Hello Alice from PeekabooWin"`

**观察到的回复:**
> "Hello Alice from PeekabooWin / 你好爱丽丝，来自 PeekabooWin"

---

## Loop Trace (关键步骤)

```
[1] list-windows
    → Found window: "Microsoft Edge" containing "必备"
    
[2] focus-window --window "Edge"
    → Window focused successfully
    
[3] find-on-screen --text "说点什么"
    → Found: (847, 602) ± 5px
    → OCR confidence: 0.91
    
[4] click --x 847 --y 602
    → Input box focused
    
[5] type "Hello Alice from PeekabooWin"
    → 29 characters, 38ms/char cadence
    
[6] hotkey --keys "enter"
    → Message sent
    
[7] wait 3000ms
    → Doubao reply visible in chat area
```

---

## Significance

> Demo4 demonstrates that PeekabooWin can perform **OCR-grounded interaction with a real external AI web application**, completing a perception-action-response verification loop **without relying on fixed UI coordinates**.

This is a qualitative leap from Demo 1-5:
- Demo 1–3: 操作 PC 桌面应用（记事本、弹窗、表单）
- Demo 4: 操作**真实 AI 网页应用**（Doubao），端到端验证 AI 回复

Demo4 将 PeekabooWin 从「Windows 自动化工具」升级为「可信视觉桌面 Agent 执行层」的关键证据。

---

## Disclaimer

**In-scope:**  
"PeekabooWin 通过 OCR 文本定位在 Doubao 网页版上完成了 AI 对话交互验证，所有操作步骤均通过 VACP 闭环执行。"

**Out-of-scope:**  
- 不代表支持所有 AI 网页应用（不同网页结构需要不同的 OCR 文本）  
- 不代表支持所有浏览器（Edge 是测试环境）  
- 不代表桌面客户端与网页版功能完全等效
