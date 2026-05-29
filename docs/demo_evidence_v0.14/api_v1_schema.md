# PeekabooWin Agent Runtime API v1 Schema

## Overview

The Agent Runtime API v1 provides seven endpoints for task execution, skill management, risk evaluation, and trace retrieval. All endpoints follow a unified response format and are served on `http://0.0.0.0:8025`.

## Unified Response Format

Every API v1 response adheres to the following envelope:

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120000",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "rule_based",
  "grounding_score": 0.87,
  "actions": [],
  "verification": {
    "status": "Passed",
    "reason": "Typed text 'hello' found in after-state OCR",
    "confidence": 0.9
  },
  "error": null
}
```

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| `ok` | bool | No | Overall success flag |
| `trace_id` | string | No | Execution trace identifier |
| `decision` | string | No | Risk gate decision: `ALLOW` / `BLOCK` |
| `risk_level` | string | No | Risk level: `L0` / `L1` / `L2` |
| `parser_mode` | string | No | Parser used: `rule_based` / `llm` / `none` |
| `grounding_score` | double | No | Best element grounding score (0.0–1.0) |
| `actions` | array | No | List of executed action steps |
| `verification` | object | Yes | Verification result (null if not applicable) |
| `error` | string | Yes | Error message (null on success) |

## Endpoints

---

### 1. Health Check

```
GET /api/v1/health
```

Returns the runtime health status.

**Request:** No parameters.

**Response:**

```json
{
  "ok": true,
  "trace_id": "",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [],
  "verification": null,
  "error": null
}
```

**Example:**

```bash
curl http://localhost:8025/api/v1/health
```

```json
{
  "ok": true,
  "trace_id": "",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [],
  "verification": null,
  "error": null
}
```

---

### 2. Task Preview

```
POST /api/v1/task/preview
```

Dry-run a task: parse, rank candidates, evaluate risk — but do not execute any actions. Equivalent to `task/run` with `dry_run = true`.

**Request Body:**

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `task` | string | Yes | — | Natural language task description |
| `context` | string | No | null | Additional context for the task |
| `max_steps` | int | No | 5 | Maximum number of steps to plan |
| `dry_run` | bool | No | true | Always true for preview |
| `timeout_ms` | int | No | 30000 | Timeout in milliseconds |

**Request Example:**

```json
{
  "task": "Type 'Hello World' in Notepad",
  "context": "Notepad is already open",
  "max_steps": 3,
  "timeout_ms": 10000
}
```

**Response Example:**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120100",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "rule_based",
  "grounding_score": 0.82,
  "actions": [
    {
      "step": 1,
      "thought": "Focus Notepad window",
      "action": "focus-window",
      "args": { "title": "Notepad" },
      "result": "[DRY-RUN] Would execute: focus-window",
      "success": true,
      "error": null
    },
    {
      "step": 2,
      "thought": "Type Hello World into the text field",
      "action": "type",
      "args": { "text": "Hello World" },
      "result": "[DRY-RUN] Would execute: type",
      "success": true,
      "error": null
    }
  ],
  "verification": null,
  "error": null
}
```

---

### 3. Task Run

```
POST /api/v1/task/run
```

Execute a task: parse → rank → execute → verify → recover → trace.

**Request Body:**

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `task` | string | Yes | — | Natural language task description |
| `context` | string | No | null | Additional context for the task |
| `max_steps` | int | No | 5 | Maximum number of steps to execute |
| `dry_run` | bool | No | false | If true, no actions are executed |
| `timeout_ms` | int | No | 30000 | Timeout in milliseconds |

**Request Example:**

```json
{
  "task": "Type 'Hello World' in Notepad",
  "context": "Notepad is already open",
  "max_steps": 5,
  "dry_run": false,
  "timeout_ms": 30000
}
```

