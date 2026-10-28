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

    public GameSettings? Load(GameInfo game)
    {
        string path = GetPath(game);

        if (!File.Exists(path))
            return null;

        GameSettings? settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path));

        if (settings is not null)
            settings.TweakBackups ??= new Dictionary<string, ExeTweakBackup>();

        return settings;
    }

    public void Save(GameInfo game, GameSettings settings)
    {
        Directory.CreateDirectory(AppPaths.GameSettingsDirectory);
        File.WriteAllText(GetPath(game), JsonSerializer.Serialize(settings, Options));
    }

    public void Delete(GameInfo game)
    {
        string path = GetPath(game);

        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetPath(GameInfo game)
    {
        string key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(game.InstallPath.Trim().ToLowerInvariant())));

        return Path.Combine(AppPaths.GameSettingsDirectory, key + ".json");
    }
}
