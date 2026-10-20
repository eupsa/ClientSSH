using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClientSSH.Services.Storage;

namespace ClientSSH.Services.Updates;

public sealed record ReleaseInfo(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl);

public enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

public sealed record UpdateResult(UpdateStatus Status, string? Version = null, string? Url = null);

public interface IUpdateService
{
    Task<UpdateResult> CheckAsync();
}

/// <summary>
/// Manual update check against the latest GitHub release of this repo. Only runs when the user asks;
/// the request carries no user data beyond a standard HTTP header.
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

            var current = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return Normalize(latest) > Normalize(current)
                ? new UpdateResult(UpdateStatus.Available, release.TagName, release.HtmlUrl)
                : new UpdateResult(UpdateStatus.UpToDate, release.TagName);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateResult(UpdateStatus.Failed);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPattern();

    // 1.0 and 1.0.0.0 must compare equal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
