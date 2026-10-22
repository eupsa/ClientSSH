using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Storage;

namespace ClientSSH.Services.Assistant;

/// <summary>
/// Sends one prompt, plus the fixed system prompt, to the provider the user picked with the user's own API key.
/// Nothing from the terminal, the hosts or the history is added. Runs only when the user sends a prompt.
/// The per-provider request formats live in <see cref="AiRequests"/>.
/// </summary>
public sealed partial class AssistantService(ISettingsStore settings, IAiKeyStore keys) : IAssistantService
{
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
            using var request = AiRequests.Ask(provider, model, key, prompt);
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var message = string.Format(Loc.Get("Assistant_Failed"), AiProviders.DisplayName(provider), (int)response.StatusCode);
                // 404 usually means the model id was retired: point to the list instead of leaving the user guessing.
                return new AssistantResult(null, response.StatusCode == HttpStatusCode.NotFound ? $"{message} {Loc.Get("Assistant_ModelHint")}" : message);
            }

            var command = CommandText.Clean(AiRequests.ExtractText(provider, JsonNode.Parse(await response.Content.ReadAsStringAsync())));
            return command.Length == 0 ? new AssistantResult(null, Loc.Get("Assistant_Empty")) : new AssistantResult(command);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new AssistantResult(null, Loc.Get("Assistant_Offline"));
        }
    }

    public async Task<ModelsResult> ListModelsAsync()
    {
        var provider = settings.Current.AiProvider;
        var key = keys.Get(provider);
        if (key is null) return new ModelsResult(null, Loc.Get("Assistant_NoKey"));

        try
        {
            using var request = AiRequests.ListModels(provider, key);
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new ModelsResult(null, string.Format(Loc.Get("Assistant_Failed"), AiProviders.DisplayName(provider), (int)response.StatusCode));

            return new ModelsResult(ModelList.Parse(provider, await response.Content.ReadAsStringAsync()));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new ModelsResult(null, Loc.Get("Assistant_Offline"));
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,80}$")]
    private static partial Regex ModelPattern();
}
