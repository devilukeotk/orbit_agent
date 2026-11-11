# Development and provider configuration

## Start and verify the server

Install the .NET 10 SDK and run the following from the project root in PowerShell. The copy command preserves an existing local config.

```powershell
dotnet --list-sdks
dotnet build Orbit.slnx
$orbitConfig = 'src/Orbit.Server/ai-providers.json'
if (-not (Test-Path -LiteralPath $orbitConfig)) {
    Copy-Item src/Orbit.Server/ai-providers.example.json $orbitConfig
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Orbit.Server --no-launch-profile
```

From a second PowerShell terminal, send a request. With `activeProfile` set to `mock`, expect HTTP 200 and `text` equal to `Mock reply: Hello, Orbit`.

```powershell
$orbitBody = @{ message = 'Hello, Orbit' } | ConvertTo-Json
Invoke-RestMethod http://localhost:5080/api/chat -Method Post -ContentType 'application/json' -Body $orbitBody
```

## Configure a profile

The server reads the local `ai-providers.json` from its content root. With the run command above, this is the server project directory. When launching the built DLL with a different working directory, pass `--contentRoot` with the directory containing the config.

| Setting | Meaning |
| --- | --- |
| `activeProfile` | Exact profile name from `profiles`, including letter case. |
| `provider` | One of `mock`, `openai`, `gemini`, `claude`, or `local`. |
| `model` | Exact model ID accepted by the provider. Required except for `mock`. |
| `keyRef` | Credential reference. Required for cloud profiles and optional for local profiles. Use 1–64 characters, beginning with an uppercase letter and followed by uppercase letters, digits, or underscores. Omit the field for a keyless local endpoint. |
| `baseUrl` | Required for `local`. Include the API prefix, such as `http://127.0.0.1:11434/v1/`. Orbit appends `chat/completions`. |

For Ollama, start the local service, use `ollama list` to check which models are installed, and copy the desired model ID into the `local` profile. For another compatible server, use its API base URL and model ID. The selected server must be reachable from the machine running Orbit.

## Set credentials and switch profiles

For development, store a credential under the exact `keyRef` name. Use your own key in place of the placeholder.

```powershell
dotnet user-secrets set OPENAI_PRIMARY '<your API key>' --project src/Orbit.Server
```

User Secrets are loaded and watched in the `Development` environment. A process environment variable such as `ORBIT_AI_KEY_OPENAI_PRIMARY` takes precedence over the matching User Secret. Supply environment variables through the launch environment or secret manager before starting the server. Changes to an external environment variable require a new process to take effect.

To switch without restarting:

1. Prepare both profiles with their respective models and credential references. Use distinct references for different keys.
2. Save the desired profile name in `activeProfile`. A new request uses the saved selection.
3. Send a request and confirm the `profile`, `provider`, and `model` fields. Keep the previous profile available if you need to switch back.

To rotate a key while the server is running, switch to a profile with an already loaded credential, or update the matching User Secret in Development if there is no environment override. Verify the new credential before revoking the old one.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Server fails to bind | Check whether another process is using loopback port 5080. |
| 503 after startup | Check the content root, filename, JSON syntax, exact profile name, model ID, and credential reference. |
| 502 with upstream 401 or 403 | Check the key, account access, and any environment override. |
| 502 with upstream 404 | Check the model ID and, for local AI, whether the base URL includes the expected API prefix. |
| 502 with no upstream status | Check that the service is listening, the destination is reachable, and TLS is valid when using HTTPS. |
| 504 | Check upstream availability and local model startup or inference time. |

For the verification already performed and remaining test cases, see the [handoff](HANDOFF.md).
