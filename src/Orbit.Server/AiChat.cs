using System.Net.Http.Headers;
using System.Text.Json;

namespace Orbit.Server;

internal sealed class AiChat(HttpClient client, AiProfiles profiles)
{
    public async Task<ChatReply> SendAsync(string message, CancellationToken cancellationToken)
    {
        var profile = await profiles.LoadAsync(cancellationToken);
        if (profile.Provider == "mock")
            return new ChatReply(profile.Name, profile.Provider, profile.Model, $"Mock reply: {message}");

        var messages = new[] { new { role = "user", content = message } };
        bool isClaude = profile.Provider == "claude";
        using var request = new HttpRequestMessage(HttpMethod.Post, profile.Endpoint);
        if (isClaude)
        {
            request.Headers.Add("x-api-key", profile.ApiKey);
            request.Headers.Add("anthropic-version", "2026-10-21");
            request.Content = JsonContent.Create(new { model = profile.Model, messages, max_tokens = 1024 });
        }
        else
        {
            if (profile.ApiKey is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
            request.Content = JsonContent.Create(new { model = profile.Model, messages });
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        try
        {
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            var root = document?.RootElement ?? throw new AiResponseException();
            string? text = isClaude
                ? ReadClaudeText(root)
                : root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(text))
                throw new AiResponseException();

            return new ChatReply(profile.Name, profile.Provider, profile.Model, text);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new AiResponseException();
        }
    }

    private static string ReadClaudeText(JsonElement root)
    {
        var textBlocks = root.GetProperty("content").EnumerateArray()
            .Where(block => block.GetProperty("type").GetString() == "text")
            .Select(block => block.GetProperty("text").GetString());
        return string.Join("\n", textBlocks);
    }
}

internal sealed record ChatReply(string Profile, string Provider, string Model, string Text);
internal sealed class AiResponseException : Exception;
