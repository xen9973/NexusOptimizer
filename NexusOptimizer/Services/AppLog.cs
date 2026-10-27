using System.IO;
using System.Text;

namespace NexusOptimizer.Services;

/// <summary>Минимальный файловый лог. Сам никогда не бросает исключения.</summary>
public static class AppLog
{
    private const long MaxSizeBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                string path = AppPaths.LogFile;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxSizeBytes)
                    File.Move(path, path + ".old", overwrite: true);

                var line = new StringBuilder();
                line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Append(' ').Append(level)
                    .Append(' ').Append(message);

                if (ex is not null)
                    line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);

                line.AppendLine();
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Лог не должен ломать приложение.
        }
    }
}
