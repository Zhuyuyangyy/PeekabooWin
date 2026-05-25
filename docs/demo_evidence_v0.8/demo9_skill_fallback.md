# Demo 9: OCR First Failure → Skill-Guided Fallback → Success

## Scenario
**First attempt**: User asks to click "Submit" on a web form — OCR fails to recognize the button text in a noisy screenshot
**Second attempt**: Skill from a prior session matches AppPattern=chrome, ScreenType=web-form, ActionSequenceMatch=click → SkillHint guides VACP to the correct button element without relying on OCR text

## Hypothesis
When OCR-based vision fails (low confidence), a stored skill can provide:
1. `SuggestedElements` (element labels from prior successful trace)
2. `SuggestedActionTypes` (action type from ProcedureSteps)

VACP uses these hints to rank candidates even when OCR text is noisy/unreliable.

## CLI Command Log
```
C:\> peekaboo-win skill-seed
> peekaboo-win skill-list
```

## Skill Search for Task "click submit in chrome"
```
C:\> peekaboo-win skill-search --task "click submit in chrome" --app chrome --visible-text "Submi"
```
```json
{
  "success": true,
  "command": "skill-search",
  "data": {
    "query": "click submit in chrome",
    "app_pattern": "chrome",
    "results": [
      {
        "skillId": "vs_chrome_form_submit",
        "name": "Chrome Web Form Submit",
        "appPattern": "chrome*",
        "screenType": "web-form",
        "riskLevel": "L1",
        "usageCount": 4,
        "score": {
          "appMatch": 1.0,
          "textMatch": 0.33,
          "actionSequenceMatch": 1.0,
          "riskMatch": 1.0,
          "recencyFactor": 0.70,
          "total": 0.87,
          "isUsable": true
        },
        "reason": "app=1.00 text=0.33 action=1.00 risk=1.00 recency=0.70"
      }
    ]
  }
}
```

## Observations

1. **textMatch = 0.33** (partial): visible OCR only got "Submi" but skill expects full "Submit"
2. **ActionSequenceMatch = 1.0**: task verb "click" matches ProcedureSteps ["click"]
3. **Total = 0.87 >= 0.7**: SkillHint injected despite weak TextMatch
4. **SkillHint.SuggestedElements = ["Submit", "submit", "button"]**: guides element ranking
5. **SkillHint.SuggestedActionTypes = ["click"]**: confirms action type

## Fallback Flow

```
Standard VACP pipeline:
  Screenshot → OCR "Submi" → low confidence → candidates=[]
  
V0.8 Skill-Guided fallback:
  Screenshot → OCR "Submi" (noisy)
  BUT: SkillHint suggests ["Submit", "submit", "button"]
  VACP re-ranks candidates using hint → Submit button elevated
  Action: click at Submit button coordinates
  Verification: screenshot diff → SUCCESS
```

## Outcome
- **Without skill**: OCR failure → no candidates → task fails
- **With skill**: SkillHint compensates for noisy OCR → task succeeds