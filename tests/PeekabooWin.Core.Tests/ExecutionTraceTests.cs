using System.Text.Json;
using Xunit;
using PeekabooWin.Core.Trace;

namespace PeekabooWin.Core.Tests;

public class ExecutionTraceTests
{
    [Fact]
    public void ExecutionTrace_DefaultValues_AreExpected()
    {
        var trace = new ExecutionTrace();

        Assert.Equal("", trace.TraceId);
        Assert.False(trace.Success);
        Assert.Equal("ALLOW", trace.Decision);
        Assert.Equal("L0", trace.RiskLevel);
        Assert.Equal("", trace.Task);
        Assert.Equal("", trace.ParserMode);
        Assert.False(trace.LlmEnabled);
        Assert.Equal("", trace.FallbackReason);
        Assert.Equal(0.0, trace.GroundingScore);
        Assert.Null(trace.Error);
        Assert.False(trace.Cancelled);
        Assert.False(trace.TimeoutTriggered);
        Assert.Equal(0, trace.TimeoutMs);
        Assert.Equal(0, trace.TotalSteps);
        Assert.Equal(0, trace.SuccessfulSteps);
        Assert.Equal(0, trace.FailedSteps);
        Assert.Equal(0, trace.BlockedSteps);
        Assert.Equal(0, trace.RecoveryAttempts);
    }

    [Fact]
    public void StepTrace_DefaultValues_AreExpected()
    {
        var step = new StepTrace();

        Assert.Equal(0, step.StepIndex);
        Assert.Equal("", step.Action);
        Assert.Null(step.Args);
        Assert.Equal("", step.Thought);
        Assert.False(step.Success);
        Assert.Null(step.Error);
        Assert.Null(step.Result);
        Assert.Null(step.RiskGate);
        Assert.Null(step.Verification);
        Assert.Null(step.Recovery);
        Assert.Null(step.CandidateRanking);
        Assert.Null(step.ExecutedAt);
        Assert.Equal(0, step.LatencyMs);
    }

    [Fact]
    public void RiskGateTrace_DefaultDecision_IsAllow()
    {
        var riskGate = new RiskGateTrace();

        Assert.Equal("ALLOW", riskGate.Decision);
        Assert.Equal(0.0, riskGate.RiskScore);
        Assert.Null(riskGate.BlockReason);
        Assert.Null(riskGate.RequiredConfirmation);
    }

    [Fact]
    public void VerificationTrace_DefaultStatus_IsEmptyString()
    {
        var verification = new VerificationTrace();

        Assert.Equal("", verification.Status);
        Assert.Equal("", verification.Reason);
        Assert.Equal(0.0, verification.Confidence);
    }

    [Fact]
    public void RecoveryTrace_DefaultStrategy_IsEmptyString()
    {
        var recovery = new RecoveryTrace();

        Assert.Equal("", recovery.Strategy);
        Assert.False(recovery.ShouldRetry);
        Assert.Equal(0, recovery.RecoveryStepCount);
    }

    [Fact]
    public void CandidateRankTrace_DefaultHasViableCandidate_IsFalse()
    {
        var ranking = new CandidateRankTrace();

        Assert.False(ranking.HasViableCandidate);
        Assert.Equal(0, ranking.TotalCandidates);
        Assert.Equal(0.0, ranking.BestScore);
        Assert.Equal("", ranking.BestText);
        Assert.Equal("", ranking.BestSource);
    }

