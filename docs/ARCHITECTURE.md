# Architecture

Orbit currently consists of a Windows-focused .NET 10 solution with a local ASP.NET Core server, Core domain services, shared contracts, SQLite infrastructure, a Windows Agent, and a WPF desktop dashboard. See the [development roadmap](ROADMAP.md) for all six phases and the [handoff](HANDOFF.md) for the current Phase 1 work.

## Phase 1 projects

| Project | Responsibility |
| --- | --- |
| `Orbit.Contracts` | Shared agent task, tool, application, and execution-result contracts. |
| `Orbit.Core` | Tool registry, constrained launch-intent recognition, launch planning, approval-token validation, and orchestration contracts. |
| `Orbit.Infrastructure` | EF Core SQLite context, initial migration, and audit log implementation. |
| `Orbit.WindowsAgent` | Reads the operator-managed application registry and starts only registered applications with fixed arguments. |
| `Orbit.Server` | Loopback HTTP API, AI chat provider integration, dependency injection, database migration, and the local agent workflow. |
| `Orbit.Desktop` | WPF dashboard, local whisper.cpp transcription, Windows speech synthesis, animated desktop pet, voice preferences, and system tray lifecycle. |

## Request flow

1. [Program.cs](../src/Orbit.Server/Program.cs) binds the incoming JSON and validates the message.
2. [AiProfiles.cs](../src/Orbit.Server/AiProfiles.cs) reads `ai-providers.json` from the server content root, validates the selected profile, and resolves its credential.
3. [AiChat.cs](../src/Orbit.Server/AiChat.cs) uses the resolved profile for the duration of the request. It either returns a labeled mock reply or sends a single provider request through a typed `HttpClient`.
4. The chat client extracts text and returns it with the profile, provider, and model. The [API reference](API.md) describes error responses.

The config is read on each valid chat request. Different requests can use different profiles if the file changes between reads. An invalid active profile returns HTTP 503. Each request sends traffic only to the selected provider, with no retry or automatic fallback. Saving with an editor that replaces the file atomically avoids exposing partially written JSON to concurrent requests.

## Local application-launch flow

1. The desktop dashboard submits input to `/api/agent/run`. HTTP 204 sends the message through the chat API.
2. Core recognizes explicit launch commands and checks the operator-managed application registry. Multiple matches prompt the user to specify the name.
3. The Windows Agent reads the authorized entry and starts its configured executable with its configured arguments. When no authorized app matches, the desktop resolves matching Start Menu shortcuts with executable targets and asks the user to approve the displayed launch details. The desktop can save the approval in the local registry.
4. The server reports success when Windows creates a process. SQLite records authorization and execution outcomes.

The separate planning and token endpoints still serve existing API clients.

## Provider protocols

| Provider | Upstream request | Credential |
| --- | --- | --- |
| `mock` | In-process echo | None |
| `openai` | OpenAI Chat Completions | Bearer token |
| `gemini` | Gemini OpenAI-compatible Chat Completions | Bearer token |
| `claude` | Anthropic Messages with `anthropic-version: 2026-10-21` and `max_tokens: 1024` | `x-api-key` |
| `local` | OpenAI-compatible `<baseUrl>/chat/completions` | Optional bearer token |

The Chat Completions path reads `choices[0].message.content`. The Claude path joins text blocks from `content` in response order. Orbit sends one user message per request and keeps no conversation history.

## Security boundaries

- Kestrel binds to loopback port 5080. The current API has no client authentication and assumes a trusted local environment. A remote deployment requires a separate authenticated design.
- The server operator controls the config file. The HTTP request accepts only a message and cannot select an endpoint, change profiles, or retrieve credentials. Restrict write access to the server config and credential sources.
- Cloud endpoints are fixed HTTPS URLs. Local profiles allow HTTPS or loopback HTTP, and reject URL credentials, query strings, and fragments. Upstream redirects are disabled. A configured local endpoint receives both the prompt and any credential associated with its profile.
- The outbound client disables its built-in request loggers. The API returns generic upstream failures and omits upstream bodies and credential headers.
- Keep API keys out of source files, config examples, chat messages, logs, and handoff notes. Local User Secrets are for development and are stored unencrypted under the Windows user profile.
- Direct launches accept requests to the local host from non-browser clients. The server reads executable paths and arguments from `authorized-applications.json`. Start Menu approval and registry updates happen in the desktop after user confirmation.

See [provider setup](DEVELOPMENT.md) for credential precedence and rotation.
