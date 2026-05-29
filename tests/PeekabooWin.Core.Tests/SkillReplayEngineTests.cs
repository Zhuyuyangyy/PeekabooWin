using Xunit;
using PeekabooWin.Core.Memory;

namespace PeekabooWin.Core.Tests;

public class SkillReplayEngineTests
{
    [Fact]
    public void VisualSkill_Creation_HasPropertyDefaults()
    {
        var skill = new VisualSkill();

        Assert.NotEmpty(skill.SkillId);
        Assert.Equal("", skill.Name);
        Assert.Equal("", skill.AppPattern);
        Assert.Equal("", skill.ScreenType);
        Assert.Empty(skill.ProcedureSteps);
        Assert.Equal("L0", skill.RiskLevel);
        Assert.Equal("neutral", skill.RiskDomain);
        Assert.Equal(1.0, skill.SuccessRate);
        Assert.Equal(0, skill.UsageCount);
    }

    [Fact]
    public void VisualSkill_RecordUsageSuccess_IncrementsBothCounts()
    {
        var skill = new VisualSkill();

        skill.RecordUsage(true);

        Assert.Equal(1, skill.UsageCount);
        Assert.Equal(1.0, skill.SuccessRate);
    }

    [Fact]
    public void VisualSkill_RecordUsageFailure_IncrementsUsageOnly()
    {
        var skill = new VisualSkill();

        skill.RecordUsage(false);

        Assert.Equal(1, skill.UsageCount);
        Assert.Equal(0.0, skill.SuccessRate);
    }

    [Fact]
    public void SkillReplayReport_DefaultValues_AreCorrect()
    {
        var report = new SkillReplayReport();

        Assert.Equal("", report.SkillId);
        Assert.Equal("", report.SkillName);
        Assert.False(report.DryRun);
        Assert.Equal(0, report.StepsTotal);
        Assert.Equal(0, report.StepsExecuted);
        Assert.Equal(0, report.StepsBlocked);
        Assert.False(report.VerificationPassed);
        Assert.Empty(report.StepRecords);
    }

    [Fact]
    public void StepReplayRecord_Properties_SetCorrectly()
    {
        var record = new StepReplayRecord
        {
            StepIndex = 2,
            StepDescription = "click_Save",
            ParsedAction = "click",
            Target = "Save",
            RiskScore = 0.15,
            Executed = true,
            Success = true
        };

        Assert.Equal(2, record.StepIndex);
        Assert.Equal("click_Save", record.StepDescription);
        Assert.Equal("click", record.ParsedAction);
        Assert.Equal("Save", record.Target);
        Assert.Equal(0.15, record.RiskScore);
        Assert.True(record.Executed);
        Assert.True(record.Success);
        Assert.False(record.DryRunSkipped);
        Assert.False(record.RiskBlocked);
        Assert.Null(record.Error);
    }
}
