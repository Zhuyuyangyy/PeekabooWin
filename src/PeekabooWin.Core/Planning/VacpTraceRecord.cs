using System.Text.Json.Serialization;
using PeekabooWin.Core.Perception;

namespace PeekabooWin.Core.Planning;

/// <summary>
/// VACP 执行轨迹记录 — 完整步骤级审计日志
/// 
/// 每次 VACP 闭环执行的每一步都记录为一个 TraceRecord，
/// 用于：可解释性 / 调试 / 复盘 / 学术证据
/// </summary>
public class VacpTraceRecord
{
    [JsonPropertyName("trace_id")]
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = "";

    [JsonPropertyName("task_description")]
    public string TaskDescription { get; set; } = "";

    [JsonPropertyName("step_index")]
    public int StepIndex { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Screenshot paths
    [JsonPropertyName("screenshot_before")]
    public string? ScreenshotBefore { get; set; }

    [JsonPropertyName("screenshot_after")]
    public string? ScreenshotAfter { get; set; }

    // Screen State Graph at this step
    [JsonPropertyName("screen_state_graph")]
    public ScreenStateGraph? ScreenStateGraph { get; set; }

    // All candidates considered
    [JsonPropertyName("candidate_actions")]
    public List<CandidateRecord> CandidateActions { get; set; } = new();

    // The winning action after ranking
    [JsonPropertyName("selected_action")]
    public SelectedActionRecord? SelectedAction { get; set; }

    // Grounding score for the selected element
    [JsonPropertyName("grounding_score")]
    public double GroundingScore { get; set; }

    [JsonPropertyName("grounding_breakdown")]
    public Dictionary<string, double> GroundingBreakdown { get; set; } = new();

    // Risk gate result
    [JsonPropertyName("risk_score")]
    public double RiskScore { get; set; }

    [JsonPropertyName("risk_gate_decision")]
    public string RiskGateDecision { get; set; } = ""; // ALLOW / CONFIRM / BLOCK

    [JsonPropertyName("risk_breakdown")]
    public Dictionary<string, double> RiskBreakdown { get; set; } = new();

    [JsonPropertyName("block_reason")]
    public string? BlockReason { get; set; }

    [JsonPropertyName("confirmation_message")]
    public string? ConfirmationMessage { get; set; }

    // Execution result
    [JsonPropertyName("execution_result")]
    public string ExecutionResult { get; set; } = ""; // SUCCESS / FAILED / BLOCKED

    [JsonPropertyName("execution_message")]
    public string? ExecutionMessage { get; set; }

    [JsonPropertyName("execution_error")]
    public string? ExecutionError { get; set; }

    // Verification result
    [JsonPropertyName("verification_score")]
    public double VerificationScore { get; set; }

    [JsonPropertyName("verification_outcome")]
    public string VerificationOutcome { get; set; } = ""; // SUCCESS / FAILED

    [JsonPropertyName("verification_breakdown")]
    public VerificationBreakdown? VerificationBreakdown { get; set; }

    [JsonPropertyName("recovery_suggestion")]
    public string? RecoverySuggestion { get; set; }

    // Retry info
    [JsonPropertyName("was_retried")]
    public bool WasRetried { get; set; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    // Overall step outcome
    [JsonPropertyName("step_success")]
    public bool StepSuccess { get; set; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }
}

public class CandidateRecord
{
    [JsonPropertyName("candidate_id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("target_label")]
    public string TargetLabel { get; set; } = "";

    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = "";

    [JsonPropertyName("model_score")]
    public double ModelScore { get; set; }

    [JsonPropertyName("grounding_score")]
    public double GroundingScore { get; set; }

    [JsonPropertyName("rank_score")]
    public double RankScore { get; set; }

    [JsonPropertyName("rank_position")]
    public int RankPosition { get; set; }
}

public class SelectedActionRecord
{
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = "";

    [JsonPropertyName("target_element_id")]
    public string? TargetElementId { get; set; }

    [JsonPropertyName("target_label")]
    public string TargetLabel { get; set; } = "";

    [JsonPropertyName("target_coordinates")]
    public CoordinateRecord? TargetCoordinates { get; set; }

    [JsonPropertyName("input_text")]
    public string? InputText { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public class CoordinateRecord
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("center_x")]
    public int CenterX => X;

    [JsonPropertyName("center_y")]
    public int CenterY => Y;
}

public class VerificationBreakdown
{
    [JsonPropertyName("visual_change")]
    public double VisualChange { get; set; }

    [JsonPropertyName("expected_state_match")]
    public double ExpectedStateMatch { get; set; }

    [JsonPropertyName("element_state_change")]
    public double ElementStateChange { get; set; }

    [JsonPropertyName("error_absence")]
    public double ErrorAbsence { get; set; }
}

/// <summary>
/// 汇总一个 Task 的所有 TraceRecord
/// </summary>
public class VacpTaskTrace
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = "";

    [JsonPropertyName("task_description")]
    public string TaskDescription { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("total_steps")]
    public int TotalSteps { get; set; }

    [JsonPropertyName("successful_steps")]
    public int SuccessfulSteps { get; set; }

    [JsonPropertyName("failed_steps")]
    public int FailedSteps { get; set; }

    [JsonPropertyName("blocked_steps")]
    public int BlockedSteps { get; set; }

    [JsonPropertyName("overall_success")]
    public bool OverallSuccess { get; set; }

    [JsonPropertyName("step_traces")]
    public List<VacpTraceRecord> StepTraces { get; set; } = new();

    /// <summary>
    /// 汇总指标
    /// </summary>
    public VacpTaskMetrics ComputeMetrics()
    {
        return new VacpTaskMetrics
        {
            TotalSteps = TotalSteps,
            SuccessfulSteps = SuccessfulSteps,
            FailedSteps = FailedSteps,
            BlockedSteps = BlockedSteps,
            StepSuccessRate = TotalSteps > 0 ? (double)SuccessfulSteps / TotalSteps : 0.0,
            HighRiskBlocked = BlockedSteps,
            AverageVerificationScore = TotalSteps > 0
                ? StepTraces.Where(t => t.VerificationScore > 0).Average(t => t.VerificationScore)
                : 0.0,
        };
    }
}

public class VacpTaskMetrics
{
    public int TotalSteps { get; set; }
    public int SuccessfulSteps { get; set; }
    public int FailedSteps { get; set; }
    public int BlockedSteps { get; set; }
    public double StepSuccessRate { get; set; }
    public int HighRiskBlocked { get; set; }
    public double AverageVerificationScore { get; set; }
}