**Response Example (success with verification):**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120200",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "rule_based",
  "grounding_score": 0.87,
  "actions": [
    {
      "step": 1,
      "thought": "Focus Notepad window",
      "action": "focus-window",
      "args": { "title": "Notepad" },
      "result": "Focused window: Notepad",
      "success": true,
      "error": null
    },
    {
      "step": 2,
      "thought": "Type Hello World into the text field",
      "action": "type",
      "args": { "text": "Hello World" },
      "result": "Typed: Hello World",
      "success": true,
      "error": null
    }
  ],
  "verification": {
    "status": "Passed",
    "reason": "Typed text 'Hello World' found in after-state OCR",
    "confidence": 0.9
  },
  "error": null
}
```

**Response Example (blocked by risk gate):**

```json
{
  "ok": false,
  "trace_id": "trace_20260530_120300",
  "decision": "BLOCK",
  "risk_level": "L2",
  "parser_mode": "rule_based",
  "grounding_score": 0.0,
  "actions": [
    {
      "step": 1,
      "thought": "Delete all files in system32",
      "action": "type",
      "args": { "text": "del /s C:\\Windows\\System32\\*" },
      "result": null,
      "success": false,
      "error": "Blocked by risk gate: destructive file operation"
    }
  ],
  "verification": null,
  "error": "Blocked by risk gate: destructive file operation"
}
```

**Response Example (failure with recovery):**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120400",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "rule_based",
  "grounding_score": 0.65,
  "actions": [
    {
      "step": 1,
      "thought": "Click the Save button",
      "action": "click",
      "args": { "name": "Save" },
      "result": "Clicked: Save (via OCR fallback)",
      "success": true,
      "error": null
    }
  ],
  "verification": {
    "status": "Passed",
    "reason": "State changed after click: text changed=True, element count changed=False",
    "confidence": 0.7
  },
  "error": null
}
```

---

### 4. Skill Search

```
POST /api/v1/skill/search
```

Search visual skill memory by context and window signature.

**Request Body:**

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `context` | string | Yes | — | Task context to match against |
| `window_title` | string | No | null | Window title for signature matching |
| `process_name` | string | No | null | Process name for signature matching |

**Request Example:**

```json
{
  "context": "Fill login form",
  "window_title": "Chrome",
  "process_name": "chrome"
}
```

**Response Example:**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120500",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [
    {
      "skill_id": "skill_001",
      "description": "Fill username and password in Chrome login",
      "match_score": 0.85,
      "risk_level": "L1",
      "risk_domain": "credential"
    }
  ],
  "verification": null,
  "error": null
}
```

---

### 5. Skill Replay

```
POST /api/v1/skill/replay
```

Replay a stored skill with transfer guard.

**Request Body:**

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `skill_id` | string | Yes | — | ID of the skill to replay |
| `task_text` | string | No | null | Current task text for context |
| `app_id` | string | No | null | Target application ID for transfer check |

**Request Example:**

```json
{
  "skill_id": "skill_001",
  "task_text": "Fill login form in Chrome",
  "app_id": "chrome_main"
}
```

**Response Example (allowed):**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120600",
  "decision": "ALLOW",
  "risk_level": "L1",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [
    {
      "step": 1,
      "thought": "Replaying skill: Fill username and password",
      "action": "type",
      "args": { "text": "user@example.com", "name": "Username" },
      "result": "Typed: user@example.com",
      "success": true,
      "error": null
    }
  ],
  "verification": {
    "status": "Passed",
    "reason": "Typed text 'user@example.com' found in after-state OCR",
    "confidence": 0.9
  },
  "error": null
}
```

**Response Example (blocked by transfer guard):**

```json
{
  "ok": false,
  "trace_id": "trace_20260530_120700",
  "decision": "BLOCK",
  "risk_level": "L2",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [],
  "verification": null,
  "error": "Negative transfer blocked: credential skill from banking domain cannot transfer to social media domain"
}
```

---

### 6. Risk Evaluate

```
POST /api/v1/risk/evaluate
```

Evaluate action risk without executing the action.

**Request Body:**

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `action` | string | Yes | — | Action type to evaluate |
| `args` | object | No | null | Action arguments for context |

**Request Example:**

```json
{
  "action": "type",
  "args": {
    "text": "del /s C:\\Windows\\System32\\*",
    "name": "Command Prompt"
  }
}
```

