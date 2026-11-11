using System.Text.RegularExpressions;

namespace Orbit.Contracts;

public sealed record AiProfileConfiguration(string? ActiveProfile, Dictionary<string, AiProfileSettings?>? Profiles)
{
    public (string Name, AiProfileSettings Profile, Uri? Endpoint) ValidateActiveProfile()
    {
        if (string.IsNullOrWhiteSpace(ActiveProfile) ||
            Profiles is null || !Profiles.TryGetValue(ActiveProfile, out var profile) || profile is null)
            throw new InvalidDataException("Set activeProfile to a profile defined in ai-providers.json.");

        if (profile.Provider == "mock")
            return (ActiveProfile, profile, null);

        var endpoint = profile.Provider switch
        {
            "openai" => new Uri("https://api.openai.com/v1/chat/completions"),
            "gemini" => new Uri("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"),
            "claude" => new Uri("https://api.anthropic.com/v1/messages"),
            "local" => GetLocalEndpoint(profile.BaseUrl),
            _ => throw new InvalidDataException("The active profile has an unsupported provider.")
        };

        if (string.IsNullOrWhiteSpace(profile.Model))
            throw new InvalidDataException("Set a model for the active AI profile.");

        if ((profile.Provider != "local" || profile.KeyRef is not null) &&
            (profile.KeyRef is null || !Regex.IsMatch(profile.KeyRef, @"\A[A-Z][A-Z0-9_]{0,63}\z")))
            throw new InvalidDataException("Set a keyRef using uppercase letters, digits, and underscores.");

        return (ActiveProfile, profile, endpoint);
    }

    private static Uri GetLocalEndpoint(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Set a local baseUrl using HTTPS or loopback HTTP, without credentials, query, or fragment.");

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/chat/completions");
    }
}

public sealed record AiProfileSettings(string? Provider, string? Model, string? KeyRef, string? BaseUrl);
