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

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plan = _taskParser.ParseTask(request.Task, request.Context);
            var steps = new List<AgentStep>();

            for (int i = 0; i < Math.Min(plan.Count, request.MaxSteps); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                        var (success, result) = await _actionExecutor.ExecuteActionAsync(step.Action, step.Args ?? new(), cancellationToken);
                        stepResult.Success = success;
                        stepResult.Result = result;
                    }

                    steps.Add(stepResult);
                    if (!stepResult.Success && !request.DryRun) break;
                }
                catch (OperationCanceledException)
                {
                    stepResult.Success = false;
                    stepResult.Error = "Operation was cancelled";
                    steps.Add(stepResult);
                    PekaLogger.Warn("AgentOrchestrator", $"Step {i + 1} cancelled");
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
        catch (OperationCanceledException)
        {
            response.Success = false;
            response.Error = "Task was cancelled";
            PekaLogger.Warn("AgentOrchestrator", "RunAsync cancelled");
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