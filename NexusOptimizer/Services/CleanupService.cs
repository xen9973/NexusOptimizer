using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NexusOptimizer.Services;

public enum CleanupKind
{
    Folders,
    RecycleBin
}

public sealed class CleanupDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    public CleanupKind Kind { get; init; } = CleanupKind.Folders;
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>Файлы и папки новее этого возраста не трогаем (они могут быть нужны запущенным программам).</summary>
    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;

    /// <summary>Папка должна называться как временная (temp/tmp): защита от неверно настроенной переменной TEMP.</summary>
    public bool RequireTempLikeName { get; init; }

    public bool SelectedByDefault { get; init; }
}

public readonly record struct CleanupSize(long Bytes, int Files);

public readonly record struct CleanupOutcome(long FreedBytes, int DeletedFiles, int SkippedFiles);

/// <summary>
/// Поиск и удаление временных файлов. Работает только с заранее известными папками,
/// не заходит в ссылки и точки соединения, не трогает скрытые и системные файлы.
/// </summary>
public sealed class CleanupService
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
    };

    public IReadOnlyList<CleanupDefinition> Definitions { get; } = BuildDefinitions();

    private static IReadOnlyList<CleanupDefinition> BuildDefinitions()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return
        [
            new CleanupDefinition
            {
                Id = "user-temp",
                Title = "Временные файлы пользователя",
                Description = "Папка TEMP: остатки установщиков и программ. Удаляются файлы, которые не менялись больше суток.",
                Folders = [Path.GetTempPath()],
                MinAge = TimeSpan.FromHours(24),
                RequireTempLikeName = true,
                SelectedByDefault = true
            },
            new CleanupDefinition
            {
                Id = "windows-temp",
                Title = "Временные файлы Windows",
                Description = "Папка Windows\\Temp. Без прав администратора часть файлов будет пропущена.",
                Folders = [Path.Combine(windows, "Temp")],
                MinAge = TimeSpan.FromHours(24),
                SelectedByDefault = true
            },
            new CleanupDefinition
            {
                Id = "shader-cache",
                Title = "Кэш шейдеров видеокарты",
                Description = "Кэш DirectX, NVIDIA и AMD. Драйвер создаст его заново, поэтому в первых запусках игр возможны недолгие подтормаживания.",
                Folders =
                [
                    Path.Combine(local, "D3DSCache"),
                    Path.Combine(local, "NVIDIA", "DXCache"),
                    Path.Combine(local, "NVIDIA", "GLCache"),
                    Path.Combine(common, "NVIDIA Corporation", "NV_Cache"),
                    Path.Combine(local, "AMD", "DxCache"),
                    Path.Combine(local, "AMD", "GLCache"),
                    Path.Combine(local, "AMD", "VkCache")
                ]
            },
            new CleanupDefinition
            {
                Id = "crash-dumps",
                Title = "Дампы после сбоев",
                Description = "Файлы, которые Windows сохраняет при аварийном завершении программ.",
                Folders = [Path.Combine(local, "CrashDumps")],
                SelectedByDefault = true
            },
            new CleanupDefinition
            {
                Id = "recycle-bin",
                Title = "Корзина",
                Description = "Файлы из корзины удаляются безвозвратно.",
                Kind = CleanupKind.RecycleBin
            }
        ];
    }

    public Task<CleanupSize> MeasureAsync(CleanupDefinition definition, CancellationToken token) =>
        Task.Run(() => Measure(definition, token), token);

    public Task<CleanupOutcome> CleanAsync(CleanupDefinition definition, CancellationToken token) =>
        Task.Run(() => Clean(definition, token), token);

    // ───────────────────────── Размер ─────────────────────────

    private static CleanupSize Measure(CleanupDefinition definition, CancellationToken token)
    {
        if (definition.Kind == CleanupKind.RecycleBin)
            return MeasureRecycleBin();

        long bytes = 0;
        int files = 0;

        foreach (string folder in ValidFolders(definition))
        {
            foreach (FileInfo file in GetEligibleFiles(folder, definition.MinAge, token))
            {
                bytes += SafeLength(file);
                files++;
            }
        }

        return new CleanupSize(bytes, files);
    }

    // ───────────────────────── Удаление ─────────────────────────

    private static CleanupOutcome Clean(CleanupDefinition definition, CancellationToken token)
    {
        if (definition.Kind == CleanupKind.RecycleBin)
            return CleanRecycleBin();

        long freed = 0;
        int deleted = 0;
        int skipped = 0;

        foreach (string folder in ValidFolders(definition))
        {
            foreach (FileInfo file in GetEligibleFiles(folder, definition.MinAge, token))
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    long length = file.Length;

                    if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                        file.Attributes = FileAttributes.Normal;

                    file.Delete();

                    freed += length;
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Файл занят программой или нет прав. Это нормально для TEMP.
                    skipped++;
                }
            }

            DeleteEmptyDirectories(folder, definition.MinAge, token);
        }

        return new CleanupOutcome(freed, deleted, skipped);
    }

    private static void DeleteEmptyDirectories(string root, TimeSpan minAge, CancellationToken token)
    {
        try
        {
            DateTime threshold = DateTime.UtcNow - minAge;

            List<DirectoryInfo> directories = new DirectoryInfo(root)
                .EnumerateDirectories("*", Options)
                .OrderByDescending(directory => directory.FullName.Length)
                .ToList();

            foreach (DirectoryInfo directory in directories)
            {
                token.ThrowIfCancellationRequested();

                if (minAge > TimeSpan.Zero && directory.LastWriteTimeUtc > threshold)
                    continue;

                try
                {
                    // Без recursive: непустая или занятая папка просто не удалится.
                    directory.Delete(recursive: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Очистка: не удалось убрать пустые папки в {root}", ex);
        }
    }

    // ───────────────────────── Вспомогательное ─────────────────────────

    private static IEnumerable<string> ValidFolders(CleanupDefinition definition)
    {
        foreach (string folder in definition.Folders)
        {
            if (!Directory.Exists(folder))
                continue;

            if (!IsSafeRoot(folder, definition))
            {
                AppLog.Warn($"Очистка: папка пропущена как небезопасная: {folder}");
                continue;
            }

            yield return folder;
        }
    }

    private static bool IsSafeRoot(string folder, CleanupDefinition definition)
    {
        try
        {
            char[] separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

            string full = Path.GetFullPath(folder).TrimEnd(separators);
            string root = (Path.GetPathRoot(full) ?? string.Empty).TrimEnd(separators);

            if (full.Length == 0 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return false;

            // Папки, которые нельзя чистить целиком, даже если переменная окружения указывает на них.
            string[] forbidden =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            ];

            if (forbidden.Any(path => path.Length > 0
                                      && string.Equals(full, path.TrimEnd(separators), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (definition.RequireTempLikeName)
            {
                string name = Path.GetFileName(full);

                if (!name.Contains("temp", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("tmp", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<FileInfo> GetEligibleFiles(string folder, TimeSpan minAge, CancellationToken token)
    {
        var result = new List<FileInfo>();
        DateTime threshold = DateTime.UtcNow - minAge;

        try
        {
            foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", Options))
            {
                token.ThrowIfCancellationRequested();

                if (minAge > TimeSpan.Zero)
                {
                    DateTime latest = file.LastWriteTimeUtc > file.CreationTimeUtc
                        ? file.LastWriteTimeUtc
                        : file.CreationTimeUtc;

                    if (latest > threshold)
                        continue;
                }

                result.Add(file);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Очистка: не удалось прочитать {folder}", ex);
        }

        return result;
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch { return 0L; }
    }

    // ───────────────────────── Корзина ─────────────────────────

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    private static CleanupSize MeasureRecycleBin()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            int result = SHQueryRecycleBin(null, ref info);

            return result == 0
                ? new CleanupSize(info.i64Size, (int)Math.Min(info.i64NumItems, int.MaxValue))
                : new CleanupSize(0, 0);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Корзина: не удалось получить размер", ex);
            return new CleanupSize(0, 0);
        }
    }

    private static CleanupOutcome CleanRecycleBin()
    {
        CleanupSize before = MeasureRecycleBin();

        if (before.Files == 0)
            return new CleanupOutcome(0, 0, 0);

        try
        {
            int result = SHEmptyRecycleBin(
                IntPtr.Zero,
                null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

            if (result == 0)
                return new CleanupOutcome(before.Bytes, before.Files, 0);

            AppLog.Warn($"Корзина: SHEmptyRecycleBin вернул 0x{result:X8}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Корзина: не удалось очистить", ex);
        }

        return new CleanupOutcome(0, 0, before.Files);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
