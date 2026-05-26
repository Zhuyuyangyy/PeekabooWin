# Known Issues

## V0.10 (Engineering Hardening)

- **KI-10: AgentOrchestrator.RunAsync does not yet propagate cancellation tokens**: The orchestrator entry point accepts a `CancellationToken` parameter but does not thread it through to child `AgentService` or `VacpPlanner` calls.
- **KI-11: TaskParser LLM fallback requires MINIMAX_API_KEY, no graceful degradation message when key is missing**: When the key is absent, the parser falls back silently to regex-only parsing with no log or user-facing warning.
- **KI-12: ActionExecutor still has sync wrappers for backward compatibility (4 remaining GetAwaiter().GetResult() calls)**: These can deadlock on UI-affiliated synchronization contexts and should be replaced with async-only APIs in a future breaking release.
- **KI-13: Test project not included in PeekabooWin.sln — must run `dotnet test` against the csproj directly**: The test `.csproj` exists but is not added to the solution file, so `dotnet test` from the sln root skips it.

## V0.9.4 (Baseline Validation)

- **KI-4: `ocr` command returns 0 words, error not surfaced**: `HandleOcr` creates a new `OcrService(lang)` and the output JSON does not include `ocrResult.Error`. When OCR fails internally, the error is silently swallowed. Meanwhile `find-on-screen` (which uses the shared OcrService) works correctly.
- **KI-5: `OcrResult.Engine` default is "Tesseract"**: `OcrResult.cs` has `Engine = "Tesseract"` as default, but the actual engine is `Windows.Media.Ocr`. When OCR fails before setting Engine, the misleading default leaks through.
- **~~KI-6: `HandleOcr` creates its own OcrService~~**: Fixed in V0.10 — `HandleOcr` now uses shared `OcrService` via DI in `OcrCommandHandler`.
- **~~KI-7: Skill Replay is a no-op shell~~**: Fixed in V0.10 — `SkillReplayEngine` now performs real execution with risk gate.
- **~~KI-8: VACP and AgentService are two independent execution paths~~**: Fixed in V0.10 — `AgentOrchestrator` unifies VACP + AgentService into a single pipeline.
- **KI-9: Test coverage — only 3 test files**: Agent, VACP, OCR, Input, Capture, Window have zero unit tests.

## V0.9
- **WindowSignature.SimilarityTo() score**: When WindowType+InputMode+RiskDomain are identical but ProcessFamily differs, the score is ~0.1 (not 1.0). The weighted formula (0.4+0.3+0.2)/9 = 0.1 for same family is the design intent. Full family match adds +0.1.

## V0.7
- **~~2 compiler warnings: CS8602~~**: Fixed in V0.9.4 — ApiServer.cs null-coalescing fallback added; AppProfile.IsCompatibleWith parameter made nullable.

## V0.8
- **~~1 compiler warning: CS8602 in ApiServer.cs~~**: Fixed in V0.9.4.

## All Versions
- Tesseract tessdata (chi_sim+eng) must be present in tessdata/ for OCR to function — however, current code uses Windows.Media.Ocr, tessdata/ folder is vestigial
- UIA may not work on some apps (games, Electron apps) — OCR fallback should be used
