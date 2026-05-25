# V0.3 Recognition Enhancement Validation Report

**Status**: Code review completed, Windows build verification pending
**Date**: 2026-05-25

---

## 1. Build Status

**Status**: UNVERIFIED - Requires Windows environment

### Build Commands

```powershell
cd D:\GITHUB\PeekabooWin
dotnet restore .\PeekabooWin.sln
dotnet build .\PeekabooWin.sln -c Release
```

### Issues Fixed During Code Review

| Issue | Severity | Status |
|-------|----------|--------|
| TesseractOcrEngine: `LineCount` field not in OcrResult | High | Fixed - removed LineCount |
| TesseractOcrEngine: unused `args` variable | Low | Fixed - removed |
| Confidence naming "real confidence" | Medium | Fixed - renamed to "estimated" |
| Fuzzy match missing dangerous element check | Medium | Fixed - added IsPotentiallyDangerous |

### Risk Assessment

| Module | Risk Level | Notes |
|--------|------------|-------|
| Windows.Media.Ocr | Low | WinRT package identity available in net8.0-windows10.0.17763.0 |
| TesseractOcrEngine | Medium | Graceful fallback if tesseract.exe not found |
| System.Drawing | Low | UseWindowsForms=true enables full GDI+ |
| OcrPreprocessor | Low | Marshal usage within LockBits scope |
| HOCR parsing | Medium | Regex may need adjustment for Tesseract version |

---

## 2. Files Modified/Created

### New Files

| File | Purpose |
|------|---------|
| `OcrPreprocessor.cs` | Image preprocessing (denoise, binarization, scaling) |
| `IOcrEngine.cs` | OCR engine interface |
| `TesseractOcrEngine.cs` | Tesseract OCR integration |
| `MultiEngineOcrService.cs` | Multi-engine OCR with fusion |
| `OcrConfidenceEvaluator.cs` | OCR quality assessment |

### Modified Files

| File | Changes |
|------|---------|
| `OcrService.cs` | Integrated preprocessing + real confidence evaluation |
| `SeeService.cs` | Added fuzzy matching + dangerous element check |
| `ElementGroundingScore.cs` | Adaptive weights + element relations |

---

## 3. Pre-Build Checklist

Before running `dotnet build`, verify:

- [ ] Windows SDK 10.0.17763.0 or later installed
- [ ] .NET 8.0 SDK installed
- [ ] Tesseract OCR installed (optional, for Tesseract engine)
  - Default install: `C:\Program Files\Tesseract-OCR\tesseract.exe`
  - Alternative: Add to PATH

---

## 4. Regression Tests (V0.2.1)

These commands MUST pass after build:

```powershell
# 1. List windows
dotnet run --project .\src\PeekabooWin.Cli -- list-windows --keyword notepad

# 2. Inspect window
dotnet run --project .\src\PeekabooWin.Cli -- inspect --window notepad --max-depth 3

# 3. Screenshot
dotnet run --project .\src\PeekabooWin.Cli -- screenshot --window notepad --out artifacts/notepad.png

# 4. Find by name
dotnet run --project .\src\PeekabooWin.Cli -- find --window notepad --name 文件

# 5. Find by control type
dotnet run --project .\src\PeekabooWin.Cli -- find --window notepad --control-type Button

# 6. Dry-run click (safety)
dotnet run --project .\src\PeekabooWin.Cli -- click-element --window notepad --control-type Button --dry-run

# 7. Press key
dotnet run --project .\src\PeekabooWin.Cli -- press --key esc
```

---

## 5. OCR Smoke Tests

### Test 1: Screenshot + OCR

```powershell
# Capture screenshot
dotnet run --project .\src\PeekabooWin.Cli -- screenshot --window notepad --out artifacts/ocr_test_notepad.png

# OCR with auto engine selection
dotnet run --project .\src\PeekabooWin.Cli -- ocr --image artifacts/ocr_test_notepad.png
```

**Expected output**:
```json
{
  "success": true,
  "text": "...",
  "confidence": 0.0-1.0,
  "engine": "windows_ocr_or_tesseract",
  "preprocessing": true,
  "estimatedConfidence": 0.0-1.0
}
```

### Test 2: Tesseract Engine

```powershell
# Test Tesseract (may fail if not installed)
dotnet run --project .\src\PeekabooWin.Cli -- ocr --image artifacts/ocr_test_notepad.png --engine tesseract --lang chi_sim+eng
```

**Expected if Tesseract NOT installed**:
```json
{
  "success": false,
  "error": "Tesseract executable not found",
  "engine_available": false,
  "hint": "Install Tesseract or use --engine windows"
}
```

**Expected if Tesseract IS installed**:
```json
{
  "success": true,
  "text": "...",
  "engine": "Tesseract",
  "confidence": 0.0-1.0
}
```

### Test 3: Dangerous Element Detection

```powershell
# Fuzzy match on a dangerous-looking element name
# This should return with IsPotentiallyDangerous=true
```

**Expected**:
```json
{
  "element": {
    "name": "确认删除",
    "elementId": "el_001"
  },
  "score": 0.95,
  "matchType": "contains",
  "isPotentiallyDangerous": true,
  "dangerWarning": "Matched dangerous keywords: 删除"
}
```

---

## 6. Naming Corrections

| Old Name | New Name | Reason |
|----------|----------|--------|
| real confidence | estimatedConfidence | Not ground-truth |
| AverageWordConfidence | EstimatedWordConfidence | Heuristic, not real |
| TrueConfidence | EstimatedConfidence | Avoid overstatement |

---

## 7. Known Limitations

1. **Tesseract HOCR parsing**: Regex may break if Tesseract version outputs different HTML format
2. **Confidence scores**: Heuristic-based, not from actual OCR engine confidence
3. **Fuzzy matching threshold**: Default 0.6 may need tuning
4. **Multi-engine fusion**: Simple averaging, may not be optimal

---

## 8. Next Steps

1. [ ] Run `dotnet build` on Windows
2. [ ] Execute V0.2.1 regression tests
3. [ ] Execute OCR smoke tests
4. [ ] Test dangerous element detection
5. [ ] If build fails, check NuGet packages and WinRT references

---

## 9. Acceptance Criteria

- [ ] `dotnet build -c Release` passes on Windows
- [ ] V0.2.1 commands (list-windows, inspect, screenshot, find, press) work
- [ ] OCR command returns structured JSON (success or graceful failure)
- [ ] Tesseract unavailable: graceful error, no crash
- [ ] Confidence named as "estimated", not "real"
- [ ] Fuzzy match on dangerous elements: warning returned

---

## Version Note

This enhancement is **V0.3.1 Recognition Enhancement**, not V0.4.

V0.3 core remains: `see → element_id → dry-run → click-element → type/press → screenshot`
