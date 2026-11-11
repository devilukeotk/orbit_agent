# Chat API

## POST /api/chat

Send `Content-Type: application/json` to `http://localhost:5080/api/chat`. Orbit accepts a nonblank `message` string up to 16,000 UTF-16 code units. Kestrel limits the request body to 64 KiB, including JSON overhead.

Request

```json
{"message":"Hello, Orbit"}
```

HTTP 200 response with the mock profile active

```json
{
  "profile": "mock",
  "provider": "mock",
  "model": "mock",
  "text": "Mock reply: Hello, Orbit"
}
```

The response reflects the profile used for this request. Each call is independent. There is no session ID, streaming response, or tool execution contract in this endpoint.

| Status | Meaning |
| --- | --- |
| 400 | The request body is malformed or the message fails validation. Explicit message validation returns an `error` field. |
| 413 | The request exceeds the server body limit. |
| 415 | The request uses an unsupported content type. |
| 502 | The provider rejected the request, could not be reached, or returned no usable text. |
| 503 | The profile file or selected profile is invalid, or a required credential is missing. |
| 504 | The upstream request timed out. |

Handled upstream and configuration errors use Problem Details. An upstream HTTP rejection includes its status code in the detail, such as `The AI provider returned HTTP 401.` The outbound request timeout is 90 seconds and the HTTP response buffer is capped at 1 MiB. A client disconnect cancels the pending work through the request cancellation token.

## Tool registry

### GET /api/tools

Returns the server's registered tool descriptors. The current Phase 1 slice registers `windows.app.discover` and `windows.app.launch`.

### POST /api/tools/windows.app.discover

Searches the operator-managed application registry and Windows Start Menu shortcut names. Discovered shortcuts carry `isAuthorized: false` and an empty launch target.

```json
{"query":"notepad"}
```

### POST /api/agent/tasks

The desktop dashboard uses `POST /api/agent/run` with the same `input` shape for direct low-risk application launches. It returns HTTP 204 for input outside the launch grammar, or an agent task with the launch result or a request to clarify an ambiguous name. When no authorized app matches, the task has `failure: "NoAuthorizedApplication"`, which the desktop uses to offer Start Menu discovery and local approval.

Plans a supported application-launch request. `open <application>` and `เปิด<application>` are currently supported. A recognized, authorized application returns an `AwaitingApproval` task with an opaque one-time `approvalToken` that expires after two minutes. The caller can submit the token to the separate launch endpoint.

```json
{"input":"open Notepad"}
```

### POST /api/agent/tasks/launch

Consumes an approval token to launch the corresponding authorized application. The server reloads the registry before launch and refuses a target that has been removed. The result reports success only after Windows creates a process.

```json
{"applicationId":"notepad","approvalToken":"opaque-token-from-task"}
```

All tool endpoints bind to loopback with the rest of the server. They are intended for the local desktop application during Phase 1.
