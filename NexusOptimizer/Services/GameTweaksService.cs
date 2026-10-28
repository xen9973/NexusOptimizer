using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace NexusOptimizer.Services;

/// <summary>Сколько из exe игры уже имеют каждую из настроек.</summary>
public sealed record TweakState(int ExecutableCount, int GpuHighPerformance, int FullscreenDisabled)
{
    public bool AllGpuHighPerformance => ExecutableCount > 0 && GpuHighPerformance == ExecutableCount;

    public bool AllFullscreenDisabled => ExecutableCount > 0 && FullscreenDisabled == ExecutableCount;
}

/// <summary>
/// Настройки Windows для конкретного exe, которые действуют без запуска игры.
/// Всё хранится в HKEY_CURRENT_USER, права администратора не нужны, исходные значения запоминаются.
/// </summary>
public sealed class GameTweaksService
{
    // Те же значения, что записывают «Параметры → Графика» и вкладка «Совместимость» в свойствах exe.
    private const string GpuKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string LayersKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    private const string GpuPreferenceName = "GpuPreference";
    private const string GpuHighPerformance = "2";
    private const string FullscreenToken = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    public TweakState ReadState(IReadOnlyList<string> executables)
    {
        int gpu = 0;
        int fullscreen = 0;

        foreach (string exe in executables)
        {
            if (GetGpuPreference(ReadString(GpuKeyPath, exe)) == GpuHighPerformance)
                gpu++;

            if (ParseLayerTokens(ReadString(LayersKeyPath, exe)).Contains(FullscreenToken, StringComparer.OrdinalIgnoreCase))
                fullscreen++;
        }

        return new TweakState(executables.Count, gpu, fullscreen);
    }

    /// <summary>
    /// Приводит реестр в соответствие с настройками. Исходные значения запоминаются в settings.TweakBackups.
    /// </summary>
    /// <returns>Список ошибок; пустой, если всё прошло успешно.</returns>
    public List<string> ApplyProfile(IReadOnlyList<string> executables, GameSettings settings)
    {
        var errors = new List<string>();
        var current = new HashSet<string>(executables.Select(Key), StringComparer.Ordinal);

        // Exe, которых больше нет в списке (игра обновилась): возвращаем им исходное состояние.
        foreach (string staleKey in settings.TweakBackups.Keys.Where(key => !current.Contains(key)).ToList())
        {
            if (TryRestore(settings.TweakBackups[staleKey], errors))
                settings.TweakBackups.Remove(staleKey);
        }

        foreach (string exe in executables)
        {
            try
            {
                if (!settings.TweakBackups.TryGetValue(Key(exe), out ExeTweakBackup? backup))
                {
                    backup = Capture(exe);
                    settings.TweakBackups[Key(exe)] = backup;
                }

                if (settings.HighPerformanceGpu)
                    SetGpuHighPerformance(exe);
                else
                    RestoreGpu(backup);

                if (settings.DisableFullscreenOptimizations)
                    SetFullscreenToken(exe, present: true);
                else
                    RestoreFullscreen(backup);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                AppLog.Warn($"Настройки Windows для {exe}", ex);
                errors.Add($"{System.IO.Path.GetFileName(exe)}: {ex.Message}");
            }
        }

        // Если ничего не включено, исходные значения уже восстановлены и копии не нужны.
        if (!settings.HighPerformanceGpu && !settings.DisableFullscreenOptimizations && errors.Count == 0)
            settings.TweakBackups.Clear();

        return errors;
    }

    /// <summary>Возвращает все изменённые значения реестра к исходным и снимает флаги в settings.</summary>
    public List<string> RevertAll(GameSettings settings)
    {
        var errors = new List<string>();

        foreach (KeyValuePair<string, ExeTweakBackup> entry in settings.TweakBackups.ToList())
        {
            if (TryRestore(entry.Value, errors))
                settings.TweakBackups.Remove(entry.Key);
        }

        settings.HighPerformanceGpu = false;
        settings.DisableFullscreenOptimizations = false;

        return errors;
    }