    [Fact]
    public void ExecutionTrace_CanBeSerializedToJsonAndBack()
    {
        var trace = new ExecutionTrace
        {
            TraceId = "trace-001",
            Task = "click Save",
            Success = true,
            Decision = "CONFIRM",
            RiskLevel = "L1",
            GroundingScore = 0.92,
            TotalSteps = 3,
            SuccessfulSteps = 2,
            FailedSteps = 1,
            BlockedSteps = 0,
            RecoveryAttempts = 1,
            TimeoutMs = 5000,
            ParserMode = "rule_based",
            LlmEnabled = true,
            FallbackReason = ""
        };

        var json = JsonSerializer.Serialize(trace);
        var deserialized = JsonSerializer.Deserialize<ExecutionTrace>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("trace-001", deserialized!.TraceId);
        Assert.Equal("click Save", deserialized.Task);
        Assert.True(deserialized.Success);
        Assert.Equal("CONFIRM", deserialized.Decision);
        Assert.Equal("L1", deserialized.RiskLevel);
        Assert.Equal(0.92, deserialized.GroundingScore);
        Assert.Equal(3, deserialized.TotalSteps);
        Assert.Equal(2, deserialized.SuccessfulSteps);
        Assert.Equal(1, deserialized.FailedSteps);
        Assert.Equal(0, deserialized.BlockedSteps);
        Assert.Equal(1, deserialized.RecoveryAttempts);
        Assert.Equal(5000, deserialized.TimeoutMs);
    }

    [Fact]
    public void StepTrace_CanHaveRiskGateAndVerificationSubTraces()
    {
        var step = new StepTrace
        {
            StepIndex = 1,
            Action = "click",
            Thought = "Clicking Save button",
            Success = true,
            RiskGate = new RiskGateTrace
            {
                Decision = "ALLOW",
                RiskScore = 0.1,
                BlockReason = null,
                RequiredConfirmation = null
            },
            Verification = new VerificationTrace
            {
                Status = "Passed",
                Reason = "Element state changed",
                Confidence = 0.95
            },
            Recovery = new RecoveryTrace
            {
                Strategy = "Retry",
                ShouldRetry = true,
                RecoveryStepCount = 1
            },
            CandidateRanking = new CandidateRankTrace
            {
                TotalCandidates = 3,
                BestScore = 0.88,
                BestText = "Save",
                BestSource = "uia",
                HasViableCandidate = true
            }
        };

        Assert.NotNull(step.RiskGate);
        Assert.Equal("ALLOW", step.RiskGate.Decision);
        Assert.Equal(0.1, step.RiskGate.RiskScore);

        Assert.NotNull(step.Verification);
        Assert.Equal("Passed", step.Verification.Status);
        Assert.Equal(0.95, step.Verification.Confidence);

        Assert.NotNull(step.Recovery);
        Assert.Equal("Retry", step.Recovery.Strategy);
        Assert.True(step.Recovery.ShouldRetry);

        Assert.NotNull(step.CandidateRanking);
        Assert.Equal(3, step.CandidateRanking.TotalCandidates);
        Assert.True(step.CandidateRanking.HasViableCandidate);
    }

    [Fact]
    public void ExecutionTrace_StepTraces_DefaultIsEmptyList()
    {
        var trace = new ExecutionTrace();

        Assert.NotNull(trace.StepTraces);
        Assert.Empty(trace.StepTraces);
    }

    [Fact]
    public void ExecutionTrace_StepTraces_CanBePopulatedAndSerialized()
    {
        var trace = new ExecutionTrace
        {
            TraceId = "trace-002",
            StepTraces = new List<StepTrace>
            {
                new() { StepIndex = 0, Action = "focus-window", Success = true },
                new() { StepIndex = 1, Action = "click", Success = false, RiskGate = new RiskGateTrace { Decision = "BLOCK", RiskScore = 0.8 } }
            }
        };

        var json = JsonSerializer.Serialize(trace);
        var deserialized = JsonSerializer.Deserialize<ExecutionTrace>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.StepTraces.Count);
        Assert.Equal("focus-window", deserialized.StepTraces[0].Action);
        Assert.Equal("click", deserialized.StepTraces[1].Action);
        Assert.NotNull(deserialized.StepTraces[1].RiskGate);
        Assert.Equal("BLOCK", deserialized.StepTraces[1].RiskGate!.Decision);
    }
}
