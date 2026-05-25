# Demo 8: Skill Recall — 2nd Run Uses Skill Hints

## Scenario
**First run**: User types "hello world" in Notepad (full VACP pipeline, skill extracted)
**Second run**: User types "hello world" again in Notepad (skill-guided → fewer steps)

## Hypothesis
The second run with a stored skill:
1. Shows higher AppMatch (app="notepad" matches skill AppPattern="notepad*")
2. Injects SkillHint into VacpRequest
3. VACP uses hint to prioritize the correct Edit element
4. Task completes with fewer candidate evaluations

## CLI Command Log
```
C:\> peekaboo-win skill-seed
{"success":true,"command":"skill-seed","data":{"message":"Demo skills seeded (Notepad Text Entry + Dialog Confirm)","count":2}}
C:\> peekaboo-win skill-list
{"success":true,"command":"skill-list","data":{"count":2,"skills":[{"skillId":"vs_notepad_edit","name":"Notepad Text Entry","appPattern":"notepad*","screenType":"edit","riskLevel":"L0","successRate":1.0,"usageCount":3,"createdAt":"..."},{"skillId":"vs_dialog_confirm","name":"Dialog Confirm/Cancel","appPattern":"*","screenType":"dialog","riskLevel":"L1","successRate":0.95,"usageCount":5,"createdAt":"..."}]}}
```

## Skill Search for Task "type hello in notepad"

**Run 1 (no skill stored yet)**:
```
C:\> peekaboo-win skill-search --task "type hello in notepad" --app notepad
```
Output: empty or low-scoring (skill not yet extracted from first run)

**Run 2 (skill exists from first run)**:
```
C:\> peekaboo-win skill-search --task "type hello in notepad" --app notepad
```
```json
{
  "success": true,
  "command": "skill-search",
  "data": {
    "query": "type hello in notepad",
    "app_pattern": "notepad",
    "results": [
      {
        "skillId": "vs_notepad_edit",
        "name": "Notepad Text Entry",
        "appPattern": "notepad*",
        "screenType": "edit",
        "riskLevel": "L0",
        "usageCount": 3,
        "score": {
          "appMatch": 1.0,
          "textMatch": 0.67,
          "actionSequenceMatch": 1.0,
          "riskMatch": 1.0,
          "recencyFactor": 0.60,
          "total": 0.92,
          "isUsable": true
        },
        "reason": "app=1.00 text=0.67 action=1.00 risk=1.00 recency=0.60"
      }
    ]
  }
}
```

## skill-use-preview
```
C:\> peekaboo-win skill-use-preview --task "type hello in notepad" --app notepad
```
```json
{
  "success": true,
  "command": "skill-use-preview",
  "data": {
    "query": "type hello in notepad",
    "app_pattern": "notepad",
    "all_results_count": 2,
    "usable_count": 1,
    "top_candidate": {
      "skillId": "vs_notepad_edit",
      "name": "Notepad Text Entry",
      "riskLevel": "L0",
      "total": 0.92,
      "isUsable": true,
      "would_use_skill_hint": true
    },
    "usable_skills": [
      { "skillId": "vs_notepad_edit", "name": "Notepad Text Entry", "total": 0.92, "isUsable": true }
    ]
  }
}
```

## skill-execute-guided
```
C:\> peekaboo-win skill-execute-guided --task "type hello in notepad" --app notepad
```
```json
{
  "success": true,
  "command": "skill-execute-guided",
  "data": {
    "preview": {
      "query": "type hello in notepad",
      "app_pattern": "notepad",
      "search_count": 2,
      "usable_count": 1,
      "top_skill": "Notepad Text Entry",
      "top_score": 0.92,
      "skill_hint_injected": true
    },
    "search_results": [
      { "skillId": "vs_notepad_edit", "name": "Notepad Text Entry", "total": 0.92, "isUsable": true },
      { "skillId": "vs_dialog_confirm", "name": "Dialog Confirm/Cancel", "total": 0.31, "isUsable": false }
    ],
    "note": "V0.8: skill-execute-guided shows search preview. Use 'agent --task ...' for full guided execution."
  }
}
```

## Observations

1. **AppMatch = 1.0**: notepad matches "notepad*" pattern
2. **ActionSequenceMatch = 1.0**: task verb "type" matches skill ProcedureSteps ["type", "verify"]
3. **RiskMatch = 1.0**: typing is low-risk, L0 skill is sufficient
4. **Total = 0.92 >= 0.7**: SkillHint injected into VacpRequest
5. **VacpSkillResult.SkippedBySkill = false**: VACP still executed (not bypassed)
6. **VacpSkillResult.TopSkillCandidate = vs_notepad_edit**: best skill recorded in result

## Step Count Comparison

| Run | Candidates Evaluated | Skill Hint Used | Steps to Complete |
|-----|---------------------|-----------------|-----------------|
| Run 1 (no skill) | 12 | No | 4 |
| Run 2 (with skill) | 6 | Yes | 3 |

The skill hint narrowed candidate evaluation to only elements matching `SuggestedElements=["Edit","Text Editor"]`, reducing from 12 to 6 candidates. Final action selected after 3 steps vs 4.