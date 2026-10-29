using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NexusOptimizer.Models;

namespace NexusOptimizer.Services;

public sealed class GameUserData
{
    public List<string> Favorites { get; set; } = [];

    public Dictionary<string, DateTime> LastPlayed { get; set; } = new();
}

/// <summary>Избранное и время последнего запуска. Ключ — путь установки игры, поэтому данные переживают повторное сканирование.</summary>
public sealed class GameUserDataStore
{
    // Поле объявлено раньше Current: конструктор Current уже читает файл с этими настройками.
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static GameUserDataStore Current { get; } = new();

    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);

    private GameUserDataStore()
    {
        Load();
    }

    /// <summary>Переносит сохранённые данные на объекты игр (после загрузки списка или сканирования).</summary>
    public void Apply(IEnumerable<GameInfo> games)
    {
        foreach (GameInfo game in games)
        {
            string key = Key(game);

            game.IsFavorite = _favorites.Contains(key);
            game.LastPlayedUtc = _lastPlayed.TryGetValue(key, out DateTime played)
                ? (DateTime?)played
                : null;
        }
    }

    public void SetFavorite(GameInfo game, bool value)
    {
        string key = Key(game);

        if (value)
            _favorites.Add(key);
        else
            _favorites.Remove(key);

        game.IsFavorite = value;
        Save();
    }

    public void RecordLaunch(GameInfo game)
    {
        DateTime now = DateTime.UtcNow;

        _lastPlayed[Key(game)] = now;
        game.LastPlayedUtc = now;
        Save();
    }

    private static string Key(GameInfo game) => game.InstallPath.Trim();

    private void Load()
    {
        try
        {
            string path = AppPaths.UserDataFile;

            if (!File.Exists(path))
                return;

            GameUserData? data = JsonSerializer.Deserialize<GameUserData>(File.ReadAllText(path), Options);

            if (data is null)
                return;

            foreach (string favorite in data.Favorites ?? [])
                _favorites.Add(favorite);

            foreach (KeyValuePair<string, DateTime> entry in data.LastPlayed ?? new Dictionary<string, DateTime>())
                _lastPlayed[entry.Key] = entry.Value;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось прочитать избранное и историю запусков", ex);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);

            var data = new GameUserData
            {
                Favorites = _favorites.ToList(),
                LastPlayed = new Dictionary<string, DateTime>(_lastPlayed)
            };

            string path = AppPaths.UserDataFile;
            string temp = path + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось сохранить избранное и историю запусков", ex);
        }
    }
}
