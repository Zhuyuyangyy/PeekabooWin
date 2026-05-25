# Demo 11: Cross-App Text Input Migration (Notepad → Doubao Web)

## Scenario
First run in Notepad (text input skill stored). Second run: Doubao Web — same "text input" goal.

**Goal:** Verify that a skill learned in one app (Notepad) can safely migrate to another app (Doubao Web/Edge) when scope validation passes.

---

## Trace Log

### Step 1: skill-search-context for "在豆包发消息" with window="豆包"

```
> PeekabooWin.Cli.exe skill-search-context --task "在豆包发消息" --window "豆包"
```

**AppProfile Built:**
```
AppProfile:
  AppName: "msedge"
  ProcessName: "msedge.exe"
  WindowTitle: "豆包 AI 助手..."
  WindowType: WebBrowser
  InputMode: Mixed
  RiskDomain: Messaging
  VisibleTextHints: ["说点什么...", "发送", "Ctrl+Enter发送", "输入框"]
  AnchorCandidates: ["input_box", "send_btn"]
```

### Step 2: SkillSearch finds candidates

```
Search results for "在豆包发消息":
  Candidate 1: vs_text_input
    AppPattern: "notepad*,msedge,doubao_web"
    Scope:
      SupportedApps: ["notepad*", "msedge", "doubao_web"]
      SupportedWindowTypes: [Edit, WebBrowser]
      RequiredAnchors: ["input_box"]
      ForbiddenDomains: []
      MinRiskLevel: "L0"
    Score: AppMatch=1.0, TextMatch=0.85, Total=0.91
```

### Step 3: SkillScopeValidator.ValidateMigration check

```
ValidateMigration(scope, appProfile, signature):
  ✓ App 'msedge' matches pattern 'msedge' in SupportedApps
  ✓ WindowType WebBrowser is in SupportedWindowTypes [Edit, WebBrowser]
  ✓ RequiredAnchors ["input_box"] found in AnchorCandidates — match ratio 100%
  ✓ RiskDomain Messaging NOT in ForbiddenDomains []
  ✓ RiskDomain Messaging (Safe) >= MinRiskLevel L0
  → Result: VALID (null)
```

### Step 4: SkillHint injected into VacpRequest

```
SkillHint:
  SuggestedElements: ["input_box", "send_btn"]
  SuggestedActionTypes: ["type", "click"]
  PreferredRiskLevel: "L0"
```

### Step 5: VACP executes with hint

```
VACP Execute闭环:
  Task: "在豆包发消息"
  SkillHint: active — prioritizing elements matching "input_box", "send_btn"
  
  Step 1: Grounding
    Element "输入框/说点什么..." matched via hint → GroundingScore=0.95
  
  Step 2: Action selected
    ActionType: type
    TargetElement: "说点什么..." (visible text matched via AnchorMapping)
  
  Step 3: Risk gate → PASS (L0, Messaging)
  
  Step 4: Execute type "你好豆包" → click input_box → type
  
  Step 5: Verification → Text found at coordinates → SUCCESS
```

### Step 6: AfterSuccess → SkillScope auto-extracted

```
Skill extracted from trace:
  SkillId: "vs_text_input_autogen_<timestamp>"
  Name: "web_text_input"
  AppPattern: "msedge,doubao_web"
  Scope:
    SupportedApps: ["msedge", "doubao_web"]
    SupportedWindowTypes: [WebBrowser]
    RequiredAnchors: ["input_box"]
    ForbiddenDomains: []
    MinRiskLevel: "L0"
```

---

## Key Evidence

| Check | Expected | Actual | Pass |
|-------|----------|--------|------|
| AppProfile built from window | msedge/Doubao Web | msedge/Doubao Web | ✓ |
| SkillSearch found cross-app skill | vs_text_input | vs_text_input | ✓ |
| App 'msedge' in scope SupportedApps | true | true | ✓ |
| RequiredAnchors match >= 50% | >= 0.5 | 1.0 | ✓ |
| SkillScopeValidator result | null (valid) | null | ✓ |
| SkillHint injected | true | true | ✓ |
| VACP executed with hint | true | true | ✓ |
| SkippedBySkill | false | false | ✓ |
| AfterSuccess called | true | true | ✓ |

---

## Conclusion

Demo 11 **PASSED**: Cross-app migration from Notepad (Edit) to Doubao Web (WebBrowser) succeeded. The skill's Scope allowed the migration, AnchorMapping correctly mapped `input_box` to `说点什么...`, and VACP executed with the injected hint. SkippedBySkill remained false throughout — VACP was never bypassed.
