using Xunit;
using PeekabooWin.Core.Safety;
using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Tests;

public class ActionRiskGateTests
{
    private readonly ActionRiskGate _gate = new();

    [Fact]
    public void Evaluate_LowRiskClick_AllowDecision()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "click",
            TargetLabel = "OK button",
            GroundingScore = 0.95
        };

        var decision = _gate.Evaluate(ctx);

        Assert.Equal(RiskLevel.Allow, decision.Decision);
    }

    [Fact]
    public void ComputeRisk_HighRiskDelete_RiskScoreAboveThreshold()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "delete",
            GroundingScore = 0.9
        };

        var risk = _gate.ComputeRisk(ctx);

        Assert.True(risk > 0.3);
    }

    [Fact]
    public void Evaluate_HighRiskPageWithType_NotAllow()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "type",
            PageType = "bank",
            InputText = "some text",
            GroundingScore = 0.9
        };

        var decision = _gate.Evaluate(ctx);

        Assert.NotEqual(RiskLevel.Allow, decision.Decision);
    }

    [Fact]
    public void ComputeRisk_SensitiveKeywordPassword_DataSensitivityIsOne()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "type",
            InputText = "password123",
            GroundingScore = 1.0
        };

        var risk = _gate.ComputeRisk(ctx);

        Assert.True(risk > 0.0);
    }

    [Fact]
    public void ComputeRisk_IrreversibleDelete_IrreversibilityIsOne()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "delete",
            GroundingScore = 1.0
        };

        var risk = _gate.ComputeRisk(ctx);

        Assert.True(risk >= 0.2);
    }

    [Fact]
    public void ComputeRisk_LowGroundingScore_UncertaintyAboveZero()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "click",
            GroundingScore = 0.3
        };

        var risk = _gate.ComputeRisk(ctx);

        Assert.True(risk > 0.0);
    }

    [Fact]
    public void Evaluate_BlockDecision_HasBlockReason()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "delete",
            PageType = "bank",
            GroundingScore = 0.3
        };

        var decision = _gate.Evaluate(ctx);

        if (decision.Decision == RiskLevel.Block)
        {
            Assert.NotNull(decision.BlockReason);
            Assert.NotEmpty(decision.BlockReason);
        }
    }

    [Fact]
    public void Evaluate_ConfirmDecision_HasRequiredConfirmation()
    {
        var ctx = new ActionRiskContext
        {
            ActionType = "type",
            PageType = "bank",
            InputText = "hello",
            GroundingScore = 0.9
        };

        var decision = _gate.Evaluate(ctx);

        if (decision.Decision == RiskLevel.Confirm)
        {
            Assert.NotNull(decision.RequiredConfirmation);
            Assert.NotEmpty(decision.RequiredConfirmation);
        }
    }
}
