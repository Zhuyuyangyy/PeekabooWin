using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Agent;

public enum RecoveryStrategy
{
    Retry,
    Refocus,
    Relocate,
    Replan,
    HumanReview,
    Abort
}

public class RecoveryContext
{
    public string FailedAction { get; set; } = "";
    public string? FailureReason { get; set; }
    public Dictionary<string, string>? FailedArgs { get; set; }
    public int AttemptNumber { get; set; }
    public int MaxAttempts { get; set; } = 2;
    public string? WindowTitle { get; set; }
    public bool WindowLostFocus { get; set; }
    public bool ElementNotFound { get; set; }
    public bool TimeoutOccurred { get; set; }
}

public class RecoveryPlan
{
    public RecoveryStrategy Strategy { get; set; }
    public List<AgentStep> RecoverySteps { get; set; } = new();
    public string Reason { get; set; } = "";
    public bool ShouldRetry { get; set; }
}

public class RecoveryPlanner
{
    private static readonly HashSet<string> DangerousActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "hotkey", "click", "click-rel", "ocr-click", "click-element", "click-element-guess"
    };

    public RecoveryPlan PlanRecovery(RecoveryContext context)
    {
        if (context.AttemptNumber >= context.MaxAttempts)
            return PlanEscalation(context);

        if (context.WindowLostFocus)
            return PlanRefocus(context);

        if (context.ElementNotFound)
            return PlanRelocate(context);

        if (context.TimeoutOccurred)
            return PlanRetry(context);

        return PlanGenericRetry(context);
    }

    private RecoveryPlan PlanRefocus(RecoveryContext context)
    {
        if (context.AttemptNumber >= context.MaxAttempts)
            return PlanEscalation(context);

        var steps = new List<AgentStep>();

        if (!string.IsNullOrEmpty(context.WindowTitle))
        {
            steps.Add(new AgentStep
            {
                Thought = $"Window lost focus, refocusing: {context.WindowTitle}",
                Action = "focus-window",
                Args = new Dictionary<string, string> { ["title"] = context.WindowTitle }
            });
        }

        steps.Add(new AgentStep
        {
            Thought = $"Retrying original action after refocus: {context.FailedAction}",
            Action = context.FailedAction,
            Args = context.FailedArgs ?? new Dictionary<string, string>()
        });

        return new RecoveryPlan
        {
            Strategy = RecoveryStrategy.Refocus,
            RecoverySteps = steps,
            Reason = $"Window lost focus (attempt {context.AttemptNumber}/{context.MaxAttempts})",
            ShouldRetry = true
        };
    }

    private RecoveryPlan PlanRelocate(RecoveryContext context)
    {
        if (context.AttemptNumber >= context.MaxAttempts)
            return new RecoveryPlan
            {
                Strategy = RecoveryStrategy.Replan,
                RecoverySteps = new List<AgentStep>(),
                Reason = $"Element not found after {context.MaxAttempts} attempts, need to replan approach",
                ShouldRetry = false
            };

        var steps = new List<AgentStep>();

        steps.Add(new AgentStep
        {
            Thought = "Element not found via UIA, taking screenshot to assess current state",
            Action = "screenshot",
            Args = new Dictionary<string, string> { ["out"] = $"recovery_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png" }
        });

        var ocrText = context.FailedArgs?.GetValueOrDefault("name")
            ?? context.FailedArgs?.GetValueOrDefault("text")
            ?? context.FailedArgs?.GetValueOrDefault("element")
            ?? "";

        if (!string.IsNullOrEmpty(ocrText))
        {
            var ocrArgs = new Dictionary<string, string> { ["text"] = ocrText };
            if (!string.IsNullOrEmpty(context.WindowTitle))
                ocrArgs["window"] = context.WindowTitle;

            steps.Add(new AgentStep
            {
                Thought = $"Trying OCR-based find instead of UIA for: {ocrText}",
                Action = "ocr-click",
                Args = ocrArgs
            });
        }
        else
        {
            steps.Add(new AgentStep
            {
                Thought = "Retrying original action with OCR coordinates",
                Action = context.FailedAction,
                Args = context.FailedArgs ?? new Dictionary<string, string>()
            });
        }

        return new RecoveryPlan
        {
            Strategy = RecoveryStrategy.Relocate,
            RecoverySteps = steps,
            Reason = $"Element not found via UIA, switching to OCR approach (attempt {context.AttemptNumber}/{context.MaxAttempts})",
            ShouldRetry = true
        };
    }

    private RecoveryPlan PlanRetry(RecoveryContext context)
    {
        if (context.AttemptNumber >= context.MaxAttempts)
            return new RecoveryPlan
            {
                Strategy = RecoveryStrategy.Abort,
                RecoverySteps = new List<AgentStep>(),
                Reason = $"Timeout occurred after {context.MaxAttempts} attempts, aborting",
                ShouldRetry = false
            };

        var steps = new List<AgentStep>
        {
            new()
            {
                Thought = "Timeout occurred, waiting briefly before retry",
                Action = context.FailedAction,
                Args = context.FailedArgs ?? new Dictionary<string, string>()
            }
        };

        return new RecoveryPlan
        {
            Strategy = RecoveryStrategy.Retry,
            RecoverySteps = steps,
            Reason = $"Timeout occurred, retrying (attempt {context.AttemptNumber}/{context.MaxAttempts})",
            ShouldRetry = true
        };
    }

    private RecoveryPlan PlanGenericRetry(RecoveryContext context)
    {
        if (context.AttemptNumber >= context.MaxAttempts)
            return new RecoveryPlan
            {
                Strategy = RecoveryStrategy.Replan,
                RecoverySteps = new List<AgentStep>(),
                Reason = $"Action failed after {context.MaxAttempts} attempts, need to replan approach",
                ShouldRetry = false
            };

        var steps = new List<AgentStep>
        {
            new()
            {
                Thought = $"Retrying failed action: {context.FailedAction}",
                Action = context.FailedAction,
                Args = context.FailedArgs ?? new Dictionary<string, string>()
            }
        };

        return new RecoveryPlan
        {
            Strategy = RecoveryStrategy.Retry,
            RecoverySteps = steps,
            Reason = $"Action failed ({context.FailureReason}), retrying (attempt {context.AttemptNumber}/{context.MaxAttempts})",
            ShouldRetry = true
        };
    }

    private RecoveryPlan PlanEscalation(RecoveryContext context)
    {
        var isDangerous = DangerousActions.Contains(context.FailedAction);

        if (isDangerous)
        {
            return new RecoveryPlan
            {
                Strategy = RecoveryStrategy.HumanReview,
                RecoverySteps = new List<AgentStep>(),
                Reason = $"Dangerous action '{context.FailedAction}' failed after {context.MaxAttempts} attempts, requires human review",
                ShouldRetry = false
            };
        }

        return new RecoveryPlan
        {
            Strategy = RecoveryStrategy.Replan,
            RecoverySteps = new List<AgentStep>(),
            Reason = $"Action failed after {context.MaxAttempts} attempts, need to replan with a different approach",
            ShouldRetry = false
        };
    }
}
