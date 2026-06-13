using System.Diagnostics;
using System.Linq;
using PeekabooWin.Core.Capture;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Memory;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Ocr;
using PeekabooWin.Core.Perception;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.Trace;
using PeekabooWin.Core.UIAutomation;
using PeekabooWin.Core.Verification;
using PeekabooWin.Core.Windows;

namespace PeekabooWin.Core.Agent;

public class AgentOrchestrator
{
    private readonly TaskParser _taskParser;
    private readonly ActionExecutor _actionExecutor;
    private readonly VacpSkillIntegration _skillIntegration;
    private readonly VacpTraceLogger _traceLogger;
    private readonly ActionRiskGate _riskGate;
    private readonly RecoveryPlanner _recoveryPlanner;
    private readonly ActionVerifier _actionVerifier;
    private readonly ElementCandidateRanker _candidateRanker;
    private readonly CaptureService _captureService;
    private readonly OcrService _ocrService;
    private readonly UIAutomationService _uiaService;
    private readonly WindowService _windowService;
    private readonly TempFileManager _tempFiles;
    private readonly SkillTransferController _skillTransferController;
    private readonly ILlmClient? _llmClient;

    private static readonly HashSet<string> RiskGatedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "click", "hotkey", "ocr-click", "press"
    };

    private static readonly HashSet<string> ElementTargetActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "click", "ocr-click", "click-element", "click-element-guess", "click-rel", "find", "find-on-screen", "ocr-find"
    };

    public AgentOrchestrator(
        TaskParser taskParser,
        ActionExecutor actionExecutor,
        VacpSkillIntegration skillIntegration,
        VacpTraceLogger traceLogger,
        ActionRiskGate riskGate,
        RecoveryPlanner recoveryPlanner,
        ActionVerifier actionVerifier,
        ElementCandidateRanker candidateRanker,
        CaptureService captureService,
        OcrService ocrService,
        UIAutomationService uiaService,
        WindowService windowService,
        TempFileManager tempFiles,
        SkillTransferController skillTransferController,
        ILlmClient? llmClient = null)
    {
        _taskParser = taskParser;
        _actionExecutor = actionExecutor;
        _skillIntegration = skillIntegration;
        _traceLogger = traceLogger;
        _riskGate = riskGate;
        _recoveryPlanner = recoveryPlanner;
        _actionVerifier = actionVerifier;
        _candidateRanker = candidateRanker;
        _captureService = captureService;
        _ocrService = ocrService;
        _uiaService = uiaService;
        _windowService = windowService;
        _tempFiles = tempFiles;
        _skillTransferController = skillTransferController;
        _llmClient = llmClient;
    }

    public async Task<AgentTaskResponse> RunAsync(AgentTaskRequest request, CancellationToken cancellationToken = default)
    {
        var trace = new ExecutionTrace
        {
            TraceId = "trace_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            Task = request.Task,
            StartedAt = DateTime.UtcNow,
            TimeoutMs = request.TimeoutMs
        };

        var response = new AgentTaskResponse
        {
            Task = request.Task,
            TimeoutMs = request.TimeoutMs
        };

        using var timeoutCts = request.TimeoutMs > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (timeoutCts != null)
        {
            timeoutCts.CancelAfter(request.TimeoutMs);
        }

        var effectiveToken = timeoutCts?.Token ?? cancellationToken;

        try
        {
            effectiveToken.ThrowIfCancellationRequested();

            var plan = await _taskParser.ParseTaskAsync(request.Task, request.Context, effectiveToken);
            var parseMeta = _taskParser.GetLastParseMetadata();
            response.ParserMode = parseMeta.ParserMode;
            response.LlmEnabled = parseMeta.LlmEnabled;
            response.FallbackReason = parseMeta.FallbackReason;
            response.LlmErrorCode = parseMeta.LlmErrorCode;
            response.LlmModel = _llmClient?.ProviderName ?? "none";
            trace.ParserMode = parseMeta.ParserMode;
            trace.LlmEnabled = parseMeta.LlmEnabled;
            trace.FallbackReason = parseMeta.FallbackReason;

            var taskRiskDecision = _riskGate.EvaluateTask(request.Task);
            trace.TaskRiskDecision = taskRiskDecision.Decision.ToString().ToUpperInvariant();
            trace.TaskRiskScore = taskRiskDecision.RiskScore;
            trace.TaskRiskPattern = taskRiskDecision.MatchedPattern;

            if (taskRiskDecision.Decision == RiskLevel.Block)
            {
                PekaLogger.Warn("AgentOrchestrator", $"Task blocked by task-level risk gate: {taskRiskDecision.BlockReason}");
                response.Success = false;
                response.Error = $"Task blocked: {taskRiskDecision.BlockReason}";
                response.LlmModel = _llmClient?.ProviderName ?? "none";
                trace.Success = false;
                trace.Decision = "BLOCK";
                trace.RiskLevel = "L2";
                trace.CompletedAt = DateTime.UtcNow;
                response.Trace = trace;
                return response;
            }

            var planRiskDecision = _riskGate.EvaluatePlan(plan, request.Task);
            trace.PlanRiskDecision = planRiskDecision.Decision.ToString().ToUpperInvariant();
            trace.PlanRiskScore = planRiskDecision.RiskScore;
            trace.PlanRiskMatchedStep = planRiskDecision.MatchedStep;

            if (planRiskDecision.Decision == RiskLevel.Block)
            {
                PekaLogger.Warn("AgentOrchestrator", $"Plan blocked by plan-level risk gate: {planRiskDecision.BlockReason}");
                response.Success = false;
                response.Error = $"Plan blocked: {planRiskDecision.BlockReason}";
                response.LlmModel = _llmClient?.ProviderName ?? "none";
                trace.Success = false;
                trace.Decision = "BLOCK";
                trace.RiskLevel = "L2";
                trace.CompletedAt = DateTime.UtcNow;
                response.Trace = trace;
                return response;
            }

            var steps = new List<AgentStep>();

            for (int i = 0; i < Math.Min(plan.Count, request.MaxSteps); i++)
            {
                effectiveToken.ThrowIfCancellationRequested();

                var step = plan[i];
                var stepTrace = new StepTrace
                {
                    StepIndex = i,
                    Action = step.Action,
                    Args = step.Args,
                    Thought = step.Thought,
                    ExecutedAt = DateTime.UtcNow
                };

                var stepSw = Stopwatch.StartNew();

                var stepResult = new AgentStep
                {
                    Step = i + 1,
                    Thought = step.Thought,
                    Action = step.Action,
                    Args = step.Args ?? new()
                };

                try
                {
                    if (RiskGatedActions.Contains(step.Action))
                    {
                        var riskContext = new ActionRiskContext
                        {
                            ActionType = step.Action,
                            InputText = step.Args?.GetValueOrDefault("text"),
                            TargetLabel = step.Args?.GetValueOrDefault("name")
                                ?? step.Args?.GetValueOrDefault("element")
                                ?? step.Args?.GetValueOrDefault("text")
                        };
                        var riskDecision = _riskGate.Evaluate(riskContext);

                        stepTrace.RiskGate = new RiskGateTrace
                        {
                            Decision = riskDecision.Decision.ToString().ToUpperInvariant(),
                            RiskScore = riskDecision.RiskScore,
                            BlockReason = riskDecision.BlockReason,
                            RequiredConfirmation = riskDecision.RequiredConfirmation
                        };

                        trace.Decision = riskDecision.Decision.ToString().ToUpperInvariant();
                        trace.RiskLevel = riskDecision.RiskScore >= 0.6 ? "L2" : riskDecision.RiskScore >= 0.3 ? "L1" : "L0";

                        if (riskDecision.Decision == RiskLevel.Block)
                        {
                            stepResult.Success = false;
                            stepResult.Error = $"Blocked by risk gate: {riskDecision.BlockReason}";
                            stepTrace.Success = false;
                            stepTrace.Error = stepResult.Error;
                            stepSw.Stop();
                            stepTrace.LatencyMs = stepSw.ElapsedMilliseconds;
                            trace.StepTraces.Add(stepTrace);
                            steps.Add(stepResult);
                            PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} blocked by risk gate: {riskDecision.BlockReason}");
                            break;
                        }
                    }

                    if (ElementTargetActions.Contains(step.Action))
                    {
                        var windowKeyword = step.Args?.GetValueOrDefault("window")
                            ?? step.Args?.GetValueOrDefault("title") ?? "";

                        var sig = _skillIntegration.BuildWindowSignatureAsync(windowKeyword).GetAwaiter().GetResult();
                        var app = AppProfile.FromWindowSignature(sig);

                        var searchResults = _skillIntegration.SearchWithContextAsync(request.Task, windowKeyword).GetAwaiter().GetResult();
                        var topSkill = searchResults.FirstOrDefault();

                        TransferDecision? transferDecision = null;
                        if (topSkill != null)
                        {
                            transferDecision = _skillTransferController.Decide(new TransferContext
                            {
                                Skill = topSkill.Skill,
                                CurrentApp = app,
                                TaskText = request.Task,
                                SkillMatchScore = topSkill.Score.Total,
                                VisibleTexts = sig.VisibleTexts
                            });

                            stepTrace.TransferDecision = new TransferDecisionTrace
                            {
                                SkillId = topSkill.Skill.SkillId,
                                SkillName = topSkill.Skill.Name,
                                Action = transferDecision.Action.ToString(),
                                Reason = transferDecision.Reason,
                                BlockReason = transferDecision.BlockReason,
                                SkillMatchScore = transferDecision.SkillMatchScore,
                                CoverageScore = transferDecision.CoverageScore
                            };

                            if (transferDecision.Action == TransferAction.BLOCK)
                            {
                                PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} skill transfer BLOCKED: {topSkill.Skill.SkillId} — {transferDecision.Reason}");
                            }
                            else if (transferDecision.Action == TransferAction.HUMAN_REVIEW)
                            {
                                PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} skill transfer HUMAN_REVIEW (CLI: blocking by default): {topSkill.Skill.SkillId} — {transferDecision.Reason}");
                                stepResult.Success = false;
                                stepResult.Error = $"Transfer blocked (HUMAN_REVIEW): {transferDecision.Reason}";
                                stepTrace.Success = false;
                                stepTrace.Error = stepResult.Error;
                                stepSw.Stop();
                                stepTrace.LatencyMs = stepSw.ElapsedMilliseconds;
                                trace.StepTraces.Add(stepTrace);
                                steps.Add(stepResult);
                                break;
                            }
                        }

                        var targetText = step.Args?.GetValueOrDefault("name")
                            ?? step.Args?.GetValueOrDefault("element")
                            ?? step.Args?.GetValueOrDefault("text")
                            ?? "";

                        if (!string.IsNullOrEmpty(targetText))
                        {
                            var rankRequest = new CandidateRankRequest
                            {
                                TargetText = targetText
                            };

                            if (!string.IsNullOrEmpty(windowKeyword))
                            {
                                try
                                {
                                    var findResult = _uiaService.FindByName(windowKeyword, targetText);
                                    if (findResult.Success)
                                    {
                                        foreach (var m in findResult.Matches)
                                        {
                                            rankRequest.UiaCandidates.Add(new UiElement
                                            {
                                                Label = m.Name ?? "",
                                                Name = m.Name ?? "",
                                                BBox = m.BoundingBox != null
                                                    ? new BoundingBox
                                                    {
                                                        X = m.BoundingBox.X,
                                                        Y = m.BoundingBox.Y,
                                                        Width = m.BoundingBox.Width,
                                                        Height = m.BoundingBox.Height
                                                    }
                                                    : new BoundingBox(),
                                                Confidence = 1.0,
                                                Source = "uia"
                                            });
                                        }
                                    }
                                }
                                catch { }
                            }

                            var rankResult = _candidateRanker.Rank(rankRequest);

                            stepTrace.CandidateRanking = new CandidateRankTrace
                            {
                                TotalCandidates = rankResult.TotalCandidates,
                                BestScore = rankResult.BestCandidate?.FinalGroundingScore ?? 0,
                                BestText = rankResult.BestCandidate?.Text ?? "",
                                BestSource = rankResult.BestCandidate?.Source ?? "",
                                HasViableCandidate = rankResult.HasViableCandidate
                            };

                            trace.GroundingScore = rankResult.BestCandidate?.FinalGroundingScore ?? 0;
                        }
                    }

                    string? beforeScreenshotPath = null;
                    string? beforeOcrText = null;
                    int? beforeElementCount = null;

                    if (!request.DryRun)
                    {
                        try
                        {
                            beforeScreenshotPath = _tempFiles.CreateTempPath("before");
                            var beforeCapture = _captureService.CaptureScreen(beforeScreenshotPath);
                            if (beforeCapture.Success)
                            {
                                var beforeOcr = await _ocrService.RecognizeImageAsync(beforeScreenshotPath);
                                if (beforeOcr.Success)
                                {
                                    beforeOcrText = beforeOcr.Text;
                                }
                            }
                            else
                            {
                                beforeScreenshotPath = null;
                            }

                            var windowKeyword = step.Args?.GetValueOrDefault("window")
                                ?? step.Args?.GetValueOrDefault("title");
                            if (!string.IsNullOrEmpty(windowKeyword))
                            {
                                try
                                {
                                    var inspectResult = _uiaService.Inspect(windowKeyword);
                                    if (inspectResult.Success)
                                    {
                                        beforeElementCount = inspectResult.ElementCount;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            PekaLogger.Warn("AgentOrchestrator", $"Before-state capture failed for step {i + 1}", ex);
                        }
                    }

                    if (request.DryRun)
                    {
                        stepResult.Success = true;
                        stepResult.Result = $"[DRY-RUN] Would execute: {step.Action}";
                        stepTrace.Success = true;
                        stepTrace.Result = stepResult.Result;
                    }
                    else
                    {
                        var (success, result) = await _actionExecutor.ExecuteActionAsync(step.Action, step.Args ?? new(), effectiveToken);
                        stepResult.Success = success;
                        stepResult.Result = result;
                        stepTrace.Success = success;
                        stepTrace.Result = result;

                        if (success)
                        {
                            try
                            {
                                var verifyRequest = new VerificationRequest
                                {
                                    Action = step.Action,
                                    Args = step.Args,
                                    BeforeScreenshotPath = beforeScreenshotPath,
                                    BeforeOcrText = beforeOcrText,
                                    BeforeElementCount = beforeElementCount
                                };
                                var verifyResult = await _actionVerifier.VerifyAsync(verifyRequest, effectiveToken);

                                stepTrace.Verification = new VerificationTrace
                                {
                                    Status = verifyResult.Status.ToString(),
                                    Reason = verifyResult.Reason,
                                    Confidence = verifyResult.Confidence
                                };
                            }
                            catch (Exception ex)
                            {
                                PekaLogger.Warn("AgentOrchestrator", $"Verification failed for step {i + 1}", ex);
                                stepTrace.Verification = new VerificationTrace
                                {
                                    Status = "Inconclusive",
                                    Reason = $"Verification error: {ex.Message}",
                                    Confidence = 0
                                };
                            }
                        }
                        else
                        {
                            try
                            {
                                var windowKeyword = step.Args?.GetValueOrDefault("window")
                                    ?? step.Args?.GetValueOrDefault("title");
                                var windowTitle = "";
                                if (!string.IsNullOrEmpty(windowKeyword))
                                {
                                    var win = _windowService.FindWindow(windowKeyword);
                                    if (win != null) windowTitle = win.Title;
                                }

                                var recoveryContext = new RecoveryContext
                                {
                                    FailedAction = step.Action,
                                    FailureReason = result,
                                    FailedArgs = step.Args,
                                    AttemptNumber = 1,
                                    MaxAttempts = 2,
                                    WindowTitle = windowTitle,
                                    WindowLostFocus = result.Contains("not found", StringComparison.OrdinalIgnoreCase)
                                        && !string.IsNullOrEmpty(windowKeyword),
                                    ElementNotFound = result.Contains("element not found", StringComparison.OrdinalIgnoreCase)
                                        || result.Contains("not found", StringComparison.OrdinalIgnoreCase),
                                    TimeoutOccurred = false
                                };

                                var recoveryPlan = _recoveryPlanner.PlanRecovery(recoveryContext);

                                stepTrace.Recovery = new RecoveryTrace
                                {
                                    Strategy = recoveryPlan.Strategy.ToString(),
                                    ShouldRetry = recoveryPlan.ShouldRetry,
                                    RecoveryStepCount = recoveryPlan.RecoverySteps.Count
                                };

                                if (recoveryPlan.ShouldRetry && recoveryPlan.RecoverySteps.Count > 0)
                                {
                                    PekaLogger.Info("AgentOrchestrator", $"Step {i + 1} failed, executing recovery ({recoveryPlan.Strategy})");

                                    foreach (var recoveryStep in recoveryPlan.RecoverySteps)
                                    {
                                        effectiveToken.ThrowIfCancellationRequested();

                                        var (retrySuccess, retryResult) = await _actionExecutor.ExecuteActionAsync(
                                            recoveryStep.Action,
                                            recoveryStep.Args ?? new(),
                                            effectiveToken);

                                        if (retrySuccess)
                                        {
                                            stepResult.Success = true;
                                            stepResult.Result = retryResult;
                                            stepTrace.Success = true;
                                            stepTrace.Result = retryResult;
                                            trace.RecoveryAttempts++;
                                            break;
                                        }
                                    }

                                    if (stepResult.Success)
                                    {
                                        try
                                        {
                                            var verifyRequest = new VerificationRequest
                                            {
                                                Action = step.Action,
                                                Args = step.Args,
                                                BeforeScreenshotPath = beforeScreenshotPath,
                                                BeforeOcrText = beforeOcrText,
                                                BeforeElementCount = beforeElementCount
                                            };
                                            var verifyResult = await _actionVerifier.VerifyAsync(verifyRequest, effectiveToken);

                                            stepTrace.Verification = new VerificationTrace
                                            {
                                                Status = verifyResult.Status.ToString(),
                                                Reason = verifyResult.Reason,
                                                Confidence = verifyResult.Confidence
                                            };
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                PekaLogger.Warn("AgentOrchestrator", $"Recovery planning failed for step {i + 1}", ex);
                            }
                        }
                    }

                    stepSw.Stop();
                    stepTrace.LatencyMs = stepSw.ElapsedMilliseconds;
                    trace.StepTraces.Add(stepTrace);
                    steps.Add(stepResult);

                    if (!stepResult.Success && !request.DryRun) break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    stepResult.Success = false;
                    stepResult.Error = "Operation was cancelled";
                    stepTrace.Success = false;
                    stepTrace.Error = "Operation was cancelled";
                    stepSw.Stop();
                    stepTrace.LatencyMs = stepSw.ElapsedMilliseconds;
                    trace.StepTraces.Add(stepTrace);
                    steps.Add(stepResult);
                    response.Cancelled = true;
                    trace.Cancelled = true;
                    PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} cancelled");
                    break;
                }
                catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
                {
                    stepResult.Success = false;
                    stepResult.Error = $"Timeout after {request.TimeoutMs}ms";
                    stepTrace.Success = false;
                    stepTrace.Error = stepResult.Error;
                    stepSw.Stop();
                    stepTrace.LatencyMs = stepSw.ElapsedMilliseconds;
                    trace.StepTraces.Add(stepTrace);
                    steps.Add(stepResult);
                    response.TimeoutTriggered = true;
                    trace.TimeoutTriggered = true;
                    PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} timed out after {request.TimeoutMs}ms");
                    break;
                }
                catch (Exception ex)
                {
                    stepResult.Success = false;
                    stepResult.Error = ex.Message;
                    stepTrace.Success = false;
                    stepTrace.Error = ex.Message;
                    stepSw.Stop();
                    stepTrace.LatencyMs = stepSw.ElapsedMilliseconds;
                    trace.StepTraces.Add(stepTrace);
                    steps.Add(stepResult);
                    PekaLogger.Error("AgentOrchestrator", $"Step {i + 1} threw exception", ex);
                    break;
                }
            }

            response.Steps = steps;
            response.Success = steps.All(s => s.Success);
            response.FinalResult = BuildFinalResult(steps);

            trace.TotalSteps = steps.Count;
            trace.SuccessfulSteps = steps.Count(s => s.Success);
            trace.FailedSteps = steps.Count(s => !s.Success);
            trace.BlockedSteps = trace.StepTraces.Count(s => s.RiskGate?.Decision == "BLOCK");
            trace.RecoveryAttempts = trace.StepTraces.Count(s => s.Recovery != null && s.Recovery.ShouldRetry);
            trace.Success = response.Success;
            trace.CompletedAt = DateTime.UtcNow;
            trace.Error = response.Error;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response.Success = false;
            response.Error = "Task was cancelled";
            response.Cancelled = true;
            trace.Success = false;
            trace.Cancelled = true;
            trace.Error = "Task was cancelled";
            trace.CompletedAt = DateTime.UtcNow;
            PekaLogger.Warn("AgentOrchestrator", "RunAsync cancelled");
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            response.Success = false;
            response.Error = $"Task timed out after {request.TimeoutMs}ms";
            response.TimeoutTriggered = true;
            trace.Success = false;
            trace.TimeoutTriggered = true;
            trace.Error = $"Task timed out after {request.TimeoutMs}ms";
            trace.CompletedAt = DateTime.UtcNow;
            PekaLogger.Warn("AgentOrchestrator", $"RunAsync timed out after {request.TimeoutMs}ms");
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            trace.Success = false;
            trace.Error = ex.Message;
            trace.CompletedAt = DateTime.UtcNow;
            PekaLogger.Error("AgentOrchestrator", "RunAsync failed", ex);
        }

        response.Trace = trace;
        return response;
    }

    private static string BuildFinalResult(List<AgentStep> steps)
    {
        if (steps.Count == 0)
            return "No steps executed";
        if (steps.All(s => s.Success))
            return $"Completed {steps.Count} step(s)";
        var failed = steps.FirstOrDefault(s => !s.Success);
        return $"Failed at step {failed?.Step}: {failed?.Error ?? "unknown error"}";
    }
}
