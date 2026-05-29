using Xunit;
using PeekabooWin.Core.Agent;

namespace PeekabooWin.Core.Tests;

public class RecoveryPlannerTests
{
    private readonly RecoveryPlanner _planner = new();

    [Fact]
    public void PlanRecovery_WindowLostFocus_StrategyIsRefocus()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click",
            WindowLostFocus = true,
            WindowTitle = "Notepad",
            AttemptNumber = 1,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.Refocus, plan.Strategy);
    }

    [Fact]
    public void PlanRecovery_WindowLostFocus_IncludesFocusWindowStep()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click",
            WindowLostFocus = true,
            WindowTitle = "Notepad",
            AttemptNumber = 1,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Contains(plan.RecoverySteps, s => s.Action == "focus-window");
    }

    [Fact]
    public void PlanRecovery_ElementNotFound_StrategyIsRelocate()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click-element",
            ElementNotFound = true,
            AttemptNumber = 1,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.Relocate, plan.Strategy);
    }

    [Fact]
    public void PlanRecovery_ElementNotFound_IncludesOcrClickStep()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click-element",
            ElementNotFound = true,
            AttemptNumber = 1,
            MaxAttempts = 3,
            FailedArgs = new Dictionary<string, string> { ["name"] = "Submit" }
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Contains(plan.RecoverySteps, s => s.Action == "ocr-click");
    }

    [Fact]
    public void PlanRecovery_TimeoutOccurred_StrategyIsRetry()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click",
            TimeoutOccurred = true,
            AttemptNumber = 1,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.Retry, plan.Strategy);
    }

    [Fact]
    public void PlanRecovery_MaxAttemptsExceededDangerousAction_StrategyIsHumanReview()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click",
            AttemptNumber = 3,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.True(plan.Strategy == RecoveryStrategy.HumanReview || plan.Strategy == RecoveryStrategy.Replan || plan.Strategy == RecoveryStrategy.Abort);
    }

    [Fact]
    public void PlanRecovery_GenericFailure_StrategyIsRetry()
    {
        var context = new RecoveryContext
        {
            FailedAction = "screenshot",
            FailureReason = "unknown error",
            AttemptNumber = 1,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.Retry, plan.Strategy);
    }

    [Fact]
    public void PlanRecovery_RecoverableFailure_RecoveryStepsNotEmpty()
    {
        var contexts = new[]
        {
            new RecoveryContext { FailedAction = "click", WindowLostFocus = true, WindowTitle = "Test", AttemptNumber = 1, MaxAttempts = 3 },
            new RecoveryContext { FailedAction = "click-element", ElementNotFound = true, AttemptNumber = 1, MaxAttempts = 3, FailedArgs = new Dictionary<string, string> { ["text"] = "OK" } },
            new RecoveryContext { FailedAction = "click", TimeoutOccurred = true, AttemptNumber = 1, MaxAttempts = 3 },
            new RecoveryContext { FailedAction = "screenshot", AttemptNumber = 1, MaxAttempts = 3 }
        };

        foreach (var context in contexts)
        {
            var plan = _planner.PlanRecovery(context);
            Assert.NotEmpty(plan.RecoverySteps);
        }
    }

    [Fact]
    public void PlanRecovery_MaxAttemptsExceededDangerousAction_StrategyIsHumanReviewSpecifically()
    {
        var context = new RecoveryContext
        {
            FailedAction = "type",
            AttemptNumber = 2,
            MaxAttempts = 2
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.HumanReview, plan.Strategy);
    }

    [Fact]
    public void PlanRecovery_MaxAttemptsExceededNonDangerous_StrategyIsReplan()
    {
        var context = new RecoveryContext
        {
            FailedAction = "screenshot",
            TimeoutOccurred = true,
            AttemptNumber = 2,
            MaxAttempts = 2
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.Replan, plan.Strategy);
    }
}
