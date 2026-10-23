using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClientSSH.Services.Storage;

namespace ClientSSH.Services.Updates;

public sealed record ReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string Url);

public sealed record ReleaseInfo(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("assets")] List<ReleaseAsset>? Assets = null);

public enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

public sealed record UpdateResult(
    UpdateStatus Status, string? Version = null, string? Url = null, string? InstallerUrl = null, string? SumsUrl = null);

public interface IUpdateService
{
    Task<UpdateResult> CheckAsync();

    /// <summary>Downloads the installer of an available update and checks it against SHA256SUMS.txt. Null on any failure.</summary>
    Task<string?> DownloadAsync(UpdateResult update);

    /// <summary>Downloads, checks and starts the installer, which replaces the app and starts it again. False on failure.</summary>
    Task<bool> InstallAsync(UpdateResult update);
}

/// <summary>
/// Update check against the latest GitHub release of this repo. It runs when the user asks, and on its own only
/// after a crash or when Windows starts the app. Requests carry no user data beyond a standard HTTP header.
/// Nothing is installed without the user's click.
/// </summary>
public sealed partial class UpdateService(ISettingsStore settings) : IUpdateService
{

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "ClientSSH" } },
    };

    public async Task<UpdateResult> CheckAsync()
    {
        // The repo comes from user settings and goes into a URL: accept only owner/name.
        var repo = settings.Current.UpdateRepo;
        if (!RepoPattern().IsMatch(repo)) return new UpdateResult(UpdateStatus.Failed);

        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateResult(UpdateStatus.NoReleases);
            response.EnsureSuccessStatusCode();

            var release = JsonSerializer.Deserialize(
                await response.Content.ReadAsStringAsync(), AppJsonContext.Default.ReleaseInfo);

            // shortcut: tags must look like 1.2.3 or v1.2.3; suffixes such as -beta are not parsed, add semver if needed.
            if (release is null || !Version.TryParse(release.TagName.TrimStart('v', 'V'), out var latest))
                return new UpdateResult(UpdateStatus.Failed);

            // Only files of this repo's release are accepted. A renamed repo (client-ssh -> ClientSSH) answers under its
            // new name, so the prefix comes from the release page GitHub returned, not from the setting.
            var prefix = DownloadPrefix(release.HtmlUrl);
            string? Asset(Func<string, bool> match) => prefix is null ? null :
                release.Assets?.FirstOrDefault(a => match(a.Name) && a.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.Url;

            var current = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return Normalize(latest) > Normalize(current)
                ? new UpdateResult(UpdateStatus.Available, release.TagName, release.HtmlUrl,
                    Asset(n => InstallerPattern().IsMatch(n)), Asset(n => n == SumsName))
                : new UpdateResult(UpdateStatus.UpToDate, release.TagName);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateResult(UpdateStatus.Failed);
        }
    }

    public async Task<string?> DownloadAsync(UpdateResult update)
    {
        if (update.InstallerUrl is null || update.SumsUrl is null) return null;

        try
        {
            var sums = await Http.GetStringAsync(update.SumsUrl);

            var name = Path.GetFileName(new Uri(update.InstallerUrl).LocalPath);
            var dir = Path.Combine(Path.GetTempPath(), "ClientSSH-update");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, name);

            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
            response.EnsureSuccessStatusCode();
            await using (var target = File.Create(file))
                await (await response.Content.ReadAsStreamAsync(cancel.Token)).CopyToAsync(target, cancel.Token);

            string hash;
            await using (var saved = File.OpenRead(file))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(saved, cancel.Token));
            if (Sha256Sums.Matches(sums, name, hash)) return file;

            File.Delete(file);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UriFormatException)
        {
            return null;
        }
    }

    public async Task<bool> InstallAsync(UpdateResult update)
    {
        var installer = await DownloadAsync(update);
        if (installer is null) return false;

        // The installer closes this app, replaces the files and starts it again.
        Process.Start(new ProcessStartInfo(installer, "/SILENT") { UseShellExecute = true });
        return true;
    }

    // https://github.com/<owner>/<name>/releases/tag/v1.2.3 -> https://github.com/<owner>/<name>/releases/download/
    internal static string? DownloadPrefix(string htmlUrl)
    {
        if (!Uri.TryCreate(htmlUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com")
            return null;
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !RepoPattern().IsMatch($"{parts[0]}/{parts[1]}")) return null;
        return $"https://github.com/{parts[0]}/{parts[1]}/releases/download/";
    }

    private const string SumsName = "SHA256SUMS.txt";

    [GeneratedRegex(@"^ClientSSH-Setup-[0-9.]+\.exe$")]
    private static partial Regex InstallerPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPattern();

    // 1.0 and 1.0.0.0 must compare equal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
