namespace ClientSSH.Services.Assistant;

public enum AiProvider { Claude, OpenAI, Gemini, Groq }

public static class AiProviders
{
    /// <summary>
    /// Model used when the user leaves the field empty. shortcut: ids age, so the field stays editable;
    /// update these when a provider retires one.
    /// </summary>
    public static string DefaultModel(AiProvider provider) => provider switch
    {
        AiProvider.Claude => "claude-haiku-5-5",
        AiProvider.OpenAI => "gpt-4o-mini",
        AiProvider.Gemini => "gemini-2.0-flash",
        _ => "llama-3.3-70b-versatile",
    };

    public static string DisplayName(AiProvider provider) => provider switch
    {
        AiProvider.Claude => "Claude (Anthropic)",
        AiProvider.OpenAI => "ChatGPT (OpenAI)",
        AiProvider.Gemini => "Gemini (Google)",
        _ => "Groq",
    };
}
