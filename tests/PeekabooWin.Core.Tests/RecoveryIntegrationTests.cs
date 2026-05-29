using Xunit;
using PeekabooWin.Core.Agent;

namespace PeekabooWin.Core.Tests;

public class RecoveryIntegrationTests
{
    private readonly RecoveryPlanner _planner = new();

    [Fact]
    public void WindowLostFocus_FirstAttempt_StrategyIsRefocusWithFocusWindowStep()
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
        Assert.Contains(plan.RecoverySteps, s => s.Action == "focus-window");
        Assert.True(plan.ShouldRetry);
    }

    [Fact]
    public void WindowLostFocus_MaxAttempts_StrategyIsHumanReview()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click",
            WindowLostFocus = true,
            WindowTitle = "Notepad",
            AttemptNumber = 3,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.HumanReview, plan.Strategy);
        Assert.False(plan.ShouldRetry);
    }

    [Fact]
    public void ElementNotFound_FirstAttempt_StrategyIsRelocateWithOcrClickStep()
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

        Assert.Equal(RecoveryStrategy.Relocate, plan.Strategy);
        Assert.Contains(plan.RecoverySteps, s => s.Action == "ocr-click");
        Assert.True(plan.ShouldRetry);
    }

    [Fact]
    public void ElementNotFound_MaxAttempts_NonDangerousAction_StrategyIsReplan()
    {
        var context = new RecoveryContext
        {
            FailedAction = "screenshot",
            ElementNotFound = true,
            AttemptNumber = 3,
            MaxAttempts = 3
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.Replan, plan.Strategy);
        Assert.False(plan.ShouldRetry);
    }

    [Fact]
    public void TimeoutOccurred_FirstAttempt_StrategyIsRetry()
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
        Assert.True(plan.ShouldRetry);
    }

    [Fact]
    public void TimeoutOccurred_MaxAttempts_DangerousAction_StrategyIsHumanReview()
    {
        var context = new RecoveryContext
        {
            FailedAction = "click",
            TimeoutOccurred = true,
            AttemptNumber = 2,
            MaxAttempts = 2
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.HumanReview, plan.Strategy);
        Assert.False(plan.ShouldRetry);
    }

    [Fact]
    public void GenericFailure_FirstAttempt_StrategyIsRetryWithSameAction()
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
        Assert.Contains(plan.RecoverySteps, s => s.Action == "screenshot");
        Assert.True(plan.ShouldRetry);
    }

    [Fact]
    public void WindowLostFocus_RefocusPlan_IncludesRetryOfOriginalAction()
    {
        var context = new RecoveryContext
        {
            FailedAction = "type",
            WindowLostFocus = true,
            WindowTitle = "Calculator",
            AttemptNumber = 1,
            MaxAttempts = 3,
            FailedArgs = new Dictionary<string, string> { ["text"] = "42" }
        };

        var plan = _planner.PlanRecovery(context);

        Assert.Equal(RecoveryStrategy.Refocus, plan.Strategy);
        Assert.Contains(plan.RecoverySteps, s => s.Action == "focus-window");
        Assert.Contains(plan.RecoverySteps, s => s.Action == "type");
    }

    [Fact]
    public void RecoveryContext_DefaultMaxAttempts_IsTwo()
    {
        var context = new RecoveryContext();

        Assert.Equal(2, context.MaxAttempts);
    }
}
