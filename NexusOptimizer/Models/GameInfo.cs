using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NexusOptimizer.Models;

public sealed class GameInfo : INotifyPropertyChanged
{
    private bool? _isAvailable;
    private bool _isFavorite;
    private DateTime? _lastPlayedUtc;

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

    // Пользовательские данные хранятся отдельно (user-data.json), поэтому в библиотеку не сериализуются.

    [JsonIgnore]
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
                return;

            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    [JsonIgnore]
    public string FavoriteGlyph => _isFavorite ? "★" : "☆";

    /// <summary>Когда игру последний раз запускали из этой программы.</summary>
    [JsonIgnore]
    public DateTime? LastPlayedUtc
    {
        get => _lastPlayedUtc;
        set
        {
            if (_lastPlayedUtc == value)
                return;

            _lastPlayedUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastPlayedText));
        }
    }

    [JsonIgnore]
    public string LastPlayedText
    {
        get
        {
            if (_lastPlayedUtc is not { } played)
                return "Не запускалась из программы";

            TimeSpan ago = DateTime.UtcNow - played;

            if (ago < TimeSpan.FromMinutes(1))
                return "Запускалась только что";

            if (ago < TimeSpan.FromHours(1))
                return $"Запускалась {(int)ago.TotalMinutes} мин назад";

            if (ago < TimeSpan.FromDays(1))
                return $"Запускалась {(int)ago.TotalHours} ч назад";

            return $"Запускалась {played.ToLocalTime():dd.MM.yyyy}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
