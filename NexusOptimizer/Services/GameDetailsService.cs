using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NexusOptimizer.Models;

namespace NexusOptimizer.Services;

/// <summary>Описание, картинки и метаданные игры со страницы Steam с кэшем на диске.</summary>
public sealed class GameDetailsService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions CacheOptions = new() { WriteIndented = false };

    private const int MaxScreenshots = 12;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NexusOptimizer/1.0");
        return client;
    }

    public async Task<GameDetails> GetDetailsAsync(GameInfo game, CancellationToken cancellationToken = default)
    {
        // Для Steam-игр appid известен из манифеста. Для остальных пробуем найти игру в Steam по названию.
        string? steamAppId = game.Platform == Platforms.Steam && !string.IsNullOrWhiteSpace(game.AppId)
            ? game.AppId
            : null;

        string cacheKey = steamAppId is not null
            ? "steam-" + steamAppId
            : "name-" + HashOf(NormalizeName(game.Name));

        GameDetails? cached = TryReadCache(cacheKey, out bool isFresh);

        if (cached is not null && isFresh)
            return cached;

        try
        {
            string? storeId = steamAppId ?? await FindStoreAppIdAsync(game.Name, cancellationToken);

            if (storeId is null)
            {
                return new GameDetails
                {
                    Notice = "Для этой игры не нашлось страницы в Steam, поэтому описания и картинок нет."
                };
            }

            GameDetails? details = await FetchSteamDetailsAsync(storeId, cancellationToken);

            if (details is null)
            {
                return cached ?? new GameDetails
                {
                    StoreAppId = storeId,
                    StoreUrl = StoreUrl(storeId),
                    Notice = "Steam не отдал описание этой игры. Возможно, страница недоступна в вашем регионе."
                };
            }

            WriteCache(cacheKey, details);
            return details;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Описание игры «{game.Name}»", ex);

            if (cached is not null)
            {
                cached.Notice = "Показаны сохранённые данные: обновить их из Steam не удалось.";
                return cached;
            }

            return new GameDetails
            {
                Notice = "Не удалось загрузить описание. Проверьте подключение к интернету и откройте страницу игры ещё раз."
            };
        }
    }

    // ───────────────────────── Steam ─────────────────────────

    private static async Task<GameDetails?> FetchSteamDetailsAsync(string storeId, CancellationToken token)
    {
        string url = $"https://store.steampowered.com/api/appdetails?appids={storeId}&l=russian";

        using HttpResponseMessage response = await Http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(token);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: token);

        if (!json.RootElement.TryGetProperty(storeId, out JsonElement entry)
            || !entry.TryGetProperty("success", out JsonElement success)
            || success.ValueKind != JsonValueKind.True
            || !entry.TryGetProperty("data", out JsonElement data))
        {
            return null;
        }

        string shortDescription = HtmlToText(GetString(data, "short_description"));
        string description = HtmlToText(GetString(data, "about_the_game"));

        return new GameDetails
        {
            StoreAppId = storeId,
            StoreUrl = StoreUrl(storeId),
            HeaderImageUrl = GetString(data, "header_image"),
            ShortDescription = shortDescription,
            Description = string.IsNullOrWhiteSpace(description) ? shortDescription : description,
            ReleaseDate = data.TryGetProperty("release_date", out JsonElement release)
                ? GetString(release, "date")
                : null,
            Genres = ReadObjectStrings(data, "genres", "description"),
            Developers = ReadStrings(data, "developers"),
            Publishers = ReadStrings(data, "publishers"),
            Screenshots = ReadScreenshots(data),
            FetchedUtc = DateTime.UtcNow
        };
    }

    private static async Task<string?> FindStoreAppIdAsync(string gameName, CancellationToken token)
    {
        string wanted = NormalizeName(gameName);

        if (wanted.Length == 0)
            return null;

        string url = "https://store.steampowered.com/api/storesearch/?term="
                     + Uri.EscapeDataString(gameName)
                     + "&l=english&cc=us";

        using HttpResponseMessage response = await Http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(token);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: token);

        if (!json.RootElement.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            string? type = GetString(item, "type");

            if (type is not null && !string.Equals(type, "app", StringComparison.OrdinalIgnoreCase))
                continue;

            string? name = GetString(item, "name");

            // Берём только точное совпадение названия, чтобы не показать описание другой игры.
            if (name is null || NormalizeName(name) != wanted)
                continue;

            if (item.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long value))
                return value.ToString();
        }

        return null;
    }

    private static string StoreUrl(string storeId) => $"https://store.steampowered.com/app/{storeId}";

    // ───────────────────────── Разбор JSON ─────────────────────────

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static List<string> ReadStrings(JsonElement data, string propertyName)
    {
        var result = new List<string>();

        if (data.TryGetProperty(propertyName, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    result.Add(item.GetString()!);
            }
        }

        return result;
    }

    private static List<string> ReadObjectStrings(JsonElement data, string propertyName, string field)
    {
        var result = new List<string>();

        if (data.TryGetProperty(propertyName, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                string? text = item.ValueKind == JsonValueKind.Object ? GetString(item, field) : null;

                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
            }
        }

        return result;
    }

    private static List<GameScreenshot> ReadScreenshots(JsonElement data)
    {
        var result = new List<GameScreenshot>();

        if (!data.TryGetProperty("screenshots", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement item in array.EnumerateArray())
        {
            string? thumbnail = GetString(item, "path_thumbnail");
            string? full = GetString(item, "path_full");

            if (string.IsNullOrWhiteSpace(thumbnail) && string.IsNullOrWhiteSpace(full))
                continue;

            result.Add(new GameScreenshot
            {
                ThumbnailUrl = thumbnail ?? full ?? "",
                FullUrl = full ?? thumbnail ?? ""
            });

            if (result.Count >= MaxScreenshots)
                break;
        }

        return result;
    }

    // ───────────────────────── Текст ─────────────────────────

    /// <summary>Превращает HTML Steam в обычный текст, сохраняя абзацы и пункты списков.</summary>
    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        string text = Regex.Replace(html, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|h[1-6]|ul|ol|li)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);

        text = text.Replace("\r", "");
        text = Regex.Replace(text, @"[ \t\u00A0]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    private static string NormalizeName(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string HashOf(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    }

    // ───────────────────────── Кэш на диске ─────────────────────────

    private static string CachePath(string key) =>
        Path.Combine(AppPaths.DetailsCacheDirectory, key + ".json");

    private static GameDetails? TryReadCache(string key, out bool isFresh)
    {
        isFresh = false;

        try
        {
            string path = CachePath(key);

            if (!File.Exists(path))
                return null;

            GameDetails? details = JsonSerializer.Deserialize<GameDetails>(File.ReadAllText(path), CacheOptions);

            if (details is null)
                return null;

            isFresh = DateTime.UtcNow - details.FetchedUtc < CacheLifetime;
            return details;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Кэш описаний: {key}", ex);
            return null;
        }
    }

    private static void WriteCache(string key, GameDetails details)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DetailsCacheDirectory);
            File.WriteAllText(CachePath(key), JsonSerializer.Serialize(details, CacheOptions));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось записать кэш описаний: {key}", ex);
        }
    }
}
