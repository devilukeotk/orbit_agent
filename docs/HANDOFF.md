# Orbit Agent handoff

## Current state

Phase 1 has a deferred acceptance test. The first Phase 2 workflow is implemented in the desktop dashboard. See the six-phase [development roadmap](ROADMAP.md).

As of 2026-11-11, the working slice is a .NET 10 Windows solution with a local ASP.NET Core server, single-message text chat, runtime profile selection, SQLite audit logging, a registered application launcher, and a WPF dashboard. The example config contains Mock, OpenAI, Gemini, Claude, and Local AI profiles, including two OpenAI credential references. The server defaults to the mock profile after copying the example config.

| Start here | Responsibility |
| --- | --- |
| [Orbit.slnx](../Orbit.slnx) | Solution entry point for the Windows MVP and tests. |
| [Program.cs](../src/Orbit.Server/Program.cs) | Host binding, credential configuration, HTTP client, chat endpoint, and error mapping. |
| [AiProfiles.cs](../src/Orbit.Server/AiProfiles.cs) | Per-request config loading, profile validation, endpoint selection, and credential resolution. |
| [AiChat.cs](../src/Orbit.Server/AiChat.cs) | Provider request construction and response parsing. |
| [AgentOrchestrator.cs](../src/Orbit.Core/AgentOrchestrator.cs) | Constrained application launch planning and one-time approvals. |
| [ApplicationCatalog.cs](../src/Orbit.WindowsAgent/ApplicationCatalog.cs) | Application registry loading and fixed-argument process start. |
| [MainWindow.xaml](../src/Orbit.Desktop/MainWindow.xaml) | Desktop chat, voice controls, and desktop pet toggle. |
| [ai-providers.example.json](../src/Orbit.Server/ai-providers.example.json) | Checked-in starting point for local profile configuration. |

The dashboard supports direct launches for uniquely matched authorized applications, Start Menu discovery through the API, an animated pet overlay, and local voice using whisper.cpp and Windows speech synthesis. SQLite persists execution audit events. The dashboard's Settings window validates and saves AI profiles and the launch allowlist in the selected server content root. The server reads those files on each request and resolves credentials from its configured secret source.

## Verification record

- `dotnet build Orbit.slnx` succeeded with zero warnings and errors during implementation. The environment used SDK 11.0.100 RC targeting .NET 10 and ran the server on the .NET 10 runtime.
- A temporary HTTP smoke test ran one Orbit server process and switched from Mock to an OpenAI-compatible local fixture, across two models and two dummy credentials, then to a keyless profile. The fixture received the expected path, model, and authorization header for each request.
- The same test checked rejection of an empty message, HTTP 503 for a missing credential and malformed config, and recovery after restoring the mock profile.
- The Release build and six automated tests passed. An isolated Release server launched Notepad from a direct HTTP command, denied an unknown application, and returned a mock chat reply.
- The Debug build and all six automated tests passed after adding Settings. A WPF smoke test saved both configurations in a temporary server content root, confirmed that invalid changes and stale edits left the files intact, and verified that the running server loaded a new mock profile and authorized VS Code entry.
- In the same isolated server, the Thai text command `เปิด VS Code` returned a completed launch, a VS Code window was present, and Windows synthesized the Thai reply to a WAV file. The installed Whisper base model misrecognized the app name in synthesized Thai input. Live microphone capture, audible playback, and the avatar state transitions still require the desktop test below.
- Real cloud credentials and a running Ollama model were outside that test. Claude request formatting and response parsing also await dedicated fixture coverage.

## Pick up the work

1. Follow the [development guide](DEVELOPMENT.md) to build and get a mock response on the local machine. Check the [API contract](API.md) before adding a caller.
2. When adding automated coverage, exercise the existing HTTP entry point for live switching, credential isolation, invalid config, and recovery. Use a fake upstream handler to verify all three cloud request shapes and their error handling without credentials.

Preserve the [security boundaries](ARCHITECTURE.md#security-boundaries) while extending the server. Until remote authentication and authorization are designed, run the endpoint on loopback for local development.

## Deferred Phase 1 acceptance test

The live-microphone VS Code test is deferred and remains open under the [Phase 1 completion criteria](ROADMAP.md). The tested Thai text command, VS Code window, and synthesized reply are recorded above. The local application registry has a VS Code entry.

When resuming, start Orbit Server and the dashboard. Select Thai and enable **Speak replies**. Hold **F8**, say `เปิด วีเอสโค้ด`, and release. Verify that the transcript identifies VS Code, the app opens, the Thai reply is audible, and the avatar goes through Listening → Thinking → Speaking → Idle. Record the observed transcript and outcome in this handoff before closing Phase 1.

## Phase 2 developer workflow

**Build & test solution** opens a file picker and asks for approval of the selected solution directory. [DeveloperWorkflow.cs](../src/Orbit.WindowsAgent/DeveloperWorkflow.cs) uses the VS Code entry from the selected server configuration directory, verifies a new visible window using its process path and workspace title, and runs the two fixed .NET commands in that directory. Emergency Stop is available in the dashboard and tray. This workflow runs in the desktop process and reports results in the dashboard.

A Windows smoke test called the workflow directly with `Orbit.slnx`, verified the VS Code window, and completed build and all six tests. Separate temporary solutions verified that a failed build skips tests and that cancellation terminates a running build child process and skips tests.

The dashboard handles `สลับไปที่ <ชื่อแอป>` and `switch to <app>` locally. Window discovery prefers an exact app name or configured alias, then an exact or partial window title. With multiple matches the picker lists the window titles. Orbit checks the selected handle and process before activation and confirms the foreground handle afterward. The application launch allowlist is optional for window lookup.

A Windows smoke test focused and restored Paint with no configuration file, then exercised the Thai VS Code alias, the window picker, cancellation, an unmatched name, and a closed target.

A WPF smoke test with an isolated server and the real Notepad++ Start Menu shortcut verified no match, cancellation, one-time launch, saving an approval, direct reuse of the saved approval, and SQLite audit events. The discovery path supports shortcuts that resolve to an existing executable.

**Inspect UI** uses the shared window picker and reads the selected HWND with Windows UI Automation on a worker thread. The result stays in a local dialog and shows control metadata and available patterns. The scan caps the tree at 200 controls and 12 levels and gives the dashboard an eight-second wait limit. Future interaction can use the selected window and supported patterns to scope actions.

A WPF smoke test verified the Notepad editor metadata without its document text, Calculator button capabilities, selection between two windows in one process, and the error for a window closed after selection.
