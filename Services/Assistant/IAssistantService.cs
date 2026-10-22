namespace ClientSSH.Services.Assistant;

/// <summary>Command text on success, or a message for the user.</summary>
public sealed record AssistantResult(string? Command, string? Error = null);

/// <summary>Model ids on success, or a message for the user.</summary>
public sealed record ModelsResult(IReadOnlyList<string>? Models, string? Error = null);

public interface IAssistantService
{
    Task<AssistantResult> AskAsync(string prompt);

    /// <summary>Asks the provider which models this key can use, so a retired default can be replaced.</summary>
    Task<ModelsResult> ListModelsAsync();
}
