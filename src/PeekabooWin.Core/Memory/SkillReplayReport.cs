namespace PeekabooWin.Core.Memory;

public class SkillReplayReport
{
    public string SkillId { get; set; } = "";
    public string SkillName { get; set; } = "";
    public bool DryRun { get; set; }
    public int StepsTotal { get; set; }
    public int StepsExecuted { get; set; }
    public int StepsBlocked { get; set; }
    public bool VerificationPassed { get; set; }
    public List<StepReplayRecord> StepRecords { get; set; } = [];
    public string? TracePath { get; set; }
}

public class StepReplayRecord
{
    public int StepIndex { get; set; }
    public string StepDescription { get; set; } = "";
    public string ParsedAction { get; set; } = "";
    public string? Target { get; set; }
    public bool DryRunSkipped { get; set; }
    public bool RiskBlocked { get; set; }
    public double RiskScore { get; set; }
    public bool Executed { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? BeforeScreenshot { get; set; }
    public string? AfterScreenshot { get; set; }
}
