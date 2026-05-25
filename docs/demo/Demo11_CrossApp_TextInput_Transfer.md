# Demo 11 — Cross-App Text Input Skill Transfer

## Scenario
User task: "在输入框里输入 hello world"

## Supported Apps
- Microsoft Edge (Doubao web chat)
- Notepad
- Any browser with a text input

## WindowSignature fingerprints

### Edge Doubao
```
WindowType: browser
InputMode: web_textbox
RiskDomain: external_ai_chat
Anchors: [input_box, send_btn]
```

### Notepad
```
WindowType: editor
InputMode: edit_field
RiskDomain: neutral
Anchors: [edit_region]
```

## Skill: text_input_skill
```json
{
  "skillId": "text_input_skill",
  "skillName": "Text Input",
  "contextAnchors": ["input_box", "edit_region"],
  "supportedApps": ["edge", "chrome", "notepad", "*"],
  "forbiddenDomains": ["payment", "admin"],
  "riskLevel": "L1"
}
```

## Transfer Decision Trace

| Step | Check | Result |
|------|-------|--------|
| 1 | SkillScope.AllowsApp(Edge) | ✅ allowed |
| 2 | SkillScope.AllowsApp(Notepad) | ✅ allowed |
| 3 | NegativeTransferGuard(task contains "input") | ✅ L1 skill, no risk verb → ALLOW |
| 4 | Anchor coverage on Notepad | ✅ edit_region found |
| 5 | SkillMatchScore | score=0.78 → **INJECT** |

## Expected Output
```
[TransferController] Decision: INJECT
  Skill: text_input_skill
  Target: notepad (AppId=notepad, WindowType=editor)
  Score: 0.78
  Reason: APPROVED score=0.780
```