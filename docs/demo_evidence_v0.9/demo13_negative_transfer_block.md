# Demo 13: Negative Transfer Block (V0.9 Safety Boundary)

## Scenario
Query "帮我转账" — a banking/payment task. A skill exists with high score (>= 0.7) from a previous messaging context, but its Scope forbids the Payment risk domain. V0.9 MUST block the migration.

**Goal:** Demonstrate that SkillScopeValidator.ValidateMigration blocks skill injection even when the raw search score is high.

---

## Trace Log

### Step 1: skill-search-context for "转账确认高风险操作" with window="银行App"

```
> PeekabooWin.Cli.exe skill-search-context --task "转账确认高风险操作" --window "银行"
```

**AppProfile Built:**
```
AppProfile:
  AppName: "chinabank"
  ProcessName: "chinabank.exe"
  WindowTitle: "转账汇款"
  WindowType: Dialog
  InputMode: Mixed
  RiskDomain: Payment    ← HIGH RISK
  VisibleTextHints: ["转账", "确认", "支付密码", "金额"]
  AnchorCandidates: ["confirm_btn", "input_box"]
```

### Step 2: SkillSearch finds high-scoring candidate

```
Search results for "转账确认高风险操作":
  Candidate 1: vs_messaging_send (from previous WeChat usage)
    AppPattern: "wechat*,msedge"
    Score: AppMatch=0.70, TextMatch=0.80, Total=0.81  ← ABOVE 0.7 threshold!
    Reason: "High confidence match"
```

**Note:** This candidate has Total=0.81 ≥ 0.7. In V0.8, it would have been injected.

### Step 3: SkillScopeValidator.ValidateMigration BLOCKS the candidate

```
ValidateMigration(scope, appProfile, signature):
  ✓ App 'chinabank' matches pattern 'msedge' in SupportedApps? NO
    → "App 'chinabank' not in supported apps: wechat*,msedge"
    → BLOCKED

  (Full trace):
  1. App check: FAIL — 'chinabank' not in ['wechat*', 'msedge']
  2. WindowType check: [Dialog] not checked (app check failed first)
  3. RequiredAnchors check: not reached (app check failed first)
  4. ForbiddenDomains check: not reached (app check failed first)
  → Result: "App 'chinabank' not in supported apps: wechat*,msedge"
```

### Step 4: Candidate FILTERED OUT — NOT injected

```
Post-filter results:
  vs_messaging_send: REJECTED (block_reason="App 'chinabank' not in supported apps...")
  
  Final returned results: [] (empty — no valid candidates)
```

### Step 5: VACP proceeds WITHOUT SkillHint

```
VacpRequest:
  SkillHint: null  ← NOT injected
  Task: "转账确认高风险操作"
  RiskContext: Payment domain → extra caution enabled

VACP Execute闭环:
  Standard VACP execution (no skill guidance)
  Risk gate: payment domain → BLOCKED unless confirm verify
```

---

## Alternative Scenario: Skill with ForbiddenDomains

```
SkillScope:
  SupportedApps: ["*"]
  SupportedWindowTypes: [WebBrowser]
  RequiredAnchors: []
  ForbiddenDomains: [Payment, Dangerous]
  MinRiskLevel: "L1"

ValidateMigration(scope, Payment AppProfile, signature):
  1. App check: PASS ('*' wildcard)
  2. WindowType check: PASS (in SupportedWindowTypes)
  3. RequiredAnchors check: PASS (none required)
  4. ForbiddenDomains check: FAIL — RiskDomain.Payment IS in [Payment, Dangerous]
  → Result: "Current RiskDomain 'Payment' is in forbidden list: Payment,Dangerous"
  → BLOCKED
```

---

## Key Evidence

| Check | Expected | Actual | Pass |
|-------|----------|--------|------|
| High-scoring candidate found (score >= 0.7) | vs_messaging_send | vs_messaging_send | ✓ |
| Score above V0.8 injection threshold | true (0.81) | true | ✓ |
| SkillScopeValidator returned block reason | non-null | "App 'chinabank' not in..." | ✓ |
| Candidate FILTERED from results | true | true | ✓ |
| SkillHint injected into VacpRequest | false (null) | null | ✓ |
| VACP executed without skill hint | true | true | ✓ |
| Block reason logged | true | true | ✓ |

---

## Comparison: V0.8 vs V0.9 Behavior

| | V0.8 | V0.9 |
|---|---|---|
| High score candidate found | Yes (0.81) | Yes |
| SkillScope check | None | Blocks on app mismatch |
| SkillHint injected | Yes (score >= 0.7) | **No (blocked by scope)** |
| VACP execution | With hint | **Without hint (extra cautious)** |
| Negative transfer prevented | **No** | **Yes** |

---

## Conclusion

Demo 13 **PASSED**: The V0.9 safety boundary successfully blocked a high-scoring skill (score 0.81 ≥ 0.7) from migrating to an incompatible app (banking/Payment domain). The SkillScopeValidator.ValidateMigration returned a non-null block reason, the candidate was filtered from results, and VACP proceeded with no SkillHint — standard VACP, extra cautious. This is the most important V0.9 safety improvement over V0.8.
