using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Storage;

namespace ClientSSH.Services.Assistant;

/// <summary>Command text on success, or a message for the user.</summary>
public sealed record AssistantResult(string? Command, string? Error = null);

public interface IAssistantService
{
    Task<AssistantResult> AskAsync(string prompt);
}

/// <summary>
/// Sends one prompt, plus the fixed system prompt, to the provider the user picked with the user's own API key.
/// Nothing from the terminal, the hosts or the history is added. Runs only when the user sends a prompt.
/// </summary>
public sealed partial class AssistantService(ISettingsStore settings, AiKeyStore keys) : IAssistantService
{
    private const string SystemPrompt =
        "You turn a request into one shell command for a Linux or Unix server reached over SSH (bash). " +
        "The request may be in any language. Reply with ONLY the command: a single line, no explanation, " +
        "no markdown, no code fences, no backticks, no leading $ or prompt. Prefer common, safe, standard tools. " +
        "If the request is unclear or cannot be done with one command, reply with a single line that starts with # " +
        "and briefly says why, so that nothing runs.";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "ClientSSH" } },
    };

    public async Task<AssistantResult> AskAsync(string prompt)
    {
        var provider = settings.Current.AiProvider;
        var model = string.IsNullOrWhiteSpace(settings.Current.AiModel) ? AiProviders.DefaultModel(provider) : settings.Current.AiModel.Trim();
        var key = keys.Get(provider);

        if (key is null) return new AssistantResult(null, Loc.Get("Assistant_NoKey"));
        // The model goes into a URL (Gemini) and a JSON body: accept only plain model ids.
        if (!ModelPattern().IsMatch(model)) return new AssistantResult(null, Loc.Get("Assistant_BadModel"));

        try
        {
            using var request = BuildRequest(provider, model, key, prompt);
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new AssistantResult(null, string.Format(Loc.Get("Assistant_Failed"), AiProviders.DisplayName(provider), (int)response.StatusCode));

            var command = CommandText.Clean(ExtractText(provider, JsonNode.Parse(await response.Content.ReadAsStringAsync())));
            return command.Length == 0 ? new AssistantResult(null, Loc.Get("Assistant_Empty")) : new AssistantResult(command);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new AssistantResult(null, Loc.Get("Assistant_Offline"));
        }
    }

    private static HttpRequestMessage BuildRequest(AiProvider provider, string model, string key, string prompt)
    {
        switch (provider)
        {
            case AiProvider.Claude:
                return Post("https://api.anthropic.com/v1/messages", new JsonObject
                {
                    ["model"] = model,
                    ["max_tokens"] = 300,
                    ["system"] = SystemPrompt,
                    ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
                }, ("x-api-key", key), ("anthropic-version", "2023-06-01"));

            case AiProvider.Gemini:
                return Post($"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", new JsonObject
                {
                    ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = SystemPrompt }) },
                    ["contents"] = new JsonArray(new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt }),
                    }),
                    ["generationConfig"] = new JsonObject { ["temperature"] = 0 },
                }, ("x-goog-api-key", key));

            default: // OpenAI and Groq share the same chat completions format
                var url = provider == AiProvider.Groq
                    ? "https://api.groq.com/openai/v1/chat/completions"
                    : "https://api.openai.com/v1/chat/completions";
                return Post(url, new JsonObject
                {
                    ["model"] = model,
                    ["temperature"] = 0,
                    ["messages"] = new JsonArray(
                        new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                        new JsonObject { ["role"] = "user", ["content"] = prompt }),
                }, ("Authorization", $"Bearer {key}"));
        }
    }

    private static HttpRequestMessage Post(string url, JsonObject body, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers) request.Headers.Add(name, value);
        return request;
    }

    private static string? ExtractText(AiProvider provider, JsonNode? json) => provider switch
    {
        AiProvider.Claude => json?["content"]?[0]?["text"]?.GetValue<string>(),
        AiProvider.Gemini => json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>(),
        _ => json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>(),
    };

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,80}$")]
    private static partial Regex ModelPattern();
}
