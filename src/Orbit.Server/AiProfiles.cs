using System.Text.Json;
using Orbit.Contracts;

namespace Orbit.Server;

internal sealed class AiProfiles(string path, IConfiguration secrets)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ActiveAiProfile> LoadAsync(CancellationToken cancellationToken)
    {
        AiProfileConfiguration? config;
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            config = await JsonSerializer.DeserializeAsync<AiProfileConfiguration>(file, JsonOptions, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new AiConfigurationException("Check that ai-providers.json exists and contains valid JSON.");
        }

        string name;
        AiProfileSettings profile;
        Uri? endpoint;
        try
        {
            (name, profile, endpoint) = (config ?? new AiProfileConfiguration(null, null)).ValidateActiveProfile();
        }
        catch (InvalidDataException error)
        {
            throw new AiConfigurationException(error.Message);
        }

        if (profile.Provider == "mock")
            return new ActiveAiProfile(name, "mock", "mock", null, null);

        string? apiKey = null;
        if (profile.KeyRef is not null)
        {
            apiKey = secrets[profile.KeyRef];
            if (string.IsNullOrEmpty(apiKey) || apiKey.Any(character => character is < '!' or > '~'))
                throw new AiConfigurationException("Set a valid credential for the active AI profile's keyRef.");
        }

        return new ActiveAiProfile(name, profile.Provider!, profile.Model!, endpoint, apiKey);
    }
}

internal sealed class ActiveAiProfile(string name, string provider, string model, Uri? endpoint, string? apiKey)
{
    public string Name { get; } = name;
    public string Provider { get; } = provider;
    public string Model { get; } = model;
    public Uri? Endpoint { get; } = endpoint;
    public string? ApiKey { get; } = apiKey;
}

internal sealed class AiConfigurationException(string message) : Exception(message);
