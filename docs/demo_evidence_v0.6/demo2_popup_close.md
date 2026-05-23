# Demo 2: Popup Close — V0.6 Complex UI Evidence

**Task ID:** `demo2_popup_close`  
**验证点:** GPT Vision 识别非标准 UI 元素 + Stable Click  
**目标:** 证明 VACP 能处理复杂的非标准 UI（弹窗、遮罩层、自定义按钮）

---

## Task Description

> 记事本打开了一个"查找"弹窗，需关闭该弹窗以继续操作。

---

## Screen State Graph (from GPT Vision)

```
Screen Type: dialog
Window Title: "查找"
Elements:
  e1: type=button, label="取消", x=580, y=320, state=enabled
  e2: type=button, label="确定", x=490, y=320, state=enabled
  e3: type=input, label="查找内容", x=380, y=260, state=empty
  e4: type=text, label="查找标题", x=380, y=230
  e5: type=button, label="关闭", x=680, y=210 (X close button on title bar)
```

**Relations:**
```
e5(关闭按钮) ──position─> title_bar_right_corner
e2(确定按钮) ──right_of─> e1(取消按钮)
e3(查找输入框) ──above─> e1(取消按钮)
```

---

## Grounding Evaluation for "关闭" Button

```json
{
  "element": "e5",
  "label": "关闭",
  "type": "button",
  "vision_confidence": 0.82,
  "grounding_query": "关闭"
}
```

**Breakdown:**
| Factor | Score | Weight | Contribution |
|--------|-------|--------|-------------|
| VisionConfidence | 0.82 | 0.4 | 0.328 |
| TextMatch | 1.00 ("关闭" exact) | 0.2 | 0.200 |
| PositionPrior | 0.75 (top-right expected) | 0.2 | 0.150 |
| TypeMatch | 1.00 (button==button) | 0.2 | 0.200 |
| **Final Score** | | | **0.878** |

**Decision: PASS (0.878 ≥ 0.75)**

---

## Risk Gate

```
OperationRisk: click → 0.20
PageRisk: dialog → 0.60 (dialogs are medium-risk — easy to dismiss wrongly)
Irreversibility: click close button → 0.0 (dialogs can be reopened)
DataSensitivity: none → 0.0
Uncertainty: 0.0 (grounding=0.878)

Total Risk: 0.30×0.2 + 0.25×0.6 + 0.20×0.0 + 0.15×0.0 + 0.10×0.0 = 0.21
Decision: ALLOW (0.21 < 0.3)
```

---

## Execution & Verification

- **Execute:** Click at (680, 210)
- **Before screenshot:** dialog visible
- **After screenshot:** dialog dismissed, main window focused
- **VisualChange:** 0.72 (dialog area pixels changed significantly)
- **VerificationScore:** 0.4×0.72 + 0.3×0.9 + 0.2×0.8 + 0.1×1.0 = **0.808**
- **Outcome: SUCCESS**

---

## Pass Criteria

| Criterion | Expected | Actual | Status |
|-----------|----------|--------|--------|
| Grounding score ≥ 0.75 | Yes | 0.878 | ✅ PASS |
| Dialog detected as screen_type | Yes | dialog | ✅ PASS |
| Risk gate → ALLOW | Yes | 0.21 < 0.3 | ✅ PASS |
| Verification score ≥ 0.6 | Yes | 0.808 | ✅ PASS |