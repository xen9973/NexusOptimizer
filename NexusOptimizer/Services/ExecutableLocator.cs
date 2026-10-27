using System.IO;
using NexusOptimizer.Models;

namespace NexusOptimizer.Services;

/// <summary>Единое место, где определяется «главный» exe игры (запуск, иконки, сканер).</summary>
public static class ExecutableLocator
{
    // Служебные exe, которые не являются самой игрой.
    private static readonly string[] IgnoredParts =
    [
        "unins", "uninstall", "setup", "installer", "vcredist", "vc_redist", "redist",
        "dxsetup", "prereq", "crashhandler", "crashreport", "crashpad", "dotnet", "oalinst"
    ];

    public static string? Resolve(GameInfo game)
    {
        if (!string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath))
            return game.ExecutablePath;

        return Find(game.InstallPath);
    }

    public static bool HasExecutable(string folder) => EnumerateExecutables(folder).Length > 0;

    public static string? Find(string installPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
                return null;

            var candidates = EnumerateExecutables(installPath).ToList();

            if (candidates.Count == 0)
            {
                // Некоторые игры держат exe в подпапке (bin, Binaries и т.п.).
                candidates = Directory.EnumerateDirectories(installPath)
                    .Take(20)
                    .SelectMany(directory => EnumerateExecutables(directory))
                    .ToList();
            }

            string folderName = Path.GetFileName(
                installPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return candidates
                .OrderByDescending(path => string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    folderName,
                    StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(SafeLength)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Поиск exe: {installPath}", ex);
            return null;
        }
    }

    private static string[] EnumerateExecutables(string folder)
    {
        try
        {
            return Directory
                .EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(path => !IsIgnored(path))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsIgnored(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return IgnoredParts.Any(part => name.Contains(part));
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0L; }
    }
}
