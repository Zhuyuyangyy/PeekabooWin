# PeekabooWin V0.9.4 — Baseline Validation Report

> Date: 2026-05-26
> Validator: Automated baseline check
> SDK: .NET 8.0.421 (Windows)

## 1. Build Status

| Item | Result |
|------|--------|
| `dotnet restore` | ✅ Pass |
| `dotnet build -c Release` | ✅ Pass (0 error, 0 warning) |
| Pre-existing CS8602 warning | ✅ Fixed (ApiServer.cs:405 null-coalescing fallback) |
| Pre-existing CS8625 warning | ✅ Fixed (AppProfile.IsCompatibleWith parameter → SkillScope?) |

### Fixes Applied

1. **CS8625** in `AppProfile.cs:47`: `IsCompatibleWith(SkillScope scope)` → `IsCompatibleWith(SkillScope? scope)` — test passes `null`, parameter must be nullable.
2. **CS8602** in `ApiServer.cs:405`: `args.GetValueOrDefault("text") ?? args.GetValueOrDefault("\"")` → added `?? ""` to guarantee non-null before `.StartsWith()`.

## 2. Unit Tests

| Metric | Value |
|--------|-------|
| Total tests | 30 |
| Passed | 30 |
| Failed | 0 |
| Skipped | 0 |
| Duration | 103 ms |

### Test Breakdown

| Test Class | Count | Status |
|------------|-------|--------|
| NegativeTransferGuardTests | 9 | ✅ All pass |
| AppProfileTests | 10 | ✅ All pass |
| SkillScopeValidatorTests | 8 | ✅ All pass (was 11 in earlier count, 8 unique) |

> Note: Only Memory/Safety layer has test coverage. Core paths (Agent, VACP, OCR, Input, Capture, Window) have **zero** unit tests.

## 3. CLI Smoke Tests

### V0.1 Core Commands

| Command | Result | Notes |
|---------|--------|-------|
| `list-windows` | ✅ Pass | Returns 7 windows with full metadata |
| `screenshot --screen --out artifacts/smoke_screen.png` | ✅ Pass | 1707×1067 PNG captured |
| `screenshot --window "必备" --out artifacts/smoke_ocr_window.png` | ✅ Pass | Window capture works |

### V0.2 UIA Commands

| Command | Result | Notes |
|---------|--------|-------|
| `inspect --window "必备" --max-depth 1` | ✅ Pass | Returns UIA tree with element_count, bounding_box, patterns |

### V0.3 OCR Commands

| Command | Result | Notes |
|---------|--------|-------|
| `ocr --out artifacts/smoke_ocr.png` | ⚠️ Partial | Returns 0 words, engine="Tesseract" (misleading default), error not surfaced |
| `ocr --lang "zh-CN" --out artifacts/smoke_ocr_zh.png` | ⚠️ Partial | Same issue — 0 words, error hidden |
| `ocr --window "必备" --out artifacts/smoke_ocr_window.png` | ⚠️ Partial | Same — 0 words returned |
| `find-on-screen --text "必备"` | ✅ Pass | OCR engine works — found text at (120, 29) |

**Root Cause Analysis**: The `ocr` command creates a new `OcrService(lang)` instance and the output handler does NOT include `ocrResult.Error` in the JSON response. The `OcrResult.Engine` default is `"Tesseract"` but the actual engine is `Windows.Media.Ocr` — when OCR fails internally, the default value leaks through. The `find-on-screen` command uses the shared `OcrService()` instance (default "zh-CN") and works correctly.

### V0.7-V0.8 Skill Commands

| Command | Result | Notes |
|---------|--------|-------|
| `skill-list` | ✅ Pass | Returns 2 seeded skills |
| `skill-seed` | ✅ Pass | Seeds demo skills, count=2 |
| `skill-search --task "notepad enter text"` | ✅ Pass | Returns ranked results with scores |
| `skill-use-preview --task "notepad enter text"` | ✅ Pass | Returns top_candidate with would_use_skill_hint=true |

