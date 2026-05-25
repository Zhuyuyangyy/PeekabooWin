using System.Collections.Generic;

namespace PeekabooWin.Core.Memory;

/// <summary>
/// Hint injected into VacpRequest to guide VACP planning toward skill-proven elements/actions.
/// V0.8 Skill-Guided Execution.
/// </summary>
public class SkillHint
{
    /// <summary>
    /// Element labels extracted from the matched skill's successful trace.
    /// VACP uses these to prioritize grounded candidates.
    /// </summary>
    public List<string> SuggestedElements { get; set; } = new();

    /// <summary>
    /// Action types from the skill's procedure steps.
    /// VACP uses these to narrow action-type ranking.
    /// </summary>
    public List<string> SuggestedActionTypes { get; set; } = new();

    /// <summary>
    /// Preferred risk level from the skill (L0/L1/L2).
    /// </summary>
    public string PreferredRiskLevel { get; set; } = "L0";
}