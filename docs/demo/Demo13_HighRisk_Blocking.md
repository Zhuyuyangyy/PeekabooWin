# Demo 13 — High-Risk Skill Transfer Blocking

## Scenario
User task: "向银行账号转账 10000 元" (high-risk: payment verb + payment target)

## App: Bank App
```
WindowType: browser
InputMode: web_textbox
RiskDomain: payment
```

## Skill: text_input_skill (L0, neutral domain)
```json
{
  "skillId": "text_input_skill",
  "skillName": "Text Input",
  "riskLevel": "L0",
  "riskDomain": "neutral"
}
```

## Transfer Decision Trace (Expected BLOCK)

| Step | Check | Result |
|------|-------|--------|
| 1 | SkillScopeValidator.Validate | ✅ allowed (no restrictions) |
| 2 | NegativeTransferGuard: HIGH_RISK_VERB | ⚠️ "转账" detected → skill is L0 |
| 3 | GuardResult | ❌ BLOCK: HIGH_RISK_VERB_DETECTED: '转账' in task, skill is L0 |
| 4 | SuggestedAction | BLOCK |

## Expected Output
```
[NegativeTransferGuard] Evaluate:
  TaskText: "向银行账号转账 10000 元"
  SkillRiskLevel: L0
  AppRiskDomain: payment
  Result: IsAllowed=false
  BlockReason: HIGH_RISK_VERB_DETECTED: '转账' in task, but skill is L0
  BlockedBecause: skill_too_weak_for_risk
  SuggestedAction: BLOCK

[TransferController] Decision: BLOCK
  Target: bank_app (RiskDomain=payment)
  Reason: HIGH_RISK_VERB_DETECTED: '转账' in task, skill is L0
```

## Second Test: L2 Skill on Same Task

## Skill: bank_transfer_skill (L2)
```json
{
  "skillId": "bank_transfer_skill",
  "skillName": "Bank Transfer",
  "riskLevel": "L2",
  "riskDomain": "payment"
}
```

| Step | Check | Result |
|------|-------|--------|
| 1 | NegativeTransferGuard: verb check | ⚠️ "转账" found but skill is L2 → proceeds |
| 2 | SkillMatchScore | score=0.85 → **INJECT** |

## Expected Output
```
[NegativeTransferGuard] Evaluate:
  SkillRiskLevel: L2
  Result: IsAllowed=true (L2 skill clears verb check)

[TransferController] Decision: INJECT
  Score: 0.85
  Reason: APPROVED score=0.850
```