using System.Security.Cryptography;
using System.Text;
using ClientSSH.Services.Storage;

namespace ClientSSH.Services.Assistant;

public interface IAiKeyStore
{
    string? Get(AiProvider provider);

    bool Has(AiProvider provider);

    /// <summary>Saves the key, or removes it when empty.</summary>
    void Set(AiProvider provider, string? key);
}

/// <summary>
/// API keys the user pasted, one per provider. They are encrypted with Windows DPAPI for the current Windows account
/// (ai-keys.json holds only the encrypted blobs), so they are never readable as text on disk and never leave the PC
/// except in the request to the provider the user chose.
/// </summary>
public sealed class AiKeyStore : IAiKeyStore
{
    private const string FileName = "ai-keys.json";

    public string? Get(AiProvider provider)
    {
        if (!Load().TryGetValue(provider.ToString(), out var blob)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(blob), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null; // another Windows account or a damaged file: treat as no key
        }
    }

    public bool Has(AiProvider provider) => Get(provider) is not null;

    /// <summary>Saves the key, or removes it when empty.</summary>
    public void Set(AiProvider provider, string? key)
    {
        var keys = Load();
        if (string.IsNullOrWhiteSpace(key))
            keys.Remove(provider.ToString());
        else
            keys[provider.ToString()] = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));

        LocalData.Write(FileName, AppJsonContext.Default.DictionaryStringString, keys);
    }

    private static Dictionary<string, string> Load() =>
        LocalData.Read(FileName, AppJsonContext.Default.DictionaryStringString, []);
}
