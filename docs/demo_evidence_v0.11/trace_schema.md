# PeekabooWin V0.11 Trace Schema

This document defines the trace schema used by PeekabooWin V0.11 for recording agent decisions, actions, and verifications. The schema extends the existing `VacpTraceRecord` format to support agent-hostable API endpoints.

---

## TraceRecord Schema

Every action processed by the PeekabooWin pipeline emits a `TraceRecord`. Records are append-only and form the audit trail for all agent-driven desktop interactions.

```json
{
  "trace_id": "string — UUID v4, unique per trace record",
  "session_id": "string — UUID v4, groups records from a single agent session",
  "timestamp": "string — ISO 8601 UTC timestamp",
  "version": "string — schema version, e.g. '0.11.0'",
  "decision": "ALLOW | REVIEW | BLOCK",
  "risk_reason": "string | null — human-readable reason when decision is REVIEW or BLOCK",
  "actions": [
    {
      "action_id": "string — UUID v4",
      "type": "string — e.g. 'click', 'type', 'scroll', 'screenshot', 'ocr'",
      "target": {
        "element": "string — accessible name or selector",
        "bounding_box": {
          "x": "number",
          "y": "number",
          "width": "number",
          "height": "number"
        },
        "grounding_score": "number — 0.0 to 1.0, confidence of element match"
      },
      "parameters": "object | null — action-specific parameters",
      "status": "pending | executed | failed | skipped",
      "error_code": "string | null — machine-readable error code on failure",
      "hint": "string | null — recovery hint for the agent on failure"
    }
  ],
  "verification": {
    "method": "string — e.g. 'screenshot_diff', 'ocr_match', 'a11y_tree_snapshot'",
    "passed": "boolean",
    "details": "string | null"
  },
  "error_code": "string | null — top-level error code for the trace (e.g. 'RISK_BLOCKED', 'GROUNDING_LOW', 'ACTION_FAILED')",
  "hint": "string | null — top-level recovery hint for the agent",
  "metadata": {
    "skill_id": "string | null — referenced skill if action was replayed",
    "skill_reused": "boolean — whether a previously recorded skill was replayed",
    "ocr_engine": "string — OCR engine used, e.g. 'WindowsAiOcr', 'Tesseract'",
    "dpi_scale": "number — display DPI scaling factor",
    "monitor_count": "number — number of active monitors"
  }
}
```

---

## Field Reference

### `trace_id`
Globally unique identifier for this trace record. Generated at trace creation time.

### `decision`
The risk-gate decision for the requested action(s):

| Value | Meaning |
|---|---|
| `ALLOW` | Action is safe to execute; no human review required |
| `REVIEW` | Action may be risky; human review recommended before execution |
| `BLOCK` | Action is unsafe; execution is prevented |

### `risk_reason`
Populated when `decision` is `REVIEW` or `BLOCK`. Contains a human-readable explanation of why the action was flagged. Examples:
- `"Registry write to HKLM\\SOFTWARE — system-wide impact"`
- `"File delete operation on C:\\Windows\\ — protected path"`
- `"Unknown executable launch — not in allowlist"`

### `actions`
Array of individual actions within this trace. Each action has its own `action_id`, `type`, `target`, `status`, and optional `error_code` / `hint`.

### `verification`
Post-execution verification result. Describes the method used to confirm the action achieved its intended effect and whether verification passed.

### `error_code`
Top-level machine-readable error code. Standard error codes:

| Code | Meaning |
|---|---|
| `RISK_BLOCKED` | Action blocked by ActionRiskGate |
| `RISK_REVIEW` | Action flagged for review |
| `GROUNDING_LOW` | Element grounding score below threshold |
| `ACTION_FAILED` | Action execution failed |
| `OCR_UNAVAILABLE` | OCR engine not available |
| `OCR_ERROR` | OCR processing error |
| `TIMEOUT` | Action timed out |
| `CANCELLED` | Action cancelled via CancellationToken |

### `hint`
Top-level recovery hint for the agent. Provides actionable guidance when an error occurs. Examples:
- `"Try an alternative selector for the target element"`
- `"Retry with elevated permissions or choose a different target path"`
- `"Switch OCR engine and retry"`

---

## API Response Schema (Agent-Hostable Endpoints)

When PeekabooWin is hosted as an API for external agents, endpoints return the following envelope:

```json
{
  "request_id": "string — UUID v4, matches the incoming request",
  "trace_id": "string — UUID v4, links to the TraceRecord",
  "status": "success | error | blocked | review",
  "decision": "ALLOW | REVIEW | BLOCK",
  "data": "object | null — response payload on success",
  "error": {
    "code": "string — machine-readable error code",
    "message": "string — human-readable error message",
    "hint": "string | null — recovery hint for the agent"
  },
  "trace_url": "string | null — URL to the full TraceRecord for auditing"
}
```

### Status Mapping

| `status` | `decision` | Meaning |
|---|---|---|
| `success` | `ALLOW` | Action executed successfully |
| `error` | `ALLOW` | Action was allowed but failed during execution |
| `blocked` | `BLOCK` | Action blocked by risk gate |
| `review` | `REVIEW` | Action flagged for human review |

---

## Versioning

The trace schema follows semantic versioning. The `version` field in each `TraceRecord` indicates the schema version used. Breaking changes increment the major version; additive changes increment the minor version.

Current schema version: **0.11.0**
