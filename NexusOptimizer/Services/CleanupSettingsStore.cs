using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NexusOptimizer.Services;

public sealed class CleanupSettings
{
    public List<string> ExcludedFolders { get; set; } = [];
}

/// <summary>Исключения очистки (папки, которые не нужно трогать) между запусками.</summary>
public sealed class CleanupSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public CleanupSettings Load()
    {
        try
        {
            string path = AppPaths.CleanupSettingsFile;

            if (!File.Exists(path))
                return new CleanupSettings();

            CleanupSettings? settings = JsonSerializer.Deserialize<CleanupSettings>(File.ReadAllText(path), Options);

            if (settings is not null)
                settings.ExcludedFolders ??= [];

            return settings ?? new CleanupSettings();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось прочитать настройки очистки", ex);
            return new CleanupSettings();
        }
    }

    public void Save(CleanupSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);

            string path = AppPaths.CleanupSettingsFile;
            string temp = path + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось сохранить настройки очистки", ex);
        }
    }
}
