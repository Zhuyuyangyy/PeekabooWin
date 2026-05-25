# Demo 12: Dialog Confirm Skill Cross-Window

## Scenario
A "close dialog" skill can close different dialogs (save dialog / error dialog / confirmation dialog) using a common Scope with WindowType.Dialog and RequiredAnchors=["close_btn"].

**Goal:** Verify that the same dialog-closing skill works across different dialog types.

---

## Trace Log

### Step 1: skill-search-context for "关闭确认弹窗" with window="另存为"

```
> PeekabooWin.Cli.exe skill-search-context --task "关闭确认弹窗" --window "另存为"
```

**AppProfile Built:**
```
AppProfile:
  AppName: "msedge"
  ProcessName: "msedge.exe"
  WindowTitle: "另存为"
  WindowType: Dialog
  InputMode: ButtonClick
  RiskDomain: Safe
  VisibleTextHints: ["保存", "取消", "×", "关闭"]
  AnchorCandidates: ["cancel_btn", "close_btn"]
```

### Step 2: SkillSearch finds dialog-closing skill

```
Search results for "关闭确认弹窗":
  Candidate 1: vs_dialog_close
    AppPattern: "*"
    Scope:
      SupportedApps: ["*"]
      SupportedWindowTypes: [Dialog]
      RequiredAnchors: ["close_btn"]
      ForbiddenDomains: []
      MinRiskLevel: "L0"
    Score: AppMatch=1.0, TextMatch=0.75, Total=0.88
```

### Step 3: SkillScopeValidator.ValidateMigration check

```
ValidateMigration(scope, appProfile, signature):
  ✓ App 'msedge' matches wildcard '*'
  ✓ WindowType Dialog is in SupportedWindowTypes [Dialog]
  ✓ RequiredAnchors ["close_btn"] found in AnchorCandidates — found "×" and "关闭"
  ✓ RiskDomain Safe NOT in ForbiddenDomains []
  → Result: VALID (null)
```

### Step 4: VACP executes

```
VACP Execute闭环:
  Task: "关闭确认弹窗"
  
  Grounding: Element "×" / "关闭" matched via AnchorMapping → GroundingScore=0.92
  Action: click at close button coordinates
  Verification: window no longer visible → SUCCESS
```

### Step 5: Now test same skill in a DIFFERENT dialog (Error Dialog)

```
> PeekabooWin.Cli.exe skill-search-context --task "关闭错误弹窗" --window "错误"
```

**AppProfile Built:**
```
AppProfile:
  WindowType: Dialog
  RiskDomain: Safe
  VisibleTextHints: ["确定", "错误", "×"]
  AnchorCandidates: ["confirm_btn", "close_btn"]
```

**SkillScopeValidator.ValidateMigration:**
```
  ✓ WindowType Dialog in SupportedWindowTypes
  ✓ RequiredAnchors ["close_btn"] found: "×" matches via AnchorMapping
  → Result: VALID (null)
```

Same skill validated and used for a different dialog type.

---

## Key Evidence

| Check | Expected | Actual | Pass |
|-------|----------|--------|------|
| Dialog WindowType detected | Dialog | Dialog | ✓ |
| RequiredAnchors match | close_btn found | "×"/"关闭" found | ✓ |
| Scope validation passed both dialog types | true | true | ✓ |
| VACP clicked correct button | true | true | ✓ |
| Dialog closed | true | true | ✓ |

---

## Conclusion

Demo 12 **PASSED**: The dialog-closing skill successfully migrated across different dialog types (Save Dialog, Error Dialog). SkillScope with WindowType.Dialog + RequiredAnchors=["close_btn"] correctly generalized, and AnchorMapping mapped logical `close_btn` to actual OCR text ("×", "关闭") in each case.
