using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NexusOptimizer.Models;
using System.IO;

namespace NexusOptimizer.Services;

/// <summary>
/// Read-only game discovery. It does not launch games, modify settings, or delete files.
/// </summary>
public sealed class GameScanner
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameInfo> _games = [];

    public async Task<IReadOnlyList<GameInfo>> ScanAsync(
        IEnumerable<string> extraFolders,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _seen.Clear();
        _games.Clear();

        await Task.Run(() =>
        {
            progress?.Report("Поиск библиотек Steam…");
            ScanSteam(cancellationToken);

            progress?.Report("Поиск установок Epic Games…");
            ScanEpic(cancellationToken);

            progress?.Report("Проверка дополнительных папок…");
            foreach (var folder in extraFolders.Where(Directory.Exists))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanExtraFolder(folder, cancellationToken);
            }
        }, cancellationToken);

        return _games.OrderBy(g => g.Name).ToArray();
    }

    private void ScanSteam(CancellationToken token)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[]
        {
            @"SOFTWARE\WOW6432Node\Valve\Steam",
            @"SOFTWARE\Valve\Steam"
        })
        {
            try
            {
                using var reg = Registry.LocalMachine.OpenSubKey(key);
                var install = reg?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(install))
                    roots.Add(install);
            }
            catch { /* Registry access is optional. */ }
        }

        try
        {
            using var reg = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var install = reg?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(install))
                roots.Add(install.Replace('/', Path.DirectorySeparatorChar));
        }
        catch { }

        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            var steamApps = Path.Combine(root, "steamapps");
            if (Directory.Exists(steamApps))
                AddSteamLibrary(steamApps, token);

            var libraryFile = Path.Combine(steamApps, "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;

            try
            {
                var content = File.ReadAllText(libraryFile);
                foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"",
                             RegexOptions.IgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    var apps = Path.Combine(path, "steamapps");
                    if (Directory.Exists(apps))
                        AddSteamLibrary(apps, token);
                }
            }
            catch { }
        }
    }

    private void AddSteamLibrary(string steamApps, CancellationToken token)
    {
        try
        {
            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var text = File.ReadAllText(manifest);
                    var name = ReadVdfValue(text, "name");
                    var installDir = ReadVdfValue(text, "installdir");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
                        continue;

                    var path = Path.Combine(steamApps, "common", installDir);
                    if (Directory.Exists(path))
                        AddGame(name, "Steam", path);
                }
                catch { }
            }
        }
        catch { }
    }

    private void ScanEpic(CancellationToken token)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var manifests = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var json = doc.RootElement;
                    var name = GetJsonString(json, "DisplayName") ?? GetJsonString(json, "AppName");
                    var path = GetJsonString(json, "InstallLocation");
                    var exe = GetJsonString(json, "LaunchExecutable");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) ||
                        !Directory.Exists(path)) continue;

                    AddGame(name, "Epic Games", path,
                        !string.IsNullOrWhiteSpace(exe) ? Path.Combine(path, exe) : null);
                }
                catch { }
            }
        }
        catch { }
    }

    private void ScanExtraFolder(string folder, CancellationToken token)
    {
        // Only inspect direct child folders to avoid an expensive full-disk crawl.
        try
        {
            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(child);
                if (LooksLikeGameFolder(child))
                    AddGame(name, "Дополнительная папка", child);
            }
        }
        catch { }
    }

    private static bool LooksLikeGameFolder(string folder)
    {
        try
        {
            if (Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly).Any())
                return true;

            return Directory.Exists(Path.Combine(folder, "Content")) ||
                   Directory.Exists(Path.Combine(folder, "Binaries")) ||
                   Directory.Exists(Path.Combine(folder, "Engine"));
        }
        catch { return false; }
    }

    private void AddGame(string name, string platform, string path, string? exe = null)
    {
        var fullPath = Path.GetFullPath(path);
        if (!_seen.Add(fullPath)) return;

        exe ??= FindLikelyExecutable(fullPath);
        _games.Add(new GameInfo
        {
            Name = name,
            Platform = platform,
            InstallPath = fullPath,
            ExecutablePath = exe
        });
    }

    private static string? FindLikelyExecutable(string folder)
    {
        try
        {
            var candidates = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileName(p).Equals("unins000.exe", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
        catch { return null; }
    }

    private static string? ReadVdfValue(string text, string key)
    {
        var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? GetJsonString(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
