using Microsoft.Windows.ApplicationModel.Resources;

namespace ClientSSH.Services.Localization;

/// <summary>Reads pt-BR strings from Resources.resw for text produced in code.</summary>
internal static class Loc
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);
}
