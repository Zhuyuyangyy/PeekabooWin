# PeekabooWin V0.14 Agent Runtime Integration Report

## Executive Summary

V0.14 closes the integration gap between the standalone components introduced in V0.13 and the `AgentOrchestrator` execution loop. The `ActionVerifier` and `RecoveryPlanner` are now wired as first-class pipeline stages, the `ExecutionTrace` model captures the complete lifecycle of every task, and the Agent Runtime API v1 exposes seven endpoints with a unified response format. All 120+ tests pass.

## ExecutionTrace Verification

The `ExecutionTrace` model was verified to capture all sub-trace data produced during orchestrator execution:

| Sub-trace | Fields Verified | Source Component |
|-----------|----------------|------------------|
| `RiskGateTrace` | Decision, RiskScore, BlockReason, RequiredConfirmation | `ActionRiskGate.Evaluate` |
| `VerificationTrace` | Status, Reason, Confidence | `ActionVerifier.VerifyAsync` |
| `RecoveryTrace` | Strategy, ShouldRetry, RecoveryStepCount | `RecoveryPlanner.PlanRecovery` |
| `CandidateRankTrace` | TotalCandidates, BestScore, BestText, BestSource, HasViableCandidate | `ElementCandidateRanker.Rank` |

**Trace lifecycle:**

1. `AgentOrchestrator.RunAsync` creates `ExecutionTrace` at entry with `TraceId = "trace_yyyyMMdd_HHmmss"`
2. Each step creates a `StepTrace` with `RiskGateTrace` (if risk-gated action), `CandidateRankTrace` (if element-target action), `VerificationTrace` (on success), and `RecoveryTrace` (on failure)
3. Recovery steps are executed inline when `RecoveryPlan.ShouldRetry = true`
4. Post-recovery verification is performed on recovered steps
5. Summary fields (`TotalSteps`, `SuccessfulSteps`, `FailedSteps`, `BlockedSteps`, `RecoveryAttempts`) are computed at completion

**Model serialization:** All trace models serialize correctly via `System.Text.Json`. Field names use snake_case via `[JsonPropertyName]` attributes.

## API v1 Endpoint Verification

Schema verification confirms all seven endpoints are defined and return the unified response format:

| # | Endpoint | Method | Request Schema | Response Schema |
|---|----------|--------|----------------|-----------------|
| 1 | `/api/v1/health` | GET | None | `{ ok, status, time }` |
| 2 | `/api/v1/task/preview` | POST | `AgentTaskRequest` (dry_run=true) | Unified response |
| 3 | `/api/v1/task/run` | POST | `AgentTaskRequest` | Unified response |
| 4 | `/api/v1/skill/search` | POST | `{ context, window_title, process_name }` | Unified response |
| 5 | `/api/v1/skill/replay` | POST | `{ skill_id, task_text, app_id }` | Unified response |
| 6 | `/api/v1/risk/evaluate` | POST | `{ action, args }` | Unified response |
| 7 | `/api/v1/trace/{id}` | GET | Path param: trace ID | Unified response |

> Note: Schema verification only. Real HTTP integration tests require a running server on a Windows desktop.

## RecoveryPlanner Integration Verification

The `RecoveryPlanner` is now fully integrated into the `AgentOrchestrator.RunAsync` execution loop:

**Integration flow:**

1. When `ActionExecutor.ExecuteActionAsync` returns failure (`success = false`)
2. `RecoveryPlanner.PlanRecovery` is called with a `RecoveryContext` derived from the failed step
3. If `RecoveryPlan.ShouldRetry = true`, each `RecoveryStep` is executed via `ActionExecutor.ExecuteActionAsync`
4. If a recovery step succeeds, post-recovery verification is performed via `ActionVerifier.VerifyAsync`
5. The `RecoveryTrace` is recorded in the step trace regardless of recovery outcome

**Verified recovery strategies:**

| Strategy | Trigger | Verified |
|----------|---------|----------|
| Retry | Generic failure, timeout | ✅ |
| Refocus | Window lost focus | ✅ |
| Relocate | Element not found | ✅ |
| Replan | Max attempts exceeded (non-dangerous) | ✅ |
| HumanReview | Max attempts exceeded (dangerous action) | ✅ |
| Abort | Timeout with max attempts exceeded | ✅ |

**Escalation behavior:** When `AttemptNumber >= MaxAttempts`, the planner escalates to `Replan` (non-dangerous) or `HumanReview` (dangerous actions: type, click, hotkey, ocr-click, etc.).

## Test Coverage

| Metric | Value |
|--------|-------|
| Total tests | 120+ |
| Passing | 120+ |
| Pass rate | 100% |

**Test file breakdown:**

| Test File | Tests | Area |
|-----------|-------|------|
| `ActionRiskGateTests.cs` | — | Risk gate evaluation |
| `ActionVerifierModelTests.cs` | — | Verification model and logic |
| `ApiResponseModelTests.cs` | — | API response serialization |
| `AppProfileTests.cs` | — | App profile and window signature |
| `AsyncCancellationTests.cs` | — | Cancellation and timeout |
| `ElementCandidateRankerTests.cs` | — | Multi-candidate ranking |
| `NegativeTransferGuardTests.cs` | — | Negative transfer blocking |
| `OcrResultTests.cs` | — | OCR result model |
| `ParserFallbackTraceTests.cs` | — | Parser fallback tracing |
| `RecoveryPlannerTests.cs` | 10 | Recovery strategy selection |
| `SkillReplayEngineTests.cs` | — | Skill replay |
| `SkillScopeValidatorTests.cs` | — | Skill scope validation |
| `TaskParserTests.cs` | — | Task parsing |
| `TimeoutTests.cs` | — | Timeout handling |

**New in V0.14:** ExecutionTrace model tests, unified API response format tests, API v1 endpoint schema tests.

## Known Issues

| ID | Description | Status |
|----|-------------|--------|
| KI-17 | `InputService.Click/RightClick` uses `Thread.Sleep(50)` for Win32 timing | Open — Win32 API requirement |
| KI-18 | `ActionVerifier` requires real desktop for integration testing | Partially fixed — verifier integrated into orchestrator; full integration tests need real desktop |
| KI-19 | `RecoveryPlanner` not integrated with `AgentOrchestrator` | Fixed — planner wired into execution loop |

## Conclusion

V0.14 delivers the Agent Runtime Integration milestone:

- **ActionVerifier** and **RecoveryPlanner** are first-class pipeline stages in the orchestrator loop
- **ExecutionTrace** captures the complete task lifecycle with four sub-trace models
- **Agent Runtime API v1** provides seven endpoints with a unified response format
- **120+ tests** all pass with expanded coverage

The system is now positioned as a trusted visual desktop automation runtime that any Windows agent can call into for safe, observable, and recoverable task execution.
