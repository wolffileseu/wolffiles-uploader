using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WolffilesUploader.Services;

public class UpdateCheckerService
{
    private const string GitHubApiUrl =
        "https://api.github.com/repos/wolffileseu/wolffiles-uploader/releases/latest";
    private const string ReleasesPageUrl =
        "https://github.com/wolffileseu/wolffiles-uploader/releases/latest";
    private const string AppInstallerUrl =
        "https://wolffiles.eu/downloads/wolffiles-uploader.appinstaller";

    private readonly HttpClient _http;

    public UpdateCheckerService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "WolffilesUploader");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0] // strip build metadata
        ?? "1.0.0";

    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            var resp = await _http.GetStringAsync(GitHubApiUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(resp);

            if (release?.TagName == null)
                return new UpdateCheckResult { IsUpToDate = true };

            var latestVersion = release.TagName.TrimStart('v');
            var isNewer = IsNewerVersion(latestVersion, CurrentVersion);

            return new UpdateCheckResult
            {
                IsUpToDate = !isNewer,
                LatestVersion = latestVersion,
                ReleasesUrl = ReleasesPageUrl,
                AppInstallerUrl = AppInstallerUrl,
                ReleaseNotes = release.Body ?? ""
            };
        }
        catch
        {
            return new UpdateCheckResult { IsUpToDate = true, CheckFailed = true };
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var l) && Version.TryParse(current, out var c))
            return l > c;
        return string.Compare(latest, current, StringComparison.Ordinal) > 0;
    }
}

public record UpdateCheckResult
{
    public bool IsUpToDate { get; init; }
    public bool CheckFailed { get; init; }
    public string? LatestVersion { get; init; }
    public string? ReleasesUrl { get; init; }
    public string? AppInstallerUrl { get; init; }
    public string? ReleaseNotes { get; init; }
}

file class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
}
