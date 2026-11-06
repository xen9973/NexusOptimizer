using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexusOptimizer.Models;

namespace NexusOptimizer.Services;

/// <summary>Исходные значения реестра для одного exe: нужны, чтобы вернуть всё «как было».</summary>
public sealed class ExeTweakBackup
{
    public string Executable { get; set; } = "";

    public bool HadGpuValue { get; set; }
    public string? GpuValue { get; set; }

    public bool HadFullscreenToken { get; set; }
}

public sealed class GameSettings
{
    public string LaunchArguments { get; set; } = "";

    /// <summary>Повысить приоритет процессов игры при запуске из программы.</summary>
    public bool AboveNormalPriority { get; set; }

    /// <summary>Предпочитать производительную видеокарту (настройки графики Windows).</summary>
    public bool HighPerformanceGpu { get; set; }

    /// <summary>Отключить полноэкранные оптимизации Windows для exe игры.</summary>
    public bool DisableFullscreenOptimizations { get; set; }

    /// <summary>Включить схему «Высокая производительность» на время игры.</summary>
    public bool HighPerformancePowerPlan { get; set; }

    /// <summary>Ключ — путь к exe в нижнем регистре.</summary>
    public Dictionary<string, ExeTweakBackup> TweakBackups { get; set; } = new();
}

/// <summary>Настройки отдельной игры. Файл на игру, ключ — хэш пути установки.</summary>
public sealed class GameSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string? _directory;

    /// <param name="directory">Папка для файлов; по умолчанию — AppPaths.GameSettingsDirectory (нужна тестам).</param>
    public GameSettingsStore(string? directory = null) => _directory = directory;

    private string DirectoryPath => _directory ?? AppPaths.GameSettingsDirectory;

    /// <summary>
    /// Читает настройки игры. Если основной файл повреждён, берёт предыдущую копию (.bak):
    /// в файле лежат резервные значения реестра, терять их нельзя.
    /// </summary>
    public GameSettings? Load(GameInfo game)
    {
        string path = GetPath(game);

        if (!File.Exists(path))
            return null;

        GameSettings? settings = TryRead(path);

        if (settings is null)
        {
            AppLog.Warn($"Файл настроек игры повреждён, пробуем предыдущую копию: {game.Name}", null);
            settings = TryRead(path + ".bak");
        }

        return settings;
    }

    /// <summary>Пишет во временный файл и подменяет им основной; прежняя версия остаётся в .bak.</summary>
    public void Save(GameInfo game, GameSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);

        string path = GetPath(game);
        string temp = path + ".tmp";

        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));

        if (File.Exists(path))
            File.Replace(temp, path, path + ".bak");
        else
            File.Move(temp, path);
    }

    public void Delete(GameInfo game)
    {
        string path = GetPath(game);

        // Копию .bak тоже убираем: иначе после отката она «воскресила» бы устаревшие резервные значения.
        foreach (string file in new[] { path, path + ".bak", path + ".tmp" })
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private static GameSettings? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            GameSettings? settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path));

            if (settings is not null)
                settings.TweakBackups ??= new Dictionary<string, ExeTweakBackup>();

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Не удалось прочитать {Path.GetFileName(path)}", ex);
            return null;
        }
    }

    private string GetPath(GameInfo game)
    {
        string key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(game.InstallPath.Trim().ToLowerInvariant())));

        return Path.Combine(DirectoryPath, key + ".json");
    }
}
