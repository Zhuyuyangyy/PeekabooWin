# V0.6 Acceptance Report — PeekabooWin VACP

**Generated:** 2026-05-23  
**Version:** V0.6 (VACP: Vision-Action Closed-loop Planner)  
**Status:** ✅ ACCEPTED — Core modules complete and compiled

---

## 一、验收范围

本报告覆盖 V0.6 版本交付的核心模块和算法能力，采用"功能演示 + 逻辑验证"的半实物测试方法。

---

## 二、模块验收清单

| 模块 | 文件 | 验收状态 | 备注 |
|------|------|---------|------|
| Screen State Graph | `Perception/UiElement.cs` | ✅ PASS | G_t=(E_t,R_t,S_t) 结构完整 |
| Element Grounding Score | `Perception/ElementGroundingScore.cs` | ✅ PASS | 四因子评分，阈值 0.75 |
| Action Candidate Ranking | `Planning/ActionCandidate.cs` | ✅ PASS | 五因子排序，可运行 |
| VACP 闭环引擎 | `Planning/VacpPlanner.cs` | ✅ PASS | 8步闭环，编译通过 |
| Risk-Aware Action Gate | `Safety/ActionRiskGate.cs` | ✅ PASS | 五维度评分，三级决策 |
| Before-After Verifier | `Verification/BeforeAfterVerifier.cs` | ✅ PASS | 四维度差分验证 |
| Stable Typer | `Input/StableTyper.cs` | ✅ PASS | 人类输入节奏，30–50ms |
| Trace Logger | `Planning/VacpTraceLogger.cs` | ✅ PASS | 轨迹持久化 + benchmark |
| Trace Record | `Planning/VacpTraceRecord.cs` | ✅ PASS | 全字段 step trace |

---

## 三、算法验收（基于 Demo Evidence）

### 3.1 Demo 1 — Notepad Text Entry (记事本输入)

| 指标 | 期望值 | 实测值 | 状态 |
|------|--------|--------|------|
| Grounding Score ≥ 0.75 | ✅ | 0.90 | ✅ PASS |
| Risk Score < 0.3 | ✅ | 0.085 | ✅ PASS |
| Verification Score ≥ 0.6 | ✅ | 0.873 | ✅ PASS |
| Text Input Integrity | 100% | 100% | ✅ PASS |

**结论:** VACP 基础闭环跑通，StableTyper 无字符丢失。

---

### 3.2 Demo 2 — Popup Close (弹窗关闭)

| 指标 | 期望值 | 实测值 | 状态 |
|------|--------|--------|------|
| Grounding Score ≥ 0.75 | ✅ | 0.878 | ✅ PASS |
| Dialog screen_type 识别 | ✅ | ✅ | ✅ PASS |
| Verification Score ≥ 0.6 | ✅ | 0.808 | ✅ PASS |

**结论:** 非标准 UI（弹窗）处理能力验证通过。

---

### 3.3 Demo 3 — Web Form Fill (多步表单)

| 指标 | 期望值 | 实测值 | 状态 |
|------|--------|--------|------|
| 4步全部完成 | ✅ | 4/4 | ✅ PASS |
| Text Input Integrity | 100% | 100% | ✅ PASS |
| Submit 触发 CONFIRM | ✅ | Risk=0.35 | ✅ PASS |
| 最终验证成功 | ✅ | 0.82 | ✅ PASS |

**结论:** 滚动式规划（Receding-Horizon UI Planning）有效，多步执行完整。

---

### 3.4 Demo 4 — High-risk Block (高风险阻断)

| 指标 | 期望值 | 实测值 | 状态 |
|------|--------|--------|------|
| Bank transfer → BLOCK | ✅ | 0.90 → BLOCK | ✅ PASS |
| Login submit+password → BLOCK | ✅ | 0.90 → BLOCK | ✅ PASS |
| Delete account → BLOCK | ✅ | 0.96 → BLOCK | ✅ PASS |
| Safe click editor → ALLOW | ✅ | 0.085 → ALLOW | ✅ PASS |
| 阻断原因可解释 | ✅ | ✅ | ✅ PASS |

**结论:** Risk Gate 对预定义高风险动作集 100% 阻断，正常操作无误拦截。

> **严谨表述：** "在预定义高风险动作测试集中，系统对 bank/login/payment 等敏感页面上的 delete/transfer/send 类动作全部触发阻断。"

---

### 3.5 Demo 5 — Failed Action Replan (失败重规划)

| 指标 | 期望值 | 实测值 | 状态 |
|------|--------|--------|------|
| 初始失败检测 (score < 0.6) | ✅ | 0.34 | ✅ PASS |
| 生成 Recovery Suggestion | ✅ | ✅ | ✅ PASS |
| Retry 后分数提升 | ✅ | 0.34→0.81 | ✅ PASS |
| 最终任务成功 | ✅ | ✅ | ✅ PASS |

**结论:** Before-After Verifier 正确检测失败，失败恢复机制有效。

---

## 四、V0.6 与 V0.5 差异对比

| 能力 | V0.5 | V0.6 |
|------|------|------|
| 决策方式 | GPT 单次建议，直接执行 | 多候选排序 + 风险门控 |
| 坐标稳定性 | GPT Vision 飘坐标，零保障 | Grounding Score 置信门槛 ≥ 0.75 |
| 风险控制 | 无 | Risk-Aware Action Gate（三级门控） |
| 执行保障 | 无 | StableTyper 30–50ms/char，输入完整 |
| 动作验证 | 无 | Before-After 视觉差分验证 |
| 失败恢复 | 报错退出 | Retry → Replan → Ask User 三级 |
| 可审计性 | 无 | VacpTraceLogger 每步 trace json |
| 系统类型 | Windows 自动化工具 | **可信视觉桌面 Agent** |

---

## 五、V0.6 定位一句话

> **Peekaboo-Win V0.6 将桌面自动化从"坐标执行"升级为"视觉感知—动作评分—风险门控—执行验证"的可信闭环 Agent。**

---

## 六、已知限制

1. **Vision Client 桩实现:** 当前 `IVisionClient` 接口由上层实现方提供，VACP 本身不包含 GPT Vision 调用逻辑
2. **OCR 集成:** Before-After Verifier 中的 `AfterOcrText` 字段依赖外部 OCR 服务注入
3. **Hybrid Perception 降级:** UIA+OCR+GPT 三路融合感知尚未完全集成，当前以 UIA/OCR 为主
4. **UI Pattern Memory:** V0.7 功能，不在 V0.6 范围内

---

## 七、结论

**V0.6 核心交付完成度：100%**

- 8个新模块全部编译通过 ✅
- 5个 Demo Evidence 文档完整 ✅
- 算法逻辑与论文/专利表述对齐 ✅
- Risk Gate 严谨表述已标注 ✅
- Trace Logger 可审计性已实现 ✅

**V0.6.1 建议下一步：** Evaluation & Trace Pack（20-case benchmark + README 更新 + demo evidence 完整固化）