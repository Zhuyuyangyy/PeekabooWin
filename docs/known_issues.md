# Known Issues

## V0.14 (Agent Runtime Integration)

- **KI-20: Trace storage not yet implemented**: The GET /api/v1/trace/{trace_id} endpoint returns a placeholder. Trace persistence (file/database) is planned for V0.15.
- **KI-21: API v1 skill/replay requires VisualSkill lookup by ID**: The skill replay endpoint needs to look up skills by ID from the store, but the current implementation creates a new store per request. Skill persistence is needed for production use.
- **KI-22: AgentOrchestrator constructor has 13 dependencies**: The constructor is heavy. A service locator or builder pattern would be cleaner, but this is deferred to avoid breaking changes.

## V0.13 (Visual Robustness Enhancement) — Fixed in V0.14

- **~~KI-18: ActionVerifier requires real desktop for integration testing~~**: Fixed in V0.14 — ActionVerifier is now integrated into AgentOrchestrator execution loop. Unit tests cover model classes; integration testing with real desktop requires manual verification.
- **~~KI-19: RecoveryPlanner does not yet integrate with AgentOrchestrator~~**: Fixed in V0.14 — RecoveryPlanner is now integrated into AgentOrchestrator. On action failure, the orchestrator calls PlanRecovery and executes up to 1 retry with recovery steps.

## V0.12 (Stability Release)

- **KI-17: InputService.Click/RightClick uses Thread.Sleep(50) for Win32 timing**: `SetCursorPos` → `Thread.Sleep(50)` → `SendInput` is a Win32 API timing requirement. The 50ms delay ensures the cursor position is updated before the click event is sent. This cannot be replaced with `Task.Delay` because `SendInput` is a synchronous Win32 call that must execute on the same thread immediately after position update. This is the only remaining `Thread.Sleep` in the codebase (2 occurrences).

## V0.10.5 (Engineering Hardening Patch) — Fixed in V0.12

- **~~KI-14: TaskParser.TryLLMParse still uses .GetAwaiter().GetResult() for synchronous LLM call~~**: Fixed in V0.12 — `ParseTask` → `ParseTaskAsync`, `TryLLMParse` → `TryLLMParseAsync`. Full async chain: `AgentOrchestrator.RunAsync` → `TaskParser.ParseTaskAsync` → `TryLLMParseAsync` → `CallMiniMaxAsync`. CancellationToken propagated through entire chain.
- **~~KI-15: AgentService.TryLLMParse also uses .GetAwaiter().GetResult()~~**: Fixed in V0.12 — `ParseTask` → `ParseTaskAsync`, `TryLLMParse` → `TryLLMParseAsync`. Full async chain in AgentService as well.
- **~~KI-16: VacpSkillIntegration.BuildWindowSignature uses .GetAwaiter().GetResult()~~**: Fixed in V0.12 — `BuildWindowSignature` sync wrapper removed. `SearchWithContext` → `SearchWithContextAsync`. CLI handler updated to async.

## V0.10 (Engineering Hardening) — Fixed in V0.10.5

- **~~KI-10: AgentOrchestrator.RunAsync does not yet propagate cancellation tokens~~**: Fixed in V0.10.5
- **~~KI-11: TaskParser LLM fallback requires MINIMAX_API_KEY, no graceful degradation message when key is missing~~**: Fixed in V0.10.5
- **~~KI-12: ActionExecutor still has sync wrappers for backward compatibility~~**: Fixed in V0.10.5
- **~~KI-13: Test project not included in PeekabooWin.sln~~**: Fixed in V0.10.5

## V0.9.4 (Baseline Validation) — Fixed in V0.10.5

- **~~KI-4: `ocr` command returns 0 words, error not surfaced~~**: Fixed in V0.10.5
- **~~KI-5: `OcrResult.Engine` default is "Tesseract"~~**: Fixed in V0.10.5

## V0.9.4 (Baseline Validation)

- **KI-9: Test coverage — expanding**: 82 tests as of V0.12. Continuing to expand.

## V0.9
- **WindowSignature.SimilarityTo() score**: When WindowType+InputMode+RiskDomain are identical but ProcessFamily differs, the score is ~0.1 (not 1.0). The weighted formula (0.4+0.3+0.2)/9 = 0.1 for same family is the design intent. Full family match adds +0.1.

## All Versions
- UIA may not work on some apps (games, Electron apps) — OCR fallback should be used