# Demo 12 — Dialog Confirmation Skill Transfer

## Scenario
User task: "点击确定按钮"

## WindowSignature fingerprints

### Windows Dialog
```
WindowType: dialog
InputMode: dialog_input
RiskDomain: neutral
Anchors: [ok_btn, cancel_btn]
```

### App1: Doubao AI Chat (external_ai_chat)
```
WindowType: browser
InputMode: web_textbox
RiskDomain: external_ai_chat
```

## Skill: dialog_confirm_skill
```json
{
  "skillId": "dialog_confirm_skill",
  "skillName": "Dialog Confirm",
  "contextAnchors": ["ok_btn"],
  "supportedApps": ["*"],
  "forbiddenDomains": ["payment", "external_ai_chat"],
  "riskLevel": "L1"
}
```

## Transfer Decision Trace

| Step | Check | Result |
|------|-------|--------|
| 1 | SkillScope.AllowsApp(Windows Dialog) | ✅ allowed |
| 2 | SkillScope.AllowsApp(Doubao) | ✅ allowed |
| 3 | NegativeTransferGuard(task="点击确定按钮") | ✅ ALLOW (no risk verb/target) |
| 4 | Anchor coverage on Windows Dialog | ✅ ok_btn found |
| 5 | SkillMatchScore | score=0.82 → **INJECT** |

### Doubao Transfer (Should be BLOCKED)
| Step | Check | Result |
|------|-------|--------|
| 1 | SkillScope.ForbiddenDomains check | ⚠️ external_ai_chat in forbidden |
| 2 | SkillScope.Validate | ❌ DOMAIN_FORBIDDEN: 'external_ai_chat' in forbidden list → **BLOCK** |

## Expected Output
```
[TransferController] Decision: INJECT
  Target: Program Manager (AppId=explorer, WindowType=dialog)
  Score: 0.82
  Reason: APPROVED score=0.820

[TransferController] Decision: BLOCK
  Target: Doubao (AppId=msedge, RiskDomain=external_ai_chat)
  Reason: DOMAIN_FORBIDDEN: risk domain 'external_ai_chat' is in forbidden list
```