# Known Issues

## V0.10.5 (Engineering Hardening Patch)

- **KI-14: TaskParser.TryLLMParse still uses .GetAwaiter().GetResult() for synchronous LLM call**: The `CallMiniMaxAsync` call inside `TryLLMParse` is synchronous because `ParseTask` returns `List<AgentStep>` (not `Task<List<AgentStep>>`). Making `ParseTask` async would require changing the entire call chain. This is acceptable because the LLM call is CPU-bound (HTTP request) and the method is called from non-UI contexts. Tracked for V0.12 async parser refactor.
- **KI-15: AgentService.TryLLMParse also uses .GetAwaiter().GetResult()**: Same reason as KI-14. The legacy `AgentService` path still has a synchronous LLM call. The `AgentOrchestrator` → `TaskParser` path has the same limitation. Will be resolved together in V0.12.
- **KI-16: VacpSkillIntegration.BuildWindowSignature uses .GetAwaiter().GetResult()**: The synchronous wrapper exists for backward compatibility with `SearchWithContext`. The async version `BuildWindowSignatureAsync` is available and preferred.

## V0.10 (Engineering Hardening) — Fixed in V0.10.5

- **~~KI-10: AgentOrchestrator.RunAsync does not yet propagate cancellation tokens~~**: Fixed in V0.10.5 — `RunAsync` now accepts `CancellationToken`, checks it at each step, and passes it to `ActionExecutor.ExecuteActionAsync`.
- **~~KI-11: TaskParser LLM fallback requires MINIMAX_API_KEY, no graceful degradation message when key is missing~~**: Fixed in V0.10.5 — `TaskParser` now logs explicit warnings via `PekaLogger`, exposes `LastFallbackReason`, `LastLlmEnabled`, `LastParserMode` properties, and provides `GetLastParseMetadata()` for structured access.
- **~~KI-12: ActionExecutor still has sync wrappers for backward compatibility (4 remaining GetAwaiter().GetResult() calls)~~**: Fixed in V0.10.5 — `ExecuteAction` sync wrapper removed from `ActionExecutor`. `AgentService.ExecuteTask` sync wrapper removed. Remaining 3 `GetAwaiter().GetResult()` calls are in LLM/skill paths where async refactor requires API break (see KI-14, KI-15, KI-16).
- **~~KI-13: Test project not included in PeekabooWin.sln~~**: Fixed in V0.10.5 — `PeekabooWin.Core.Tests.csproj` added to solution file.

## V0.9.4 (Baseline Validation) — Fixed in V0.10.5

- **~~KI-4: `ocr` command returns 0 words, error not surfaced~~**: Fixed in V0.10 — `HandleOcr` now uses shared `OcrService` via DI. Fixed in V0.10.5 — `OcrResult.Engine` default changed from misleading `"Tesseract"` to `""`.
- **~~KI-5: `OcrResult.Engine` default is "Tesseract"~~**: Fixed in V0.10.5 — Default changed to `""`. The actual engine is always set to `"Windows.Media.Ocr"` by `OcrService.RecognizeSoftwareBitmapAsync`. If Engine is empty, it means OCR was never successfully executed.

## V0.9.4 (Baseline Validation)

- **KI-9: Test coverage — only 3 test files**: Agent, VACP, OCR, Input, Capture, Window have zero unit tests. Expanding in V0.11.

## V0.9
- **WindowSignature.SimilarityTo() score**: When WindowType+InputMode+RiskDomain are identical but ProcessFamily differs, the score is ~0.1 (not 1.0). The weighted formula (0.4+0.3+0.2)/9 = 0.1 for same family is the design intent. Full family match adds +0.1.

## All Versions
- UIA may not work on some apps (games, Electron apps) — OCR fallback should be used
