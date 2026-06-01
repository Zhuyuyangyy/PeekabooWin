using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PeekabooWin.Core.Trace;

public class ExecutionTrace
{
    public string TraceId { get; set; } = "";
    public string Task { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public string ParserMode { get; set; } = "";
    public bool LlmEnabled { get; set; }
    public string FallbackReason { get; set; } = "";
    public string Decision { get; set; } = "ALLOW";
    public string RiskLevel { get; set; } = "L0";
    public double GroundingScore { get; set; }
    public List<StepTrace> StepTraces { get; set; } = new();
    public string? Error { get; set; }
    public bool Cancelled { get; set; }
    public bool TimeoutTriggered { get; set; }
    public int TimeoutMs { get; set; }
    public int TotalSteps { get; set; }
    public int SuccessfulSteps { get; set; }
    public int FailedSteps { get; set; }
    public int BlockedSteps { get; set; }
    public int RecoveryAttempts { get; set; }
}

public class StepTrace
{
    public int StepIndex { get; set; }
    public string Action { get; set; } = "";
    public Dictionary<string, string>? Args { get; set; }
    public string Thought { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Result { get; set; }
    public RiskGateTrace? RiskGate { get; set; }
    public VerificationTrace? Verification { get; set; }
    public RecoveryTrace? Recovery { get; set; }
    public CandidateRankTrace? CandidateRanking { get; set; }
    public TransferDecisionTrace? TransferDecision { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public long LatencyMs { get; set; }
}

public class RiskGateTrace
{
    public string Decision { get; set; } = "ALLOW";
    public double RiskScore { get; set; }
    public string? BlockReason { get; set; }
    public string? RequiredConfirmation { get; set; }
}

public class VerificationTrace
{
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Confidence { get; set; }
}

public class RecoveryTrace
{
    public string Strategy { get; set; } = "";
    public bool ShouldRetry { get; set; }
    public int RecoveryStepCount { get; set; }
}

public class CandidateRankTrace
{
    public int TotalCandidates { get; set; }
    public double BestScore { get; set; }
    public string BestText { get; set; } = "";
    public string BestSource { get; set; } = "";
    public bool HasViableCandidate { get; set; }
}

public class TransferDecisionTrace
{
    public string? SkillId { get; set; }
    public string? SkillName { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? BlockReason { get; set; }
    public double SkillMatchScore { get; set; }
    public double CoverageScore { get; set; } = 1.0;
}
