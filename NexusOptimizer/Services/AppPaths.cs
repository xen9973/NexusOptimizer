using System.IO;

namespace NexusOptimizer.Services;

/// <summary>Все пути, в которые приложение пишет свои данные (%AppData%\NexusOptimizer).</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NexusOptimizer");

    public static string LibraryFile => Path.Combine(Root, "library.json");

    public static string GameSettingsDirectory => Path.Combine(Root, "GameSettings");

    public static string DetailsCacheDirectory => Path.Combine(Root, "cache", "details");

    public static string LogFile => Path.Combine(Root, "logs", "app.log");
}
