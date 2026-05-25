# Known Issues

## V0.7
- **2 compiler warnings**: CS8602 null dereference warnings in VacpPlannerWithSkills.cs and ApiServer.cs
- **Impact**: None — does not affect any validated CLI commands or Demo7 evidence
- **Resolution**: To be cleaned before V0.8 release

## V0.9
- **WindowSignature.SimilarityTo() score**: When WindowType+InputMode+RiskDomain are identical but ProcessFamily differs, the score is ~0.1 (not 1.0). The weighted formula (0.4+0.3+0.2)/9 = 0.1 for same family is the design intent. Full family match adds +0.1.

## V0.8
- **1 compiler warning**: CS8602 null dereference in ApiServer.cs (pre-existing, non-critical)

## All Versions
- Tesseract tessdata (chi_sim+eng) must be present in tessdata/ for OCR to function
- UIA may not work on some apps (games, Electron apps) — OCR fallback should be used