**Response Example (high risk):**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120800",
  "decision": "BLOCK",
  "risk_level": "L2",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [],
  "verification": null,
  "error": null
}
```

**Response Example (low risk):**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120900",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [],
  "verification": null,
  "error": null
}
```

---

### 7. Trace Retrieve

```
GET /api/v1/trace/{id}
```

Retrieve a previously recorded execution trace by its ID.

**Path Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | string | Yes | Trace identifier (e.g., `trace_20260530_120000`) |

**Request Example:**

```bash
curl http://localhost:8025/api/v1/trace/trace_20260530_120200
```

**Response Example:**

```json
{
  "ok": true,
  "trace_id": "trace_20260530_120200",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "rule_based",
  "grounding_score": 0.87,
  "actions": [],
  "verification": null,
  "error": null
}
```

**Response Example (trace not found):**

```json
{
  "ok": false,
  "trace_id": "trace_nonexistent",
  "decision": "ALLOW",
  "risk_level": "L0",
  "parser_mode": "none",
  "grounding_score": 0.0,
  "actions": [],
  "verification": null,
  "error": "Trace not found: trace_nonexistent"
}
```

## Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `MISSING_API_KEY` | 200 | LLM API key not configured; parser falls back to rule-based |
| `LLM_CALL_FAILED` | 200 | LLM call failed; parser falls back to rule-based |
| `BLOCKED_BY_RISK_GATE` | 200 | Action blocked by risk gate evaluation |
| `NEGATIVE_TRANSFER_BLOCKED` | 200 | Skill replay blocked by transfer guard |
| `TASK_TIMEOUT` | 200 | Task execution exceeded timeout |
| `TASK_CANCELLED` | 200 | Task was cancelled |
| `TRACE_NOT_FOUND` | 200 | Requested trace ID does not exist |
| `INVALID_REQUEST` | 400 | Missing required fields in request body |
| `INTERNAL_ERROR` | 500 | Unexpected server error |

> Note: All business-logic errors return HTTP 200 with `ok: false` and a descriptive `error` field. Only protocol-level errors (malformed request, server crash) use non-200 status codes.

## Data Models

### AgentTaskRequest

```json
{
  "task": "string (required)",
  "context": "string (optional)",
  "max_steps": 5,
  "dry_run": false,
  "timeout_ms": 30000
}
```

### AgentStep

```json
{
  "step": 1,
  "thought": "string",
  "action": "string",
  "args": {},
  "result": "string (nullable)",
  "success": true,
  "error": "string (nullable)"
}
```

### VerificationResult

```json
{
  "status": "Passed | Failed | Inconclusive",
  "reason": "string",
  "confidence": 0.9
}
```

### RiskGateTrace

```json
{
  "decision": "ALLOW | BLOCK",
  "risk_score": 0.65,
  "block_reason": "string (nullable)",
  "required_confirmation": "string (nullable)"
}
```

### RecoveryTrace

```json
{
  "strategy": "Retry | Refocus | Relocate | Replan | HumanReview | Abort",
  "should_retry": true,
  "recovery_step_count": 2
}
```

### CandidateRankTrace

```json
{
  "total_candidates": 5,
  "best_score": 0.87,
  "best_text": "Submit",
  "best_source": "uia",
  "has_viable_candidate": true
}
```

### ExecutionTrace

```json
{
  "trace_id": "trace_20260530_120000",
  "task": "string",
  "started_at": "2026-05-30T12:00:00Z",
  "completed_at": "2026-05-30T12:00:05Z",
  "success": true,
  "parser_mode": "rule_based",
  "llm_enabled": false,
  "fallback_reason": "",
  "decision": "ALLOW",
  "risk_level": "L0",
  "grounding_score": 0.87,
  "step_traces": [],
  "error": null,
  "cancelled": false,
  "timeout_triggered": false,
  "timeout_ms": 30000,
  "total_steps": 2,
  "successful_steps": 2,
  "failed_steps": 0,
  "blocked_steps": 0,
  "recovery_attempts": 0
}
```
