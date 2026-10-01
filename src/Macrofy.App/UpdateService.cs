using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Macrofy.App;

// Checks GitHub Releases for a newer version and installs it. Only works when Macrofy was
// installed with Setup.exe; a copy run straight from a build folder just reports that.
public sealed class UpdateService
{
    public const string RepoUrl = "https://github.com/UltimaCodes/Macrofy";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false));
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    public static string CurrentVersion { get; } =
        typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3)
        ?? "1.0.0";

    // The newer version's number, or null when up to date (or not installed).
    public async Task<string?> CheckAsync()
    {
        if (!IsInstalled)
            return null;
        _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pending?.TargetFullRelease.Version.ToString();
    }

    // Downloads the update found by CheckAsync, then restarts into it.
    public async Task InstallAndRestartAsync(Action<int>? progress = null)
    {
        if (_pending is null)
            return;
        await _manager.DownloadUpdatesAsync(_pending, progress, CancellationToken.None).ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease, Array.Empty<string>());
    }
}
