using ClientSSH.Services.Assistant;
using Xunit;

namespace ClientSSH.Tests;

public class ModelListTests
{
    [Fact]
    public void Anthropic_lists_data_ids_sorted() =>
        Assert.Equal(["claude-a", "claude-b"],
            ModelList.Parse(AiProvider.Claude, """{"data":[{"id":"claude-b"},{"id":"claude-a"}]}"""));

    [Fact]
    public void OpenAI_keeps_only_chat_models() =>
        Assert.Equal(["gpt-4o-mini", "o3"],
            ModelList.Parse(AiProvider.OpenAI,
                """{"data":[{"id":"text-embedding-3-small"},{"id":"gpt-4o-mini"},{"id":"whisper-1"},{"id":"gpt-4o-realtime-preview"},{"id":"o3"}]}"""));

    [Fact]
    public void Gemini_keeps_generateContent_models_without_the_models_prefix() =>
        Assert.Equal(["gemini-x"],
            ModelList.Parse(AiProvider.Gemini,
                """{"models":[{"name":"models/gemini-x","supportedGenerationMethods":["generateContent"]},{"name":"models/embed","supportedGenerationMethods":["embedContent"]}]}"""));

    [Fact]
    public void Groq_drops_speech_and_guard_models() =>
        Assert.Equal(["llama-3.3-70b-versatile"],
            ModelList.Parse(AiProvider.Groq,
                """{"data":[{"id":"whisper-large-v3"},{"id":"llama-3.3-70b-versatile"},{"id":"llama-guard-4-12b"}]}"""));
}
