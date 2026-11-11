# Orbit Agent

Orbit is a Windows personal AI companion under active Phase 1 development. The desktop dashboard includes text chat, push-to-talk with local Whisper transcription, an animated desktop pet, system tray operation, and direct launch commands for authorized Windows applications. The loopback server maintains a SQLite execution audit log.

## Documentation

- [Architecture and security boundaries](docs/ARCHITECTURE.md)
- [Provider setup, verification, and troubleshooting](docs/DEVELOPMENT.md)
- [Chat API reference](docs/API.md)
- [Development roadmap](docs/ROADMAP.md)
- [Project handoff and next development steps](docs/HANDOFF.md)

## Run locally

Install the .NET 10 SDK and run these commands from the repository root in PowerShell.

```powershell
$orbitConfig = 'src/Orbit.Server/ai-providers.json'
if (-not (Test-Path -LiteralPath $orbitConfig)) {
    Copy-Item src/Orbit.Server/ai-providers.example.json $orbitConfig
}
$orbitApplications = 'src/Orbit.Server/authorized-applications.json'
if (-not (Test-Path -LiteralPath $orbitApplications)) {
    Copy-Item src/Orbit.Server/authorized-applications.example.json $orbitApplications
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Orbit.Server --no-launch-profile
```

The example starts with the mock profile, which echoes a labeled reply without calling an AI service. Send a request from a second terminal.

```powershell
$body = @{ message = 'Hello, Orbit' } | ConvertTo-Json
Invoke-RestMethod http://localhost:5080/api/chat -Method Post -ContentType 'application/json' -Body $body
```

The response includes the selected profile, provider, model, and reply text.

## Run the Windows dashboard

Start the local server first, then run the WPF application from another PowerShell terminal.

```powershell
dotnet run --project src/Orbit.Desktop
```

Type `open Notepad` or `เปิด Notepad`. Orbit starts the app when the command identifies one authorized entry. The system tray menu can show or exit the dashboard. Use **Desktop pet** to toggle the floating avatar.

Type `สลับไปที่ Paint` or `switch to VS Code` to focus an open window. Orbit searches app names and window titles. This works without adding apps to the launch allowlist, and aliases already in the allowlist also work. If several windows match, pick one by its title. Orbit restores a minimized window and confirms the switch after Windows moves focus to it.

Choose **Inspect UI** and select an open window to see the control tree exposed by Windows UI Automation. The local inspection lists control types, accessible names, and capabilities such as **Invoke** or **Text**. It reads control metadata and leaves text field values and document contents alone. The result depends on what the selected app exposes.

Open **Settings** and select the server's content root, normally `src/Orbit.Server`. Edit and save the JSON in the **AI profiles** or **Authorized applications** tab. The editor validates the active profile and application entries before saving. The server reads the new settings on its next request.

To run the Phase 2 developer workflow, choose **Build & test solution**, select `Orbit.slnx`, and approve the displayed workspace. Orbit opens the solution's folder in the configured VS Code, verifies that a new workspace window is visible, runs `dotnet build`, and then runs `dotnet test --no-build` after a successful build. The dashboard shows command output and exit codes. **Emergency Stop** in the dashboard or system tray cancels the workflow and stops its running build or test process tree. The VS Code window stays open.

For voice, extract the Windows x64 archive from [whisper.cpp v1.8.3](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.8.3) into `%LOCALAPPDATA%\Orbit\whisper`, then place the [multilingual base model](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin) in that folder. Orbit looks for `Release\whisper-cli.exe` and `ggml-base.bin`. To use another install or multilingual model, set `ORBIT_WHISPER_CLI` and `ORBIT_WHISPER_MODEL` to their full paths before launching Orbit.

Hold **F8** while Orbit has focus or hold **Hold to talk**, speak, then release. Orbit records up to 25 seconds and transcribes locally on the CPU. Install [Microsoft Pattara](https://support.microsoft.com/th-th/accessibility/windows/narrator/appendix-a-supported-languages-and-voices) in Windows for Thai playback with **Speak replies** enabled. Voice works without an API key. Voice language defaults to Thai. If the active chat profile uses a cloud provider, ordinary chat transcripts follow the same path as typed messages.

## Authorize an application

`src/Orbit.Server/authorized-applications.json` is an operator-managed application registry. Orbit can launch a matching authorized app directly. If a command such as `open Notepad++` has no authorized match, the desktop searches Start Menu shortcuts with executable targets and shows the selected executable, arguments, and working folder. Choose **Open once** for this launch or **Allow and open** to save the selected app for later. Chat input identifies the app by name.

```json
{
  "applications": [
    {
      "id": "notepad",
      "displayName": "Notepad",
      "executable": "notepad.exe",
      "arguments": []
    }
  ]
}
```

Keep this local configuration out of source control. The application registry is loaded for each request, so changing it takes effect without a server restart.

For the spoken VS Code test, add the `visual-studio-code` entry from the example registry to your local file. Adjust its executable path to match your installation. An entry can define `aliases` such as `VS Code`, and paths can use Windows environment variables such as `%LOCALAPPDATA%`.

## Switch providers, models, or keys

In `src/Orbit.Server/ai-providers.json`, set `model` for the profile you want and change `activeProfile` to its name. The next request uses the saved settings. A request already in progress keeps the profile it started with. Invalid active settings return an error. Requests are sent only to the selected profile.

| Profile | What to configure |
| --- | --- |
| `openai` | An OpenAI API model ID and credential referenced by `OPENAI_PRIMARY`. |
| `openai-secondary` | An OpenAI API model ID and a separate credential referenced by `OPENAI_SECONDARY`. |
| `gemini` | A Gemini model ID and credential referenced by `GEMINI_PRIMARY`. |
| `claude` | A Claude model ID and credential referenced by `CLAUDE_PRIMARY`. |
| `local` | A model installed on your local server and an OpenAI-compatible base URL. The example points to Ollama. Add `keyRef` if the endpoint requires a bearer token. |

Add profiles under any distinct names to keep multiple models or credentials for the same provider. Cloud profiles use their provider's fixed API endpoint. A local base URL can use loopback HTTP or HTTPS and should include the API prefix, such as `/v1/`. Orbit appends `chat/completions`.

Keep only credential names in `keyRef`. For development, register the actual key using .NET User Secrets with the same name, for example:

```powershell
dotnet user-secrets set OPENAI_PRIMARY '<your OpenAI API key>' --project src/Orbit.Server
```

Alternatively, provide a process environment variable such as `ORBIT_AI_KEY_OPENAI_PRIMARY` through your launch environment or secret manager. Environment variables override User Secrets. Saving changes to User Secrets is picked up while running in Development. Set environment variables before launch and preconfigure multiple credentials to switch among them while running.

Provider documentation is available for [OpenAI](https://developers.openai.com/api/reference/chat-completions/overview), [Gemini](https://ai.google.dev/gemini-api/docs/openai), [Claude](https://platform.claude.com/docs/en/api/messages/create), and [Ollama](https://docs.ollama.com/api/openai-compatibility).
