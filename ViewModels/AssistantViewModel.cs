using ClientSSH.Services.Assistant;
using ClientSSH.Services.Localization;
using ClientSSH.Services.Sessions;
using ClientSSH.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientSSH.ViewModels;

/// <summary>
/// The side panel: the user describes what they want, the model answers with a command, and the command is typed
/// at the prompt of the selected session WITHOUT Enter, so the user reads it and decides.
/// </summary>
public sealed partial class AssistantViewModel : ObservableObject
{
    private readonly IAssistantService _assistant;
    private readonly IAiKeyStore _keys;
    private readonly ISettingsStore _settings;
    private readonly ISessionManager _sessions;
    private readonly bool _ready;

    public IReadOnlyList<string> ProviderNames { get; } =
        Enum.GetValues<AiProvider>().Select(AiProviders.DisplayName).ToList();

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Prompt { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;
    [ObservableProperty] public partial int ProviderIndex { get; set; }
    [ObservableProperty] public partial string Model { get; set; }
    [ObservableProperty] public partial string ModelPlaceholder { get; set; } = "";
    [ObservableProperty] public partial string ApiKey { get; set; } = "";
    [ObservableProperty] public partial bool HasKey { get; set; }

    /// <summary>Filled by "List models"; the Model box stays free text, so any id can be typed.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Models { get; } = [];

    /// <summary>Raised after a command was typed, so the page can give the keyboard back to the terminal.</summary>
    public event Action? CommandInserted;

    public AssistantViewModel(IAssistantService assistant, IAiKeyStore keys, ISettingsStore settings, ISessionManager sessions)
    {
        _assistant = assistant;
        _keys = keys;
        _settings = settings;
        _sessions = sessions;

        ProviderIndex = (int)settings.Current.AiProvider;
        Model = settings.Current.AiModel;
        Refresh();
        _ready = true;
    }

    private AiProvider Provider => (AiProvider)ProviderIndex;

    partial void OnProviderIndexChanged(int value)
    {
        if (!_ready) return;
        Model = ""; // each provider has its own model ids
        Models.Clear();
        _settings.Save(_settings.Current with { AiProvider = Provider, AiModel = "" });
        Refresh();
    }

    partial void OnModelChanged(string value)
    {
        if (_ready) _settings.Save(_settings.Current with { AiModel = value.Trim() });
    }

    private void Refresh()
    {
        ModelPlaceholder = AiProviders.DefaultModel(Provider);
        HasKey = _keys.Has(Provider);
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void SaveKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return;
        _keys.Set(Provider, ApiKey);
        ApiKey = "";
        Refresh();
        Status = Loc.Get("Assistant_KeySaved");
    }

    [RelayCommand]
    private async Task ListModelsAsync()
    {
        Status = Loc.Get("Assistant_Thinking");
        var result = await _assistant.ListModelsAsync();
        if (result.Models is null)
        {
            Status = result.Error ?? "";
            return;
        }

        Models.Clear();
        foreach (var id in result.Models) Models.Add(id);
        Status = string.Format(Loc.Get("Assistant_ModelsLoaded"), Models.Count);
    }

    [RelayCommand]
    private void RemoveKey()
    {
        _keys.Set(Provider, null);
        Refresh();
        Status = "";
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        var prompt = Prompt.Trim();
        if (prompt.Length == 0 || IsBusy) return;

        var session = _sessions.Selected?.Focused.Session;
        if (session is null)
        {
            Status = Loc.Get("Assistant_NoSession");
            return;
        }

        IsBusy = true;
        Status = Loc.Get("Assistant_Thinking");
        var result = await _assistant.AskAsync(prompt);
        IsBusy = false;

        if (result.Command is null)
        {
            Status = result.Error ?? "";
            return;
        }

        // The session may have ended while the model was answering.
        if (_sessions.Selected?.Focused.Session is not { } live)
        {
            Status = Loc.Get("Assistant_NoSession");
            return;
        }

        live.Write(result.Command);
        Prompt = "";
        Status = Loc.Get("Assistant_Inserted");
        CommandInserted?.Invoke();
    }
}
