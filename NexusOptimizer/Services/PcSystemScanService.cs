using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NexusOptimizer.Services;

public sealed record PcSystemRecommendation(PcTweakDefinition Tweak, string Reason);

public sealed record PcSystemScanReport(
    string WindowsVersion,
    string ProcessorName,
    int LogicalProcessorCount,
    ulong TotalPhysicalMemoryBytes,
    IReadOnlyList<string> GraphicsAdapters,
    IReadOnlyList<PcSystemRecommendation> Recommendations,
    IReadOnlyList<string> Warnings);

public sealed class PcSystemScanService
{
    private const ulong LowMemoryThresholdBytes = 8UL * 1024 * 1024 * 1024;
    private const string DisplayAdapterRegistryPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private readonly PcTweaksService _tweaksService;

    public PcSystemScanService(PcTweaksService? tweaksService = null) =>
        _tweaksService = tweaksService ?? new PcTweaksService();

    public PcSystemScanReport Scan(Action<string>? reportProgress = null)
    {
        var warnings = new List<string>();

        reportProgress?.Invoke("Проверяем Windows...");
        string windowsVersion = ReadWindowsVersion(warnings);

        reportProgress?.Invoke("Определяем процессор...");
        string processorName = ReadProcessorName(warnings);
        int logicalProcessorCount = Environment.ProcessorCount;

        reportProgress?.Invoke("Измеряем объём оперативной памяти...");
        ulong totalPhysicalMemoryBytes = ReadTotalPhysicalMemory();

        reportProgress?.Invoke("Ищем графические адаптеры...");
        IReadOnlyList<string> graphicsAdapters = ReadGraphicsAdapters(warnings);

        reportProgress?.Invoke("Проверяем текущие настройки Windows...");
        PcTweaksState state = _tweaksService.LoadState();
        HashSet<string> enabled = _tweaksService.ReadEnabled(
            state,
            out IReadOnlyList<PcTweakResult> readErrors,
            out IReadOnlySet<string> unavailableIds);

        warnings.AddRange(readErrors.Select(error =>
            $"{GetTweakName(error.Id)}: {error.Message}"));

        IReadOnlySet<string> readErrorIds = readErrors
            .Select(error => error.Id)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<PcSystemRecommendation> recommendations = BuildRecommendations(
            totalPhysicalMemoryBytes,
            enabled,
            unavailableIds,
            readErrorIds);

        reportProgress?.Invoke("Анализ завершён.");
        return new PcSystemScanReport(
            windowsVersion,
            processorName,
            logicalProcessorCount,
            totalPhysicalMemoryBytes,
            graphicsAdapters,
            recommendations,
            warnings);
    }

    internal static IReadOnlyList<PcSystemRecommendation> BuildRecommendations(
        ulong totalPhysicalMemoryBytes,
        IReadOnlySet<string> enabledIds,
        IReadOnlySet<string> unavailableIds,
        IReadOnlySet<string> readErrorIds)
    {
        var recommendations = new List<PcSystemRecommendation>();

        AddRecommendation(
            "game-mode",
            "Игровой режим Windows сейчас не включён. Его можно включить для приоритизации игры средствами Windows; эффект зависит от игры и драйверов.");
        AddRecommendation(
            "game-capture",
            "Фоновая запись игр не отключена. Если вы не пользуетесь автоматической записью клипов, её отключение уберёт эту фоновую функцию.");

        if (totalPhysicalMemoryBytes <= LowMemoryThresholdBytes)
        {
            AddRecommendation(
                "window-animations",
                "На компьютере с небольшим объёмом ОЗУ можно упростить анимацию интерфейса. Это в основном визуальная настройка и не гарантирует прирост FPS.");
            AddRecommendation(
                "transparency-effects",
                "Отключение прозрачности упрощает визуальные эффекты Windows. Возможная польза зависит от видеосистемы и обычно невелика.");
        }

        return recommendations;

        void AddRecommendation(string id, string reason)
        {
            PcTweakDefinition? tweak = PcTweaksCatalog.Tweaks.FirstOrDefault(item => item.Id == id);
            if (tweak is null
                || tweak.Scope != PcTweakScope.CurrentUser
                || tweak.ServiceName is not null
                || enabledIds.Contains(id)
                || unavailableIds.Contains(id)
                || readErrorIds.Contains(id))
            {
                return;
            }

            recommendations.Add(new PcSystemRecommendation(tweak, reason));
        }
    }

    private static string ReadWindowsVersion(List<string> warnings)
    {
        try
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string? productName = key?.GetValue("ProductName") as string;
            string? displayVersion = key?.GetValue("DisplayVersion") as string;
            if (!string.IsNullOrWhiteSpace(productName))
                return string.IsNullOrWhiteSpace(displayVersion)
                    ? productName
                    : $"{productName} ({displayVersion})";
        }
        catch (Exception ex) when (IsRegistryReadError(ex))
        {
            AppLog.Warn("Не удалось определить версию Windows для системного сканирования", ex);
            warnings.Add("Не удалось определить версию Windows.");
        }

        return RuntimeInformation.OSDescription;
    }

    private static string ReadProcessorName(List<string> warnings)
    {
        try
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = root.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string? name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        catch (Exception ex) when (IsRegistryReadError(ex))
        {
            AppLog.Warn("Не удалось определить процессор для системного сканирования", ex);
        }

        warnings.Add("Не удалось определить модель процессора.");
        return "Не удалось определить";
    }

    private static ulong ReadTotalPhysicalMemory()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось определить объём оперативной памяти.");

        return status.TotalPhysical;
    }

    private static IReadOnlyList<string> ReadGraphicsAdapters(List<string> warnings)
    {
        var adapters = new List<string>();

        try
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? displayClass = root.OpenSubKey(DisplayAdapterRegistryPath);
            if (displayClass is null)
            {
                warnings.Add("Не удалось найти сведения о графических адаптерах.");
                return adapters;
            }

            foreach (string subKeyName in displayClass.GetSubKeyNames())
            {
                using RegistryKey? adapter = displayClass.OpenSubKey(subKeyName);
                string? name = adapter?.GetValue("DriverDesc") as string
                    ?? adapter?.GetValue("Device Description") as string;
                if (!string.IsNullOrWhiteSpace(name) && !adapters.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                    adapters.Add(name.Trim());
            }
        }
        catch (Exception ex) when (IsRegistryReadError(ex))
        {
            AppLog.Warn("Не удалось определить графические адаптеры для системного сканирования", ex);
            warnings.Add("Не удалось полностью прочитать сведения о графических адаптерах.");
        }

        if (adapters.Count == 0)
            warnings.Add("Не удалось определить модель видеокарты.");

        return adapters;
    }

    private static string GetTweakName(string id) =>
        PcTweaksCatalog.Tweaks.FirstOrDefault(tweak => tweak.Id == id)?.Name ?? id;

    private static bool IsRegistryReadError(Exception ex) =>
        ex is UnauthorizedAccessException or System.Security.SecurityException or
            System.IO.IOException or ArgumentException;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