    // ───────────────────────── Видеокарта ─────────────────────────

    private static void SetGpuHighPerformance(string exe)
    {
        List<KeyValuePair<string, string>> pairs = ParseGpuValue(ReadString(GpuKeyPath, exe));
        int index = pairs.FindIndex(pair => string.Equals(pair.Key, GpuPreferenceName, StringComparison.OrdinalIgnoreCase));
        var updated = new KeyValuePair<string, string>(GpuPreferenceName, GpuHighPerformance);

        if (index >= 0)
            pairs[index] = updated;
        else
            pairs.Add(updated);

        WriteString(GpuKeyPath, exe, string.Concat(pairs.Select(pair => $"{pair.Key}={pair.Value};")));
    }

    private static void RestoreGpu(ExeTweakBackup backup)
    {
        if (backup.HadGpuValue)
            WriteString(GpuKeyPath, backup.Executable, backup.GpuValue ?? string.Empty);
        else
            DeleteValue(GpuKeyPath, backup.Executable);
    }

    private static List<KeyValuePair<string, string>> ParseGpuValue(string? raw)
    {
        var result = new List<KeyValuePair<string, string>>();

        foreach (string part in (raw ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = part.IndexOf('=');

            result.Add(separator > 0
                ? new KeyValuePair<string, string>(part[..separator].Trim(), part[(separator + 1)..].Trim())
                : new KeyValuePair<string, string>(part, string.Empty));
        }

        return result;
    }

    private static string? GetGpuPreference(string? raw)
    {
        return ParseGpuValue(raw)
            .Where(pair => string.Equals(pair.Key, GpuPreferenceName, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .FirstOrDefault();
    }

    // ───────────────────────── Полноэкранные оптимизации ─────────────────────────

    private static void SetFullscreenToken(string exe, bool present)
    {
        List<string> tokens = ParseLayerTokens(ReadString(LayersKeyPath, exe));
        bool has = tokens.Contains(FullscreenToken, StringComparer.OrdinalIgnoreCase);

        if (present == has)
            return;

        if (present)
            tokens.Add(FullscreenToken);
        else
            tokens.RemoveAll(token => string.Equals(token, FullscreenToken, StringComparison.OrdinalIgnoreCase));

        if (tokens.Count == 0)
            DeleteValue(LayersKeyPath, exe);
        else
            WriteString(LayersKeyPath, exe, "~ " + string.Join(' ', tokens));
    }

    private static void RestoreFullscreen(ExeTweakBackup backup) =>
        SetFullscreenToken(backup.Executable, backup.HadFullscreenToken);

    private static List<string> ParseLayerTokens(string? raw)
    {
        return (raw ?? string.Empty)
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token != "~")
            .ToList();
    }

    // ───────────────────────── Резервные копии ─────────────────────────

    private static ExeTweakBackup Capture(string exe)
    {
        string? gpu = ReadString(GpuKeyPath, exe);

        return new ExeTweakBackup
        {
            Executable = exe,
            HadGpuValue = gpu is not null,
            GpuValue = gpu,
            HadFullscreenToken = ParseLayerTokens(ReadString(LayersKeyPath, exe))
                .Contains(FullscreenToken, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool TryRestore(ExeTweakBackup backup, List<string> errors)
    {
        try
        {
            RestoreGpu(backup);
            RestoreFullscreen(backup);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            AppLog.Warn($"Не удалось вернуть настройки Windows для {backup.Executable}", ex);
            errors.Add($"{System.IO.Path.GetFileName(backup.Executable)}: {ex.Message}");
            return false;
        }
    }

    private static string Key(string exe) => exe.ToLowerInvariant();

    // ───────────────────────── Реестр ─────────────────────────

    private static string? ReadString(string keyPath, string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
    }

    private static void WriteString(string keyPath, string valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath)
                                ?? throw new InvalidOperationException("Не удалось открыть раздел реестра.");

        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    private static void DeleteValue(string keyPath, string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
