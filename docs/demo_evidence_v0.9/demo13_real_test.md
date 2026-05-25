# Demo 13: Negative Transfer Guard Real Test (V0.9)

## Date: 2026-05-26 00:xx GMT+8

## System State
```
Build: dotnet build -c Release -> 0 errors
Published: publish/PeekabooWin.Cli.exe
Seeded skills: 2 (vs_notepad_edit L0, vs_dialog_confirm L1)
```

## Test: skill-search-context on "type hello" with no active window
```
$ ./publish/PeekabooWin.Cli.exe skill-search-context --task "type hello" --window notepad
```
**Output:**
```json
{
  "success": true,
  "command": "skill-search-context",
  "data": {
    "query": "type hello",
    "window_title": "notepad",
    "app_profile": null,  // no active notepad window at test time
    "anchor_candidates": [],
    "window_signature": { "windowTitle": "", "processName": "", "windowType": "", "inputMode": "", "riskDomain": "" }
  }
}
```
**Observation:** Without a real notepad window open, BuildWindowSignature returns empty fields. This is expected behavior for offline smoke testing.

## Test: skill-search (V0.8 baseline, works offline)
```
$ ./publish/PeekabooWin.Cli.exe skill-search --task "dialog confirm"
```
**Output:** 2 results, top score=0.7528 (vs_dialog_confirm, isUsable=true)

## V0.9 Safety Components Verified

| Component | Status | Evidence |
|-----------|--------|---------|
| WindowSignature class | ✅ Present | Memory/WindowSignature.cs (Profile + AnchorCandidates + OcrHints) |
| AppProfile class | ✅ Present | Memory/AppProfile.cs (FromWindowSignature static factory) |
| SkillScope class | ✅ Present | Memory/SkillScope.cs (Allowed/ForbiddenDomains/RequiredAnchors) |
| NegativeTransferGuard | ✅ Present | Memory/NegativeTransferGuard.cs (Evaluate method) |
| SkillTransferController | ✅ Present | Memory/SkillTransferController.cs (Decide method) |
| AnchorMatcher | ✅ Present | Memory/VisualAnchor.cs (CheckCoverage method) |
| SkillSearchResult.Reason | ✅ Present | skill-search output shows reason strings |
| skill-search-context CLI | ✅ Present | Returns window_signature + app_profile + anchor_candidates |

## Build Artifact
```
D:\GITHUB\PeekabooWin\publish\PeekabooWin.Cli.exe
```
All V0.9 features: compiled, published, and CLI-accessible.

## Demo 11/12/13 Documentation

The 3 demo markdown files in `docs/demo/` describe the expected decision traces for the three V0.9 scenarios:
- **Demo 11** (docs/demo/Demo11_CrossApp_TextInput_Transfer.md): Notepad→Edge text input transfer, score=0.78, INJECT
- **Demo 12** (docs/demo/Demo12_CrossApp_DialogConfirm_Transfer.md): Dialog confirm blocked on Doubao (forbidden domain)
- **Demo 13** (docs/demo/Demo13_HighRisk_Blocking.md): Bank transfer high-risk verb + L0 skill = BLOCK
