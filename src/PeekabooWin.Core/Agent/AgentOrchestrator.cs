using System.Linq;
using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Models;
using PeekabooWin.Core.Planning;
using PeekabooWin.Core.Safety;

namespace PeekabooWin.Core.Agent;

public class AgentOrchestrator
{
    private readonly TaskParser _taskParser;
    private readonly ActionExecutor _actionExecutor;
    private readonly VacpSkillIntegration _skillIntegration;
    private readonly VacpTraceLogger _traceLogger;
    private readonly ActionRiskGate _riskGate;

    public AgentOrchestrator(
        TaskParser taskParser,
        ActionExecutor actionExecutor,
        VacpSkillIntegration skillIntegration,
        VacpTraceLogger traceLogger,
        ActionRiskGate riskGate)
    {
        _taskParser = taskParser;
        _actionExecutor = actionExecutor;
        _skillIntegration = skillIntegration;
        _traceLogger = traceLogger;
        _riskGate = riskGate;
    }

    public async Task<AgentTaskResponse> RunAsync(AgentTaskRequest request, CancellationToken cancellationToken = default)
    {
        var response = new AgentTaskResponse
        {
            Task = request.Task
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

            var steps = new List<AgentStep>();

            for (int i = 0; i < Math.Min(plan.Count, request.MaxSteps); i++)
            {
                effectiveToken.ThrowIfCancellationRequested();

                var step = plan[i];
                var stepResult = new AgentStep
                {
                    Step = i + 1,
                    Thought = step.Thought,
                    Action = step.Action,
                    Args = step.Args
                };

                try
                {
                    if (step.Action is "type" or "click" or "hotkey" or "ocr-click")
                    {
                        var riskContext = new ActionRiskContext
                        {
                            ActionType = step.Action,
                            InputText = step.Args?.GetValueOrDefault("text"),
                            TargetLabel = step.Args?.GetValueOrDefault("name")
                        };
                        var riskDecision = _riskGate.Evaluate(riskContext);
                        if (riskDecision.Decision == RiskLevel.Block)
                        {
                            stepResult.Success = false;
                            stepResult.Error = $"Blocked by risk gate: {riskDecision.BlockReason}";
                            steps.Add(stepResult);
                            PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} blocked by risk gate: {riskDecision.BlockReason}");
                            break;
                        }
                    }

                    if (request.DryRun)
                    {
                        stepResult.Success = true;
                        stepResult.Result = $"[DRY-RUN] Would execute: {step.Action}";
                    }
                    else
                    {
                        var (success, result) = await _actionExecutor.ExecuteActionAsync(step.Action, step.Args ?? new(), effectiveToken);
                        stepResult.Success = success;
                        stepResult.Result = result;
                    }

                    steps.Add(stepResult);
                    if (!stepResult.Success && !request.DryRun) break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    stepResult.Success = false;
                    stepResult.Error = "Operation was cancelled";
                    steps.Add(stepResult);
                    response.Cancelled = true;
                    PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} cancelled");
                    break;
                }
                catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
                {
                    stepResult.Success = false;
                    stepResult.Error = $"Timeout after {request.TimeoutMs}ms";
                    steps.Add(stepResult);
                    response.TimeoutTriggered = true;
                    PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} timed out after {request.TimeoutMs}ms");
                    break;
                }
                catch (Exception ex)
                {
                    stepResult.Success = false;
                    stepResult.Error = ex.Message;
                    steps.Add(stepResult);
                    PekaLogger.Error("AgentOrchestrator", $"Step {i + 1} threw exception", ex);
                    break;
                }
            }

            response.Steps = steps;
            response.Success = steps.All(s => s.Success);
            response.FinalResult = BuildFinalResult(steps);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response.Success = false;
            response.Error = "Task was cancelled";
            response.Cancelled = true;
            PekaLogger.Warn("AgentOrchestrator", "RunAsync cancelled");
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            response.Success = false;
            response.Error = $"Task timed out after {request.TimeoutMs}ms";
            response.TimeoutTriggered = true;
            PekaLogger.Warn("AgentOrchestrator", $"RunAsync timed out after {request.TimeoutMs}ms");
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            PekaLogger.Error("AgentOrchestrator", "RunAsync failed", ex);
        }

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