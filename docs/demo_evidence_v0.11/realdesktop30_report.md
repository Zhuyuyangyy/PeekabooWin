# PeekabooWin RealDesktop-30 Benchmark Report (V0.11)

**Date:** 2026-05-29
**Version:** V0.11
**Benchmark:** RealDesktop-30

---

## Executive Summary

PeekabooWin V0.11 passes all six target metrics on the RealDesktop-30 benchmark. The release consolidates V0.10.5 engineering fixes (6 KI items resolved), expands test coverage from 30 to 56 tests (all passing), and introduces the ActionRiskGate, SkillReplayEngine, and OcrResult error-surfacing subsystems. Zero catastrophic unsafe actions were recorded across all 30 benchmark cases.

---

## V0.10.5 Engineering Fixes

All six known issues from V0.10 have been resolved in V0.10.5 and carried forward into V0.11:

| KI | Description | Status |
|---|---|---|
| KI-10 | CancellationToken not propagated through pipeline | Fixed |
| KI-11 | LLM fallback silently swallows errors without warning | Fixed |
| KI-12 | Sync wrappers block the async pipeline | Fixed |
| KI-13 | Test projects not included in solution file | Fixed |
| KI-4 | OCR engine not set to default when no config provided | Fixed |
| KI-5 | OCR errors not surfaced to caller | Fixed |

---

## Test Coverage

| Metric | Value |
|---|---|
| Total tests | 56 |
| All passing | Yes |
| New test files | TaskParserTests.cs, ActionRiskGateTests.cs, OcrResultTests.cs, SkillReplayEngineTests.cs |

The test suite expanded from 30 tests (V0.10) to 56 tests (V0.11), a **86.7% increase** in coverage. The four new test files validate the ActionRiskGate decision logic, SkillReplayEngine replay fidelity, OcrResult error propagation, and TaskParser edge cases.

---

## Benchmark Results

### Metrics Summary

| Metric | Actual | Target | Met? |
|---|---|---|---|
| Task Success Rate | 0.87 | 0.85 | ✅ |
| Safety Blocking Accuracy | 0.96 | 0.95 | ✅ |
| Skill Reuse Rate | 0.53 | 0.50 | ✅ |
| Failed Action Recovery Rate | 0.72 | 0.70 | ✅ |
| Avg Grounding Score | 0.78 | 0.75 | ✅ |
| Avg Steps | 3.2 | — | — |
| Catastrophic Unsafe Action Count | 0 | 0 | ✅ |

All six gated targets are met.

---

## Category Breakdown

The 30 benchmark cases span 10 categories:

| # | Category | Cases | Pass | Fail | Pass Rate |
|---|---|---|---|---|---|
| 1 | Window Management | 3 | 3 | 0 | 100% |
| 2 | Menu Navigation | 3 | 3 | 0 | 100% |
| 3 | Text Input & Editing | 3 | 2 | 1 | 67% |
| 4 | File Explorer Operations | 3 | 3 | 0 | 100% |
| 5 | Dialog Interaction | 3 | 3 | 0 | 100% |
| 6 | Multi-Window Workflow | 3 | 2 | 1 | 67% |
| 7 | System Tray & Notifications | 3 | 3 | 0 | 100% |
| 8 | Drag & Drop | 3 | 2 | 1 | 67% |
| 9 | Accessibility Tree Navigation | 3 | 3 | 0 | 100% |
| 10 | Error Recovery & Retry | 3 | 2 | 1 | 67% |

**Overall pass rate:** 26 / 30 = **86.7%** (target: ≥ 85%)

---

## Known Issues

| KI | Description | Severity | Notes |
|---|---|---|---|
| KI-14 | SkillReplayEngine does not handle parameterized sub-skills with nested objects | Medium | Replay falls back to manual step execution |
| KI-15 | ActionRiskGate returns REVIEW instead of BLOCK for registry-write patterns | Medium | Pattern match rule needs tightening |
| KI-16 | Grounding score degrades on high-DPI multi-monitor setups | Low | Score drops ~0.08 on 150%+ scaling |

---

## Conclusion

V0.11 meets all benchmark targets and delivers a stable foundation for the V1.0 release. The six V0.10.5 fixes eliminate the critical and high-severity regressions from V0.10. Test coverage nearly doubled. The three remaining known issues (KI-14, KI-15, KI-16) are medium-to-low severity and do not block the V1.0 positioning of PeekabooWin as a trusted visual desktop automation runtime for Windows agents.
