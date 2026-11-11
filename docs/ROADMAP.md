# Development roadmap

The development sequence follows Section 15 (Development Phases) of [the original project brief](../first_prompt.txt). Phase 1 has a deferred acceptance test, and Phase 2 development has started.

| Phase | Scope | Status |
| --- | --- | --- |
| 1. Foundation + Working Windows MVP | .NET solution, ASP.NET Core server, SQLite persistence, WPF desktop app with a basic pet avatar, AI text chat, push-to-talk, Thai voice responses, Windows Agent with application discovery and launch, basic tool registry, authorization and execution logging, system tray, configuration UI, unit tests, and README and development documentation. | In progress. The dashboard settings editor and live reload are verified. Voice uses local whisper.cpp and Windows speech synthesis. The full spoken VS Code launch with a live microphone remains to be verified. See the [handoff](HANDOFF.md). |
| 2. Advanced Windows Agent | Window management, UI Automation, screen understanding, file operations, developer tools, multi-step execution, approval dialogs, Emergency Stop, and task verification. | In progress. The approved .NET workflow runs build and test with cancellation. The dashboard can find, select, restore, and focus open windows and inspect their UI Automation controls. |
| 3. Memory + Personality | Long-term memory, user preferences, personality configuration, task history, context management, and enhanced avatar behavior. | Planned |
| 4. Remote Agent | Secure Windows VPS deployment, device registration, SignalR remote execution, device presence, reconnection, and remote approvals. | Planned |
| 5. Home Assistant | Home Assistant adapter, entity discovery, device control, local integration bridge, and smart home commands. | Planned |
| 6. ESP32 Pet | ESP32-S3 firmware, LCD avatar, audio capture and playback, cloud communication, offline command recognition, local control, and OTA updates. | Planned |

Phase 1 is complete when the deliverables above are in place and the Windows MVP can capture a spoken request to open Visual Studio Code, transcribe it, identify the action, validate the tool arguments, resolve the app, check authorization, launch it, and verify the result. Orbit then responds by voice in Thai and displays the appropriate avatar states. The mock AI provider supports local testing without cloud credentials.

## Future voice activation

The [voice assistant brief](../first_prompt.txt#6-voice-assistant) calls for wake-word support after push-to-talk. A proposed wake phrase is `Hey Orbit`, which puts Orbit into a listening state for the next command. A specific development phase is still to be assigned.

## Target platforms

Section 2 of the brief uses its own phase numbers to group target platforms. Use the development phase numbers above when planning implementation.

| Target platform in Section 2 | Development phase in Section 15 |
| --- | --- |
| 1. Windows Desktop | 1. Foundation + Working Windows MVP |
| 2. Cloud Server | 4. Remote Agent |
| 3. ESP32-S3 | 6. ESP32 Pet |
| 4. Home Assistant | 5. Home Assistant |
