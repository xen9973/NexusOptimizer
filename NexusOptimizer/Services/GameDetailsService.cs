
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NexusOptimizer.Services;

public sealed class GameDetailsService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public async Task<(string Description, string? AppId)> GetDetailsAsync(
        string gameName,
        string installPath)
    {
        string? appId = FindSteamAppId(installPath);

        if (appId is null)
        {
            return (
                "Автоматическое получение описания пока доступно только для игр Steam. " +
                "Вы можете запустить игру и просмотреть её локальные файлы.",
                null);
        }

        try
        {
            string url =
                $"https://store.steampowered.com/api/appdetails?appids={appId}&l=russian";

            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            if (!json.RootElement.TryGetProperty(appId, out var entry) ||
                !entry.TryGetProperty("success", out var success) ||
                !success.GetBoolean() ||
                !entry.TryGetProperty("data", out var data))
            {
                return ("Steam не предоставил описание этой игры.", appId);
            }

            string description = "";

            if (data.TryGetProperty("about_the_game", out var about))
                description = about.GetString() ?? "";

            if (string.IsNullOrWhiteSpace(description) &&
                data.TryGetProperty("short_description", out var shortDescription))
            {
                description = shortDescription.GetString() ?? "";
            }

            description = Regex.Replace(description, "<.*?>", " ");
            description = System.Net.WebUtility.HtmlDecode(description);
            description = Regex.Replace(description, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(description))
                description = "Описание для этой игры отсутствует.";

            return (description, appId);
        }
        catch
        {
            return (
                "Не удалось загрузить описание. Проверьте подключение к интернету " +
                "и попробуйте открыть окно игры позже.",
                appId);
        }
    }

    private static string? FindSteamAppId(string installPath)
    {
        try
        {
            var commonFolder = Directory.GetParent(installPath);
            var steamAppsFolder = commonFolder?.Parent;

            if (commonFolder is null ||
                steamAppsFolder is null ||
                !Directory.Exists(steamAppsFolder.FullName))
            {
                return null;
            }

            string installDirectoryName = Path.GetFileName(
                installPath.TrimEnd(Path.DirectorySeparatorChar));

            foreach (string manifest in Directory.GetFiles(
                         steamAppsFolder.FullName, "appmanifest_*.acf"))
            {
                string text = File.ReadAllText(manifest);

                var installDirMatch = Regex.Match(
                    text,
                    "\"installdir\"\\s*\"([^\"]+)\"");

                if (!installDirMatch.Success)
                    continue;

                if (!string.Equals(
                        installDirMatch.Groups[1].Value,
                        installDirectoryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(manifest);
                string id = fileName["appmanifest_".Length..];

                if (id.All(char.IsDigit))
                    return id;
            }
        }
        catch
        {
            // Ошибка чтения манифеста не должна блокировать окно игры.
        }

        return null;
    }
}
