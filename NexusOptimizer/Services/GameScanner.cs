using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NexusOptimizer.Models;

namespace NexusOptimizer.Services;

public sealed class GameScanner
{
    // Служебные приложения Steam, которые лежат в steamapps, но играми не являются.
    private static readonly HashSet<string> SteamNonGameIds = new(StringComparer.Ordinal)
    {
        "228980" // Steamworks Common Redistributables
    };

    private static readonly string[] SteamNonGamePrefixes =
    [
        "Proton",
        "Steam Linux Runtime",
        "Steamworks Common Redistributables"
    ];

    // Флаг «установка завершена» в StateFlags манифеста Steam.
    private const int SteamFullyInstalledFlag = 4;

    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameInfo> _games = [];

    public async Task<IReadOnlyList<GameInfo>> ScanAsync(
        IEnumerable<string> extraFolders,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _seen.Clear();
        _games.Clear();

        string[] folders = extraFolders.ToArray();

        return await Task.Run<IReadOnlyList<GameInfo>>(() =>
        {
            progress?.Report("Поиск библиотек Steam…");
            ScanSteam(cancellationToken);

            progress?.Report("Поиск установок Epic Games…");
            ScanEpic(cancellationToken);

            progress?.Report("Проверка добавленных папок…");
            foreach (string folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(folder))
                    ScanExtraFolder(folder, cancellationToken);
            }

            GameInfo[] result = _games
                .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            // Прогреваем иконки и проверку папок здесь, в фоне,
            // чтобы список не тормозил при первой отрисовке.
            progress?.Report("Подготовка иконок…");
            foreach (GameInfo game in result)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = game.IsAvailable;
                GameIconHelper.GetIcon(game, rememberMisses: false);
            }

            return result;
        }, cancellationToken);
    }

    // ───────────────────────── Steam ─────────────────────────

    private void ScanSteam(CancellationToken token)
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string keyPath in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);

                if (key?.GetValue("InstallPath") is string installPath
                    && !string.IsNullOrWhiteSpace(installPath))
                {
                    steamRoots.Add(installPath);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Реестр Steam (HKLM\\{keyPath})", ex);
            }
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");

            if (key?.GetValue("SteamPath") is string steamPath
                && !string.IsNullOrWhiteSpace(steamPath))
            {
                steamRoots.Add(steamPath.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Реестр Steam (HKCU)", ex);
        }

        var visitedLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in steamRoots)
        {
            token.ThrowIfCancellationRequested();

            string steamApps = Path.Combine(root, "steamapps");

            if (!Directory.Exists(steamApps))
                continue;

            if (visitedLibraries.Add(steamApps))
                AddSteamLibrary(steamApps, token);

            string libraryFile = Path.Combine(steamApps, "libraryfolders.vdf");

            if (!File.Exists(libraryFile))
                continue;

            try
            {
                string text = File.ReadAllText(libraryFile);

                foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    string libraryPath = match.Groups[1].Value.Replace("\\\\", "\\");
                    string libraryApps = Path.Combine(libraryPath, "steamapps");

                    if (Directory.Exists(libraryApps) && visitedLibraries.Add(libraryApps))
                        AddSteamLibrary(libraryApps, token);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"libraryfolders.vdf: {libraryFile}", ex);
            }
        }
    }

    private void AddSteamLibrary(string steamApps, CancellationToken token)
    {
        IEnumerable<string> manifests;

        try
        {
            manifests = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf").ToArray();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Библиотека Steam: {steamApps}", ex);
            return;
        }

        foreach (string manifest in manifests)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                string text = File.ReadAllText(manifest);

                string? appId = ReadVdfValue(text, "appid");
                string? name = ReadVdfValue(text, "name");
                string? installDir = ReadVdfValue(text, "installdir");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
                    continue;

                if (IsSteamNonGame(appId, name))
                    continue;

                // Недокачанные и ещё не установленные игры в список не попадают.
                if (int.TryParse(ReadVdfValue(text, "StateFlags"), out int flags)
                    && (flags & SteamFullyInstalledFlag) == 0)
                {
                    continue;
                }

                string gamePath = Path.Combine(steamApps, "common", installDir);

                if (Directory.Exists(gamePath))
                    AddGame(name, Platforms.Steam, gamePath, appId: appId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Манифест Steam: {manifest}", ex);
            }
        }
    }

    private static bool IsSteamNonGame(string? appId, string name)
    {
        if (appId is not null && SteamNonGameIds.Contains(appId))
            return true;

        return SteamNonGamePrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // ───────────────────────── Epic Games ─────────────────────────

    private void ScanEpic(CancellationToken token)
    {
        string manifestsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");

        if (!Directory.Exists(manifestsFolder))
            return;

        string[] files;

        try
        {
            files = Directory.EnumerateFiles(manifestsFolder, "*.item").ToArray();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Манифесты Epic: {manifestsFolder}", ex);
            return;
        }

        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement json = document.RootElement;

                if (!IsEpicGame(json))
                    continue;

                string? appName = GetJsonString(json, "AppName");
                string? name = GetJsonString(json, "DisplayName") ?? appName;
                string? installPath = GetJsonString(json, "InstallLocation");
                string? launchExecutable = GetJsonString(json, "LaunchExecutable");

                if (string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(installPath)
                    || !Directory.Exists(installPath))
                {
                    continue;
                }

                string? executable = string.IsNullOrWhiteSpace(launchExecutable)
                    ? null
                    : Path.Combine(installPath, launchExecutable);

                AddGame(name, Platforms.Epic, installPath, executable, appName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Манифест Epic: {file}", ex);
            }
        }
    }

    private static bool IsEpicGame(JsonElement json)
    {
        if (json.TryGetProperty("bIsIncompleteInstall", out JsonElement incomplete)
            && incomplete.ValueKind == JsonValueKind.True)
        {
            return false;
        }

        // Дополнения (DLC) ссылаются на основную игру другим AppName.
        string? appName = GetJsonString(json, "AppName");
        string? mainGame = GetJsonString(json, "MainGameAppName");

        if (!string.IsNullOrWhiteSpace(mainGame)
            && !string.Equals(mainGame, appName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // У игр среди категорий есть "games"; инструменты и движок её не имеют.
        if (json.TryGetProperty("AppCategories", out JsonElement categories)
            && categories.ValueKind == JsonValueKind.Array
            && categories.GetArrayLength() > 0)
        {
            foreach (JsonElement category in categories.EnumerateArray())
            {
                if (category.ValueKind == JsonValueKind.String
                    && string.Equals(category.GetString(), "games", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    // ───────────────────────── Дополнительные папки ─────────────────────────

    private void ScanExtraFolder(string folder, CancellationToken token)
    {
        int before = _games.Count;

        try
        {
            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                token.ThrowIfCancellationRequested();

                if (LooksLikeGameFolder(child))
                    AddGame(Path.GetFileName(child), Platforms.Folder, child);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Дополнительная папка: {folder}", ex);
        }

        // Пользователь мог выбрать саму папку игры, а не каталог с играми.
        if (_games.Count == before && LooksLikeGameFolder(folder))
        {
            string name = Path.GetFileName(
                folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            AddGame(name, Platforms.Folder, folder);
        }
    }

    private static bool LooksLikeGameFolder(string folder)
    {
        try
        {
            return ExecutableLocator.HasExecutable(folder)
                   || Directory.Exists(Path.Combine(folder, "Content"))
                   || Directory.Exists(Path.Combine(folder, "Binaries"))
                   || Directory.Exists(Path.Combine(folder, "Engine"));
        }
        catch
        {
            return false;
        }
    }

    // ───────────────────────── Общее ─────────────────────────

    private void AddGame(
        string name,
        string platform,
        string path,
        string? executable = null,
        string? appId = null)
    {
        string fullPath = Path.GetFullPath(path);
        string key = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!_seen.Add(key))
            return;

        string? exe = !string.IsNullOrWhiteSpace(executable) && File.Exists(executable)
            ? executable
            : ExecutableLocator.Find(fullPath);

        _games.Add(new GameInfo
        {
            Name = name,
            Platform = platform,
            InstallPath = fullPath,
            ExecutablePath = exe,
            AppId = appId
        });
    }

    private static string? ReadVdfValue(string text, string key)
    {
        Match match = Regex.Match(
            text,
            $"\"{Regex.Escape(key)}\"\\s*\"([^\"]*)\"",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
