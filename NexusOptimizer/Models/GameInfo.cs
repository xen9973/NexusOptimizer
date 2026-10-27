using System.IO;
using System.Text.Json.Serialization;

namespace NexusOptimizer.Models;

public sealed class GameInfo
{
    private bool? _isAvailable;

    public string Name { get; init; } = "";
    public string Platform { get; init; } = "";
    public string InstallPath { get; init; } = "";

    /// <summary>Основной exe игры, если его удалось определить.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Идентификатор в лаунчере: appid для Steam, AppName для Epic Games.</summary>
    public string? AppId { get; init; }

    /// <summary>Существует ли папка игры. Проверяется один раз и запоминается.</summary>
    [JsonIgnore]
    public bool IsAvailable => _isAvailable ??= Directory.Exists(InstallPath);

    [JsonIgnore]
    public string Status => IsAvailable ? "Найдена" : "Путь недоступен";
}
