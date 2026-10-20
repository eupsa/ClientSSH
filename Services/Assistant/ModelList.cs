using System.Text.Json.Nodes;

namespace ClientSSH.Services.Assistant;

/// <summary>Reads the model ids out of a provider's "list models" reply, keeping the ones that can write text.</summary>
public static class ModelList
{
    public static IReadOnlyList<string> Parse(AiProvider provider, string json)
    {
        var root = JsonNode.Parse(json);
        IEnumerable<string> ids = provider == AiProvider.Gemini
            ? (root?["models"]?.AsArray() ?? [])
                .Where(m => m?["supportedGenerationMethods"]?.AsArray().Any(x => x?.GetValue<string>() == "generateContent") == true)
                .Select(m => m!["name"]!.GetValue<string>().Replace("models/", ""))
            : (root?["data"]?.AsArray() ?? []).Select(m => m?["id"]?.GetValue<string>() ?? "");

        return ids.Where(id => id.Length > 0 && IsChatModel(provider, id)).Order().ToList();
    }

    // The OpenAI and Groq lists also hold embeddings, speech, moderation and image models.
    private static bool IsChatModel(AiProvider provider, string id) => provider switch
    {
        AiProvider.OpenAI => id.StartsWith("gpt-", StringComparison.Ordinal) && !id.Contains("audio") && !id.Contains("realtime")
            && !id.Contains("image") && !id.Contains("transcribe") && !id.Contains("tts")
            || id.Length > 1 && id[0] == 'o' && char.IsDigit(id[1]),
        AiProvider.Groq => !new[] { "whisper", "guard", "tts", "playai", "distil-whisper" }.Any(id.Contains),
        _ => true,
    };
}
