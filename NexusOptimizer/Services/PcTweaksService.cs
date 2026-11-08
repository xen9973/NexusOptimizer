using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NexusOptimizer.Services;

public sealed record PcTweakResult(string Id, bool Success, string Message);

public sealed class PcTweaksService
{
    private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private readonly PcTweaksStore _store;

    public PcTweaksService(PcTweaksStore? store = null) => _store = store ?? new PcTweaksStore();

    public PcTweaksState LoadState() => _store.Load();

    public HashSet<string> ReadEnabled(
        PcTweaksState state,
        out IReadOnlyList<PcTweakResult> errors,
        out IReadOnlySet<string> unavailableIds)
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        var readErrors = new List<PcTweakResult>();
        var unavailable = new HashSet<string>(StringComparer.Ordinal);

        foreach (PcTweakDefinition tweak in PcTweaksCatalog.Tweaks)
        {
            try
            {
                if (tweak.ServiceName is not null && !IsServiceInstalled(tweak.RegistryPath!))
                {
                    unavailable.Add(tweak.Id);
                    continue;
                }

                if (tweak.Id == "power-plan")
                {
                    string? active = GetActivePowerScheme();
                    if (active is null)
                        readErrors.Add(new PcTweakResult(tweak.Id, false, "Не удалось определить активную схему питания."));
                    else if (string.Equals(active, HighPerformanceScheme, StringComparison.OrdinalIgnoreCase))
                        enabled.Add(tweak.Id);
                    continue;
                }

                RegistryHive hive = tweak.Scope == PcTweakScope.WholeComputer
                    ? RegistryHive.LocalMachine
                    : RegistryHive.CurrentUser;

                if (Equals(ReadRegistryValue(hive, tweak.RegistryPath!, tweak.ValueName!, out _), tweak.EnabledValue))
                    enabled.Add(tweak.Id);
            }
            catch (Exception ex) when (IsRegistryOrProcessError(ex))
            {
                AppLog.Warn($"Не удалось прочитать состояние твика {tweak.Id}", ex);
                readErrors.Add(new PcTweakResult(tweak.Id, false, ex.Message));
            }
        }

