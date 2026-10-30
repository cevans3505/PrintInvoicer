using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintInvoicer.Core.Updates;

namespace PrintInvoicer.App;

/// <summary>A published release: its version and the page to download it from.</summary>
public record ReleaseInfo(Version Version, string Url);

/// <summary>
/// Tells the user when a newer release is on GitHub. It never downloads or installs anything: the bar links to the
/// release page and the user replaces the exe, so nothing runs on this PC without a person choosing it.
/// The startup check runs at most once a day; launches in between reuse the last answer, kept per PC in
/// %LOCALAPPDATA%\PrintInvoicer\update-check.json. Network problems are silent.
/// </summary>
public partial class UpdateService : ObservableObject
{
    /// <summary>The repository whose releases are checked. A fork that publishes its own releases changes this.</summary>
    public const string Repository = "cevans3505/PrintInvoicer";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    private record State(DateTime? LastCheckUtc, string? LatestTag, string? LatestUrl);

    private readonly Func<CancellationToken, Task<ReleaseInfo?>> _fetch;
    private readonly string _statePath;

    public UpdateService(Version? installed = null, Func<CancellationToken, Task<ReleaseInfo?>>? fetch = null, string? statePath = null)
    {
        Installed = ReleaseVersion.Normalize(installed ?? Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        _fetch = fetch ?? FetchLatestAsync;
        _statePath = statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintInvoicer", "update-check.json");
        Status = $"You have version {InstalledText}.";
    }

    public Version Installed { get; }
    public string InstalledText => ReleaseVersion.Display(Installed);

    [ObservableProperty] private ReleaseInfo? _available;
    [ObservableProperty] private bool _showBar;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _checking;

    public string BarText => Available is ReleaseInfo r
        ? $"PrintInvoicer {ReleaseVersion.Display(r.Version)} is available. You have {InstalledText}."
        : "";

    partial void OnAvailableChanged(ReleaseInfo? value) => OnPropertyChanged(nameof(BarText));

    /// <summary>The startup check: asks GitHub if a day has passed since the last time, otherwise uses that answer.</summary>
    public async Task CheckOnStartupAsync(bool enabled)
    {
        if (!enabled) return;
        var state = LoadState();
        ReleaseInfo? latest = ReleaseVersion.IsDue(state.LastCheckUtc, DateTime.UtcNow)
            ? await FetchAndRememberAsync()
            : FromState(state);
        Apply(latest, manual: false);
    }

    /// <summary>The Settings button: always asks GitHub, and says what it found.</summary>
    [RelayCommand]
    private async Task CheckNowAsync()
    {
        Checking = true;
        Status = "Checking...";
        try
        {
            Apply(await FetchAndRememberAsync(), manual: true);
        }
        finally
        {
            Checking = false;
        }
    }

    private bool _lastFetchFailed;

    private void Apply(ReleaseInfo? latest, bool manual)
    {
        if (latest is not null && ReleaseVersion.IsNewer(latest.Version, Installed))
        {
            Available = latest;
            ShowBar = true;
            Status = $"Version {ReleaseVersion.Display(latest.Version)} is available. You have {InstalledText}.";
        }
        else if (manual)
        {
            Available = null;
            ShowBar = false;
            Status = _lastFetchFailed
                ? $"Could not reach GitHub to check. You have version {InstalledText}."
                : $"You have the latest version ({InstalledText}). Checked {DateTime.Now:MMM d, h:mm tt}.";
        }
    }

    /// <summary>Hides the bar until the app is next started; it comes back then, so an update is not forgotten.</summary>
    [RelayCommand]
    private void Later() => ShowBar = false;

    [RelayCommand]
    private void Download()
    {
        string url = Available?.Url ?? ReleasesPage;
        // Only ever open this repository's pages, whatever the API returned.
        if (!url.StartsWith($"https://github.com/{Repository}/", StringComparison.OrdinalIgnoreCase)) url = ReleasesPage;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task<ReleaseInfo?> FetchAndRememberAsync()
    {
        ReleaseInfo? latest;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            latest = await _fetch(cts.Token);
            _lastFetchFailed = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _lastFetchFailed = true;
            return FromState(LoadState());   // offline: fall back to what was known
        }
        SaveState(new State(DateTime.UtcNow, latest is null ? null : "v" + ReleaseVersion.Display(latest.Version), latest?.Url));
        return latest;
    }

    private sealed class LatestRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PrintInvoicer", "1"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>GitHub's latest published (non-draft, non-prerelease) release; null when the repo has none.</summary>
    private static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var release = await JsonSerializer.DeserializeAsync<LatestRelease>(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ReleaseVersion.Parse(release?.TagName) is Version v ? new ReleaseInfo(v, release!.HtmlUrl ?? ReleasesPage) : null;
    }

    private static ReleaseInfo? FromState(State s) =>
        ReleaseVersion.Parse(s.LatestTag) is Version v ? new ReleaseInfo(v, s.LatestUrl ?? ReleasesPage) : null;

    private State LoadState()
    {
        try
        {
            if (File.Exists(_statePath)) return JsonSerializer.Deserialize<State>(File.ReadAllText(_statePath)) ?? new State(null, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new State(null, null, null);
    }

    private void SaveState(State s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(s));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
