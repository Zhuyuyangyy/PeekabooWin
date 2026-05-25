# Changelog

All notable changes to PeekabooWin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.9.0] - 2026-05-26

### Added
- **Multi-App Skill Generalization** — Skills learned on one app can transfer to similar apps
- `AppProfile` — Persistent application automation profile
- `WindowSignature` — Real-time window fingerprint (WindowType + InputMode + RiskDomain + ProcessFamily)
- `SkillScope` — Cross-app migration rules (SupportedApps + RequiredAnchors + ForbiddenDomains + MinRiskLevel)
- `SkillScopeValidator` — Pre-injection scope validation
- `NegativeTransferGuard` — Blocks dangerous transfers (high-risk verb + L0/L1 skill, forbidden domain, external_ai_chat→payment/admin)
- `AnchorMapping` — Semantic anchor ↔ OCR text mapping
- `SkillTransferController` — Full transfer pipeline (Scope → Guard → Anchor → Score → Decision)
- `VisualAnchor` — Canonical UI anchor types (input_box/send_btn/ok_btn/cancel_btn/close_btn/edit_region)
- Enum types: `WindowType.cs`, `InputMode.cs`, `RiskDomain.cs`
- `VisualSkill` fields: `RiskDomain`, `ContextAnchors`
- `VacpSkillIntegration` rewritten with `BuildWindowSignature()` + `SearchWithContext()`
- CLI `skill-search-context` command
- Unit test project (`tests/PeekabooWin.Core.Tests`) with 30 passing tests

### Changed
- `HandleSkillSearchContext` — Fixed to use AppProfile.FromWindowSignature
- `VisualSkill.SkillScope.RequiredAnchors` → `List<string>` (was string[])
- `AppProfile.IsCompatibleWith` now checks both AppId and ProcessName

### Fixed
- `AnchorMapping.cs` missing enum `WindowType` reference (added `using PeekabooWin.Core.Models;`)
- `WindowSignature.Profile` back-compat property restored
- `AppProfile.WindowTitle` property added
- CLI `SupportedWindowTypes` removed (property does not exist on SkillScope)
- CLI duplicate `AppName` fixed in HandleSkillSearchContext

### Demos
- Demo 11: Notepad→Edge cross-app text input transfer (score=0.78, INJECT)
- Demo 12: Dialog confirm blocked on Doubao (forbidden domain)
- Demo 13: Bank transfer high-risk blocking (L0 skill on payment = BLOCK)

---

## [0.8.0] - 2026-05-25
### Added
- Skill-Guided Execution with multi-dimensional scoring
- `SkillMatchScore` (AppMatch + TextMatch + ActionMatch + RiskMatch + Recency)
- `SkillExecutionPolicy` — L0 high-risk task auto-intercept
- `SkillHint` injection into VacpRequest (visual ranking, does not bypass VACP)
- CLI: `skill-search`, `skill-use-preview`, `skill-execute-guided`

---

## [0.7.0] - 2026-05-24
### Added
- Visual Skill Memory — UI pattern memory
- `VisualSkill` + `VisualSkillStore` + `VisualSkillRetriever` + `VisualSkillExtractor`
- Cross-session persistence (`skills.json`)
- CLI: `skill-list`, `skill-replay`, `skill-seed`

---

## [0.6.0] - 2026-05-22
### Added
- VACP — Vision-Action Closed-loop Planner
- Risk gate + execution verification + failure recovery

---

## [0.5.0] - 2026-05-20
### Added
- HTTP API server (供 Hermes/OpenClaw 调用)
- `/health`, `/windows`, `/click`, `/type`, `/agent` endpoints

---

## [0.4.0] - 2026-05-18
### Added
- LLM Agent Runtime (自然语言任务解析)

---

## [0.3.0] - 2026-05-15
### Added
- OCR 文字识别底座

---

## [0.2.0] - 2026-05-10
### Added
- UI Automation 控件树 (`inspect`, `find`, `click-element`)

---

## [0.1.0] - 2026-05-05
### Added
- Window capture + screenshot + mouse/keyboard input
- `list-windows`, `focus-window`, `screenshot`, `click`, `type`, `hotkey`