        errors = readErrors;
        unavailableIds = unavailable;
        return enabled;
    }

    private static bool IsServiceInstalled(string registryPath)
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? key = root.OpenSubKey(registryPath);
        return key is not null && key.GetValue("Start") is not null;
    }

    public IReadOnlyList<PcTweakResult> ApplyUserTweaks(
        PcTweaksState state,
        IReadOnlySet<string> selectedIds,
        IReadOnlySet<string>? skipIds = null,
        bool forceRestore = false)
    {
        var results = new List<PcTweakResult>();

        foreach (PcTweakDefinition tweak in PcTweaksCatalog.Tweaks.Where(item =>
                     item.Scope == PcTweakScope.CurrentUser && item.Id != "power-plan"
                     && skipIds?.Contains(item.Id) != true))
        {
            ApplyRegistryTweak(tweak, RegistryHive.CurrentUser, state, selectedIds.Contains(tweak.Id), results, forceRestore);
        }

        SaveState(state, results);
        return results;
    }

    public IReadOnlyList<PcTweakResult> ApplySelectedUserTweaks(
        PcTweaksState state,
        IReadOnlySet<string> selectedIds)
    {
        var results = new List<PcTweakResult>();

        foreach (PcTweakDefinition tweak in PcTweaksCatalog.Tweaks.Where(item =>
                     item.Scope == PcTweakScope.CurrentUser
                     && item.Id != "power-plan"
                     && selectedIds.Contains(item.Id)))
        {
            ApplyRegistryTweak(tweak, RegistryHive.CurrentUser, state, selected: true, results, forceRestore: false);
        }

        SaveState(state, results);
        return results;
    }

    public IReadOnlyList<PcTweakResult> ApplyMachineTweaks(
        PcTweaksState state,
        IReadOnlySet<string> selectedIds,
        IReadOnlySet<string>? skipIds = null,
        bool forceRestore = false)
    {
        var results = new List<PcTweakResult>();

        foreach (PcTweakDefinition tweak in PcTweaksCatalog.Tweaks.Where(item =>
                     item.Scope == PcTweakScope.WholeComputer && item.Id != "power-plan"
                     && skipIds?.Contains(item.Id) != true))
        {
            ApplyRegistryTweak(tweak, RegistryHive.LocalMachine, state, selectedIds.Contains(tweak.Id), results, forceRestore);
        }

        SaveState(state, results);
        return results;
    }

    public IReadOnlyList<PcTweakResult> ApplyPowerPlan(PcTweaksState state, bool selected)
    {
        const string id = "power-plan";

        try
        {
            string? current = GetActivePowerScheme();

            if (selected)
            {
                if (string.Equals(current, HighPerformanceScheme, StringComparison.OrdinalIgnoreCase))
                    return [new PcTweakResult(id, true, "Схема «Высокая производительность» уже включена.")];

                if (current is null)
                    return [new PcTweakResult(id, false, "Не удалось определить текущую схему питания.")];

                if (!IsPowerSchemeAvailable(HighPerformanceScheme))
                    return [new PcTweakResult(id, false, "Схема «Высокая производительность» недоступна на этом компьютере.")];

                state.OriginalPowerScheme ??= current;
                _store.Save(state);

                if (!SetActivePowerScheme(HighPerformanceScheme))
                    return [new PcTweakResult(id, false, "Windows не смогла переключить схему питания.")];

                return [new PcTweakResult(id, true, "Включена схема «Высокая производительность».")];
            }

            if (state.OriginalPowerScheme is null)
                return [new PcTweakResult(id, true, "Схема питания не менялась программой.")];

            string original = state.OriginalPowerScheme;
            if (string.Equals(current, HighPerformanceScheme, StringComparison.OrdinalIgnoreCase))
            {
                if (!SetActivePowerScheme(original))
                    return [new PcTweakResult(id, false, "Не удалось вернуть исходную схему питания.")];

                state.OriginalPowerScheme = null;
                _store.Save(state);
                return [new PcTweakResult(id, true, "Исходная схема питания восстановлена.")];
            }

            state.OriginalPowerScheme = null;
            _store.Save(state);
            return [new PcTweakResult(id, true, "Схема не менялась: сохранён выбор пользователя.")];
        }
        catch (Exception ex) when (IsRegistryOrProcessError(ex))
        {
            AppLog.Warn("Не удалось изменить схему питания", ex);
            return [new PcTweakResult(id, false, ex.Message)];
        }
    }

    private void ApplyRegistryTweak(
        PcTweakDefinition tweak,
        RegistryHive hive,
        PcTweaksState state,
        bool selected,
        List<PcTweakResult> results,
        bool forceRestore)
    {
        try
        {
            if (selected)
            {
                if (!state.RegistryBackups.ContainsKey(tweak.Id))
                {
                    object? current = ReadRegistryValue(hive, tweak.RegistryPath!, tweak.ValueName!, out RegistryValueKind? currentKind);
                    if (currentKind == tweak.ValueKind && Equals(current, tweak.EnabledValue))
                    {
                        results.Add(new PcTweakResult(tweak.Id, true, "Настройка уже находится в требуемом состоянии; исходное значение не менялось."));
                        return;
                    }

                    state.RegistryBackups[tweak.Id] = CaptureValue(hive, tweak.RegistryPath!, tweak.ValueName!);
                    _store.Save(state);
                }

                WriteRegistryValue(hive, tweak.RegistryPath!, tweak.ValueName!, tweak.EnabledValue!, tweak.ValueKind!.Value);
                results.Add(new PcTweakResult(tweak.Id, true, "Настройка применена."));
            }
            else if (state.RegistryBackups.TryGetValue(tweak.Id, out PcTweakBackup? backup))
            {
                if (backup.Hive != hive
                    || !string.Equals(backup.RegistryPath, tweak.RegistryPath, StringComparison.Ordinal)
                    || !string.Equals(backup.ValueName, tweak.ValueName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Резервная копия не соответствует этому твику; восстановление остановлено.");
                }

                if (!forceRestore)
                {
                    object? current = ReadRegistryValue(hive, tweak.RegistryPath!, tweak.ValueName!, out RegistryValueKind? currentKind);
                    if (currentKind != tweak.ValueKind || !Equals(current, tweak.EnabledValue))
                    {
                        throw new InvalidDataException("Значение изменено вне программы. Резервная копия сохранена, чтобы не перезаписать чужое изменение.");
                    }
                }

                RestoreValue(backup);
                state.RegistryBackups.Remove(tweak.Id);
                _store.Save(state);
                results.Add(new PcTweakResult(tweak.Id, true, "Исходное значение восстановлено."));
            }
        }
        catch (Exception ex) when (IsRegistryOrProcessError(ex))
        {
            AppLog.Warn($"Не удалось применить твик {tweak.Id}", ex);
            results.Add(new PcTweakResult(tweak.Id, false, ex.Message));
        }
    }

    private void SaveState(PcTweaksState state, List<PcTweakResult> results)
    {
        try
        {
            _store.Save(state);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось сохранить резервные копии твиков", ex);
            results.Add(new PcTweakResult("backup", false, "Не удалось сохранить резервные копии: " + ex.Message));
        }
    }

    private static PcTweakBackup CaptureValue(RegistryHive hive, string path, string name)
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using RegistryKey? key = root.OpenSubKey(path);

        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            return new PcTweakBackup { Hive = hive, RegistryPath = path, ValueName = name };

        RegistryValueKind kind = key.GetValueKind(name);
        object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new InvalidOperationException("Не удалось прочитать исходное значение реестра.");

        return new PcTweakBackup
        {
            Hive = hive,
            RegistryPath = path,
            ValueName = name,
            Existed = true,
            ValueKind = kind,
            Value = JsonSerializer.SerializeToElement(value, value.GetType())
        };
    }

    private static object? ReadRegistryValue(RegistryHive hive, string path, string name, out RegistryValueKind? kind)
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using RegistryKey? key = root.OpenSubKey(path);

        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            kind = null;
            return null;
        }

        kind = key.GetValueKind(name);
        return key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    private static void WriteRegistryValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind)
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using RegistryKey key = root.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException("Не удалось создать раздел реестра.");
        key.SetValue(name, value, kind);
    }

    private static void RestoreValue(PcTweakBackup backup)
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(backup.Hive, RegistryView.Registry64);

        if (backup.Existed)
        {
            if (backup.Value is null)
                throw new InvalidDataException("В резервной копии отсутствует исходное значение.");

            object value = backup.ValueKind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => backup.Value.Value.GetString()!,
                RegistryValueKind.DWord => backup.Value.Value.GetInt32(),
                RegistryValueKind.QWord => backup.Value.Value.GetInt64(),
                RegistryValueKind.MultiString => backup.Value.Value.Deserialize<string[]>() ?? [],
                RegistryValueKind.Binary => backup.Value.Value.Deserialize<byte[]>() ?? [],
                _ => throw new InvalidDataException($"Неподдерживаемый тип значения: {backup.ValueKind}.")
            };

            using RegistryKey key = root.CreateSubKey(backup.RegistryPath, writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть раздел реестра для восстановления.");
            key.SetValue(backup.ValueName, value, backup.ValueKind);
            return;
        }

        using (RegistryKey? key = root.OpenSubKey(backup.RegistryPath, writable: true))
        {
            if (key is null)
                return;

            key.DeleteValue(backup.ValueName, throwOnMissingValue: false);
            if (key.ValueCount == 0 && key.SubKeyCount == 0)
                root.DeleteSubKey(backup.RegistryPath, throwOnMissingSubKey: false);
        }
    }

    private static string? GetActivePowerScheme()
    {
        (int exitCode, string output) = RunPowerCfg("/getactivescheme");
        if (exitCode != 0)
            return null;

        Match match = GuidRegex.Match(output);
        return match.Success ? match.Value : null;
    }

    private static bool IsPowerSchemeAvailable(string scheme)
    {
        (int exitCode, string output) = RunPowerCfg("/list");
        return exitCode == 0 && output.Contains(scheme, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetActivePowerScheme(string scheme)
    {
        if (!Guid.TryParse(scheme, out Guid parsed))
            return false;

        return RunPowerCfg("/setactive " + parsed).ExitCode == 0;
    }

    private static (int ExitCode, string Output) RunPowerCfg(string arguments)
    {
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null)
            return (-1, string.Empty);

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(5000))
        {
            process.Kill();
            return (-1, output + error);
        }

        return (process.ExitCode, output + error);
    }

    private static bool IsRegistryOrProcessError(Exception ex) =>
        ex is UnauthorizedAccessException or System.Security.SecurityException
            or System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception;
}
