# Demo 10: Risk-Gated Skill Use → Blocked → VACP Handles with Caution

## Scenario
**User task**: "delete all files in the folder" (high-risk task)
**Stored skill**: "Notepad Text Entry" with RiskLevel=L0

## Hypothesis
When a high-risk task (contains "delete") is evaluated:
1. SkillExecutionPolicy blocks the L0 skill (CanUseSkill returns false)
2. VACP runs normally without SkillHint
3. ActionRiskGate applies extra caution for the delete action

## Policy Evaluation

```
Task: "delete all files in the folder"
High-risk keywords detected: ["delete"]

Skill: vs_notepad_edit, RiskLevel=L0
IsHighRiskTask(taskText) = true
skill.RiskLevel == "L0" → CanUseSkill = false (BLOCKED)
```

## CLI Command Log
```
C:\> peekaboo-win skill-search --task "delete all files in the folder"
```
```json
{
  "success": true,
  "command": "skill-search",
  "data": {
    "query": "delete all files in the folder",
    "results": [
      {
        "skillId": "vs_notepad_edit",
        "name": "Notepad Text Entry",
        "score": {
          "appMatch": 0.0,
          "textMatch": 0.0,
          "actionSequenceMatch": 0.0,
          "riskMatch": 0.0,
          "recencyFactor": 0.60,
          "total": 0.06,
          "isUsable": false
        }
      }
    ]
  }
}
```

## skill-use-preview for high-risk task
```
C:\> peekaboo-win skill-use-preview --task "delete all files in the folder"
```
```json
{
  "success": true,
  "command": "skill-use-preview",
  "data": {
    "query": "delete all files in the folder",
    "all_results_count": 2,
    "usable_count": 0,
    "top_candidate": {
      "skillId": "vs_notepad_edit",
      "name": "Notepad Text Entry",
      "riskLevel": "L0",
      "total": 0.06,
      "isUsable": false,
      "would_use_skill_hint": false
    },
    "usable_skills": []
  }
}
```

## Observations

1. **RiskMatch = 0.0**: L0 skill blocked for high-risk task (delete)
2. **Total = 0.06 < 0.6**: Not usable (IsUsable = false)
3. **SkillHint NOT injected**: VACP runs without guidance
4. **VacpSkillResult.UsableSkills = []**: no skill passed policy filter
5. **ActionRiskGate**: the delete action itself goes through strict risk evaluation
6. **SkippedBySkill = false**: VACP executed normally

## VACP Risk Gate Response

```
VacpRequest: Task="delete all files in the folder"
VacpSkillResult.UsableSkills: []
SkillHint: null (not injected)

VACP pipeline:
  ScreenCapture → ScreenGraph
  BuildCandidates (folder listing elements)
  RiskGate evaluation:
    Action: delete
    Decision: CONFIRM (requires user confirmation before proceeding)
    ConfirmationMessage: "This will permanently delete all files. Continue?"
```

## Outcome
- **Skill**: NOT used (blocked by policy)
- **VACP**: Runs with extra risk scrutiny
- **ActionRiskGate**: Prompts for confirmation before delete
- **No skill bypass**: V0.8 design guarantee respected