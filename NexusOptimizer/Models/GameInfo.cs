using System.IO;

namespace NexusOptimizer.Models;

public sealed class GameInfo
{
    public string Name { get; init; } = "";
    public string Platform { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public string? ExecutablePath { get; init; }
    public string Status => Directory.Exists(InstallPath) ? "Найдена" : "Путь недоступен";
    public string DisplayName => Name;
    public bool IsInstalled =>
        !string.IsNullOrWhiteSpace(InstallPath)
        && System.IO.File.Exists(InstallPath);
}
