using System;
using System.IO;
using System.Text.Json;

namespace NexusOptimizer.Services;

public sealed class AppPreferences
{
    public string DefaultGameProfile { get; set; } = OptimizationCatalog.StandardProfile;
    public string Theme { get; set; } = AppThemeService.DarkTheme;
    public bool HasCompletedWelcome { get; set; }
}

/// <summary>Общие настройки приложения, независимые от настроек отдельных игр.</summary>
public sealed class AppPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string? _filePath;

    public AppPreferencesStore(string? filePath = null) => _filePath = filePath;

    private string FilePath => _filePath ?? AppPaths.AppPreferencesFile;

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppPreferences();

            AppPreferences? preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath));

            if (preferences is null
                || !IsSupportedProfile(preferences.DefaultGameProfile)
                || !AppThemeService.IsSupportedTheme(preferences.Theme))
            {
                AppLog.Warn("Некорректные общие настройки, используются значения по умолчанию.", null);
                return new AppPreferences();
            }

            return preferences;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось прочитать общие настройки приложения", ex);
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!IsSupportedProfile(preferences.DefaultGameProfile)
            || !AppThemeService.IsSupportedTheme(preferences.Theme))
        {
            throw new ArgumentException("Некорректные общие настройки приложения.", nameof(preferences));
        }

        string directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("Не удалось определить папку настроек приложения.");
        Directory.CreateDirectory(directory);

        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(preferences, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    public void SaveTheme(string theme)
    {
        if (!AppThemeService.IsSupportedTheme(theme))
            throw new ArgumentException("Неизвестная тема приложения.", nameof(theme));

        AppPreferences preferences = Load();
        preferences.Theme = theme;
        Save(preferences);
    }

    public void SaveWelcomeCompleted()
    {
        AppPreferences preferences = Load();
        preferences.HasCompletedWelcome = true;
        Save(preferences);
    }

    private static bool IsSupportedProfile(string? name) =>
        name is OptimizationCatalog.StandardProfile
            or OptimizationCatalog.BalancedProfile
            or OptimizationCatalog.PerformanceProfile;
}
