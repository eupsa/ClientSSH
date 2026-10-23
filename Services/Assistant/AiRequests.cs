using System.Text;
using System.Text.Json.Nodes;

namespace ClientSSH.Services.Assistant;

/// <summary>Each provider's wire format: the HTTP requests and where the answer sits in the reply JSON.</summary>
internal static class AiRequests
{
    private const string SystemPrompt =
        "You turn a request into one shell command for a Linux or Unix server reached over SSH (bash). " +
        "The request may be in any language. Reply with ONLY the command: a single line, no explanation, " +
        "no markdown, no code fences, no backticks, no leading $ or prompt. Prefer common, safe, standard tools. " +
        "If the request is unclear or cannot be done with one command, reply with a single line that starts with # " +
        "and briefly says why, so that nothing runs.";

    public static HttpRequestMessage ListModels(AiProvider provider, string key) => provider switch
    {
        AiProvider.Claude => Get("https://api.anthropic.com/v1/models?limit=100", ("x-api-key", key), ("anthropic-version", "2023-06-01")),
        AiProvider.Gemini => Get("https://generativelanguage.googleapis.com/v1beta/models?pageSize=100", ("x-goog-api-key", key)),
        AiProvider.Groq => Get("https://api.groq.com/openai/v1/models", ("Authorization", $"Bearer {key}")),
        _ => Get("https://api.openai.com/v1/models", ("Authorization", $"Bearer {key}")),
    };

    public static HttpRequestMessage Ask(AiProvider provider, string model, string key, string prompt)
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

    public static string? ExtractText(AiProvider provider, JsonNode? json) => provider switch
    {
        AiProvider.Claude => json?["content"]?[0]?["text"]?.GetValue<string>(),
        AiProvider.Gemini => json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>(),
        _ => json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>(),
    };

    private static HttpRequestMessage Get(string url, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in headers) request.Headers.Add(name, value);
        return request;
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
}
