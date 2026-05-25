# PeekabooWin API Server

HTTP REST API for external Agent integration (OpenClaw/Hermes).

## Run

```bash
cd src/PeekabooWin.ApiServer
dotnet run -c Release
```

Or use the batch file:
```bash
start_api_server.bat
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | /health | Server health check |
| GET | /api/v1/skills/list | List all visual skills |
| POST | /api/v1/transfer/decide | Get transfer decision for a skill |
| GET | /api/v1/app/profile | Get AppProfile for a process |
| POST | /api/v1/window/capture | Capture window screenshot (planned) |

## Transfer Decide Example

```bash
curl -X POST http://localhost:8025/api/v1/transfer/decide ^
  -H "Content-Type: application/json" ^
  -d "{\"skillId\":\"vs_notepad_edit\",\"skillRiskLevel\":\"L0\",\"appId\":\"notepad\",\"appRiskDomain\":\"neutral\",\"taskText\":\"type hello\",\"score\":0.78}"
```

Response:
```json
{"action":"INJECT","reason":"APPROVED score=0.780","score":0.78,"blockReason":null}
```