using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace NexusOptimizer.Services;

public sealed record PowerResult(bool Acquired, string Message);

/// <summary>
/// Временно включает схему электропитания «Высокая производительность» и возвращает прежнюю.
/// Исходная схема дублируется в файл: если программа завершилась аварийно, при следующем запуске её вернут.
/// </summary>
public sealed class PowerPlanService
{
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private readonly object _gate = new();
    private int _references;
    private string? _originalScheme;

    private static string RecoveryFile => Path.Combine(AppPaths.Root, "power-restore.txt");

    public PowerResult Acquire()
    {
        lock (_gate)
        {
            if (_references > 0)
            {
                _references++;
                return new PowerResult(true, "Схема «Высокая производительность» уже включена другой игрой.");
            }

            string? current = GetActiveScheme();

            if (current is null)
                return new PowerResult(false, "Не удалось определить текущую схему электропитания.");

            if (string.Equals(current, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                return new PowerResult(false, "Схема «Высокая производительность» уже включена, менять её не нужно.");

            if (!SetActiveScheme(HighPerformanceGuid))
                return new PowerResult(false, "Схема «Высокая производительность» недоступна на этом компьютере.");

            _originalScheme = current;
            _references = 1;
            SaveRecovery(current);

            return new PowerResult(true, "Включена схема электропитания «Высокая производительность».");
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (_references == 0)
                return;

            _references--;

            if (_references == 0)
                Restore();
        }
    }

    /// <summary>Немедленно возвращает прежнюю схему (закрытие программы).</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            if (_references == 0)
                return;

            _references = 0;
            Restore();
        }
    }

    /// <summary>Если программа закрылась, не вернув схему, возвращаем её при следующем запуске.</summary>
    public void RestoreLeftover()
    {
        try
        {
            lock (_gate)
            {
                if (_references > 0 || !File.Exists(RecoveryFile))
                    return;

                string text = File.ReadAllText(RecoveryFile).Trim();

                if (Guid.TryParse(text, out Guid original))
                {
                    string? current = GetActiveScheme();

                    if (string.Equals(current, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                        SetActiveScheme(original.ToString());
                }

                DeleteRecovery();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось вернуть схему электропитания после прошлого запуска", ex);
        }
    }

    private void Restore()
    {
        string? original = _originalScheme;
        _originalScheme = null;

        if (original is not null)
        {
            // Возвращаем, только если пользователь сам не выбрал другую схему за время игры.
            string? current = GetActiveScheme();

            if (string.Equals(current, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                SetActiveScheme(original);
        }

        DeleteRecovery();
    }

    // ───────────────────────── powercfg ─────────────────────────

    private static string? GetActiveScheme()
    {
        (int exitCode, string output) = RunPowerCfg("/getactivescheme");

        if (exitCode != 0)
            return null;

        Match match = GuidRegex.Match(output);
        return match.Success ? match.Value : null;
    }

    private static bool SetActiveScheme(string guid)
    {
        // Принимаем только корректный GUID, чтобы в командную строку не попало ничего лишнего.
        if (!Guid.TryParse(guid, out Guid parsed))
            return false;

        (int exitCode, _) = RunPowerCfg("/setactive " + parsed);
        return exitCode == 0;
    }

    private static (int ExitCode, string Output) RunPowerCfg(string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            if (process is null)
                return (-1, string.Empty);

            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                return (-1, output);
            }

            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"powercfg {arguments}", ex);
            return (-1, string.Empty);
        }
    }

    // ───────────────────────── Файл восстановления ─────────────────────────

    private static void SaveRecovery(string scheme)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(RecoveryFile, scheme);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось сохранить файл восстановления электропитания", ex);
        }
    }

    private static void DeleteRecovery()
    {
        try
        {
            if (File.Exists(RecoveryFile))
                File.Delete(RecoveryFile);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось удалить файл восстановления электропитания", ex);
        }
    }
}