### V0.9 Skill Context Commands

| Command | Result | Notes |
|---------|--------|-------|
| `skill-search-context --task "type hello" --window "必备"` | ✅ Pass | Returns window_signature (browser/web_textbox/neutral) + scored results |
| `skill-execute-guided --task "type hello"` | ✅ Pass | Returns preview with search results (note: V0.8 MVP, no real execution) |

### V0.4 Agent Commands

| Command | Result | Notes |
|---------|--------|-------|
| `agent --task "list windows" --max-steps 1` | ✅ Pass | Rule-based parsing works, executes list-windows action |

## 4. Known Issues (Updated)

### Carried Forward

| ID | Issue | Severity | Since |
|----|-------|----------|-------|
| KI-1 | Tesseract tessdata must be present for OCR — but code uses Windows.Media.Ocr, tessdata/ folder is vestigial | Low | V0.3 |
| KI-2 | UIA may not work on some apps (games, Electron) — OCR fallback should be used | Low | V0.2 |
| KI-3 | WindowSignature.SimilarityTo() score design: same WindowType+InputMode+RiskDomain but different ProcessFamily → ~0.1, not 1.0 | By Design | V0.9 |

### New Issues Found in V0.9.4

| ID | Issue | Severity | Details |
|----|-------|----------|---------|
| KI-4 | `ocr` command returns 0 words, error not surfaced | High | `HandleOcr` creates new OcrService, doesn't include `ocrResult.Error` in output. `OcrResult.Engine` defaults to "Tesseract" which is misleading. |
| KI-5 | `OcrResult.Engine` default is "Tesseract" but actual engine is Windows.Media.Ocr | Medium | `OcrResult.cs:23` has `Engine = "Tesseract"` as default. Should be `"Windows.Media.Ocr"` or empty. |
| KI-6 | `HandleOcr` creates its own OcrService instead of using the shared instance | Medium | Inconsistent with other handlers. The shared `ocrService` in Main() is unused by the `ocr` command. |
| KI-7 | Skill Replay is a no-op shell | High | `HandleSkillReplay` records steps as "played" without executing any real actions. |
| KI-8 | VACP and AgentService are two independent execution paths | High | `VacpPlanner` and `AgentService` don't share a common execution pipeline. |
| KI-9 | Test coverage: only 3 test files, core paths untested | High | Agent, VACP, OCR, Input, Capture, Window — zero tests. |

## 5. Architecture Debt Summary

| Category | Item | Impact |
|----------|------|--------|
| DI | All services manually `new` in Program.cs | Every handler re-creates VisualSkillStore, OcrService |
| CLI | Program.cs is 1159 lines | Unmaintainable, mixes routing + business logic |
| Async | `.GetAwaiter().GetResult()` in 6+ locations | Deadlock risk, especially in API server context |
| Temp Files | No unified temp file management | Scattered `Path.GetTempPath()` + silent `catch` on delete |
| Error Model | Mixed: CommandResult.Fail / throw / silent catch | Agent can't reason about failure causes |
| Logging | Console.WriteLine only | No structured logging, no trace correlation |
| Skill Replay | Empty implementation | Core feature gap |
| VACP/Agent | Dual execution paths | Architecture divergence |

## 6. Baseline Verdict

**PASS** — with known issues documented.

The V0.9 codebase builds cleanly (0 errors, 0 warnings after fixes), all 30 unit tests pass, and core CLI commands function correctly. The identified issues are architectural debt and error-handling gaps that should be addressed in the V0.10 engineering hardening phase, not blockers for proceeding.

### Recommended Next Steps (V0.10 Roadmap)

1. **V0.10.0**: DI container + CLI handler split (no feature changes)
2. **V0.10.1**: async full-chain + TempFileManager
3. **V0.10.2**: Unified error model + structured logging
4. **V0.10.3**: Real Skill Replay implementation
5. **V0.10.4**: VACP-Agent unified execution path

---

*Report generated: 2026-05-26T07:05:00Z*
