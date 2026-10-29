using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using NexusOptimizer.Models;
using NexusOptimizer.Services;

namespace NexusOptimizer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public const string SortByName = "По названию";
    public const string SortByRecent = "Недавние";

    private readonly GameScanner _scanner = new();
    private readonly LibraryStore _store = new();

    private CancellationTokenSource? _scanCts;
    private string _status = "Готово к сканированию";
    private string _searchText = string.Empty;
    private string _selectedPlatform = Platforms.All;
    private string _sortMode = SortByName;
    private bool _showOnlyFavorites;
    private bool _isScanning;
    private bool _suspendCounts;
    private DateTime? _lastScanUtc;

    public ObservableCollection<GameInfo> Games { get; } = [];

    public ObservableCollection<string> ExtraFolders { get; } = [];

    public ObservableCollection<string> PlatformFilters { get; } =
        [Platforms.All, Platforms.Steam, Platforms.Epic, Platforms.Folder];

    public ObservableCollection<string> SortModes { get; } = [SortByName, SortByRecent];

    public ICollectionView FilteredGames { get; }

    public MainViewModel()
    {
        FilteredGames = CollectionViewSource.GetDefaultView(Games);
        FilteredGames.Filter = FilterGame;
        ApplySort();

        Games.CollectionChanged += (_, _) => RaiseCountsChanged();
        ExtraFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasExtraFolders));

        LoadCachedLibrary();
    }

    // ───────────────────────── Свойства для привязок ─────────────────────────

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                FilteredGames.Refresh();
                RaiseCountsChanged();
            }
        }
    }

    public string SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            if (SetField(ref _selectedPlatform, value))
            {
                FilteredGames.Refresh();
                RaiseCountsChanged();
            }
        }
    }

    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (SetField(ref _sortMode, value))
                ApplySort();
        }
    }

    public bool ShowOnlyFavorites
    {
        get => _showOnlyFavorites;
        set
        {
            if (SetField(ref _showOnlyFavorites, value))
            {
                FilteredGames.Refresh();
                RaiseCountsChanged();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetField(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanButtonText));
                RaiseCountsChanged();
            }
        }
    }

    public string ScanButtonText => IsScanning ? "Отменить сканирование" : "Сканировать игры";

    public bool HasExtraFolders => ExtraFolders.Count > 0;

    public int VisibleGamesCount => FilteredGames.Cast<object>().Count();

    public string LibrarySummary
    {
        get
        {
            if (Games.Count == 0)
                return "Игры ещё не найдены";

            return VisibleGamesCount == Games.Count
                ? $"Игр в библиотеке: {Games.Count}"
                : $"Показано {VisibleGamesCount} из {Games.Count}";
        }
    }

    public bool IsEmpty => VisibleGamesCount == 0;

    public string EmptyTitle
    {
        get
        {
            if (Games.Count > 0) return "Ничего не найдено";
            return IsScanning ? "Ищем игры…" : "Игры пока не найдены";
        }
    }

    public string EmptyHint
    {
        get
        {
            if (Games.Count > 0)
                return "Измените запрос или выберите другой источник.";

            return IsScanning
                ? "Это займёт несколько секунд."
                : "Нажмите «Сканировать игры»: программа проверит Steam и Epic Games. Игры из других мест добавьте через «Добавить папку».";
        }
    }

    // ───────────────────────── Запуск и сканирование ─────────────────────────

    /// <summary>Показывает сохранённую библиотеку сразу и тихо обновляет её в фоне.</summary>
    public Task InitializeAsync() => ScanAsync(isAutomatic: true);

    public async Task ToggleScanAsync()
    {
        if (IsScanning)
        {
            _scanCts?.Cancel();
            Status = "Отмена сканирования…";
            return;
        }

        await ScanAsync();
    }

    public async Task ScanAsync(bool isAutomatic = false)
    {
        if (IsScanning)
            return;

        _scanCts = new CancellationTokenSource();
        CancellationToken token = _scanCts.Token;

        IsScanning = true;
        Status = isAutomatic && Games.Count > 0
            ? "Обновляем библиотеку…"
            : "Подготовка к сканированию…";

        try
        {
            var progress = new Progress<string>(message => Status = message);

            IReadOnlyList<GameInfo> results = await _scanner.ScanAsync(
                ExtraFolders.ToArray(),
                progress,
                token);

            ApplyResults(results);

            _lastScanUtc = DateTime.UtcNow;
            SaveLibrary();

            Status = $"Сканирование завершено. Найдено игр: {Games.Count}";
        }
        catch (OperationCanceledException)
        {
            Status = "Сканирование отменено";
        }
        catch (Exception ex)
        {
            AppLog.Error("Ошибка сканирования", ex);
            Status = "Ошибка сканирования. Подробности записаны в журнал.";

            if (!isAutomatic)
            {
                MessageBox.Show(
                    ex.Message,
                    "Не удалось выполнить сканирование",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    public void Shutdown() => _scanCts?.Cancel();

    // ───────────────────────── Папки для поиска ─────────────────────────

    /// <returns>true, если папка добавлена и нужно обновить список игр.</returns>
    public bool AddFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку, в которой лежат игры"
        };

        if (dialog.ShowDialog() != true)
            return false;

        string folder = dialog.FolderName;

        if (IsUnsuitableFolder(folder, out string reason))
        {
            Status = reason;

            MessageBox.Show(
                reason,
                "Эту папку добавить нельзя",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return false;
        }

        if (ExtraFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            Status = "Эта папка уже добавлена";
            return false;
        }

        ExtraFolders.Add(folder);
        SaveLibrary();
        Status = $"Добавлена папка: {folder}";
        return true;
    }

    public async Task RemoveFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !ExtraFolders.Remove(folder))
            return;

        SaveLibrary();

        if (IsScanning)
        {
            Status = $"Папка убрана: {folder}. Изменения применятся при следующем сканировании.";
            return;
        }

        Status = $"Папка убрана: {folder}";
        await ScanAsync();
    }

    private static bool IsUnsuitableFolder(string folder, out string reason)
    {
        char[] separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

        string full = Path.GetFullPath(folder).TrimEnd(separators);
        string root = (Path.GetPathRoot(Path.GetFullPath(folder)) ?? string.Empty).TrimEnd(separators);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(separators);

        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Корень диска добавить нельзя: в список попадут все программы. Выберите папку с играми, например D:\\Games.";
            return true;
        }

        if (windows.Length > 0
            && (string.Equals(full, windows, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "Системную папку Windows добавить нельзя.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    // ───────────────────────── Действия над играми ─────────────────────────

    public void OpenGameFolder(GameInfo game)
    {
        if (!Directory.Exists(game.InstallPath))
        {
            Status = $"Папка недоступна: {game.InstallPath}";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{game.InstallPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось открыть папку: {game.InstallPath}", ex);
            Status = $"Не удалось открыть папку: {ex.Message}";
        }
    }

    public void CopyGamePath(GameInfo game)
    {
        try
        {
            Clipboard.SetText(game.InstallPath);
            Status = "Путь скопирован в буфер обмена";
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось скопировать путь", ex);
            Status = $"Не удалось скопировать путь: {ex.Message}";
        }
    }

    // ───────────────────────── Внутреннее ─────────────────────────

    private void LoadCachedLibrary()
    {
        LibraryData data = _store.Load();

        foreach (string folder in data.ExtraFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder)
                && !ExtraFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                ExtraFolders.Add(folder);
            }
        }

        _suspendCounts = true;

        foreach (GameInfo game in data.Games)
            Games.Add(game);

        _suspendCounts = false;

        GameUserDataStore.Current.Apply(Games);

        _lastScanUtc = data.LastScanUtc;

        if (Games.Count > 0)
            _status = $"Загружена сохранённая библиотека: {Games.Count}";
    }

    /// <summary>Заменяет список игр, только если результат отличается от показанного.</summary>
    private void ApplyResults(IReadOnlyList<GameInfo> results)
    {
        static string Key(GameInfo g) =>
            $"{g.Platform}|{g.InstallPath}|{g.Name}|{g.AppId}|{g.ExecutablePath}";

        var current = new HashSet<string>(Games.Select(Key), StringComparer.OrdinalIgnoreCase);

        if (current.Count == results.Count && results.All(game => current.Contains(Key(game))))
            return;

        _suspendCounts = true;

        Games.Clear();

        foreach (GameInfo game in results)
            Games.Add(game);

        _suspendCounts = false;

        GameUserDataStore.Current.Apply(Games);

        FilteredGames.Refresh();
        RaiseCountsChanged();
    }

    private void SaveLibrary()
    {
        _store.Save(new LibraryData
        {
            ExtraFolders = ExtraFolders.ToList(),
            Games = Games.ToList(),
            LastScanUtc = _lastScanUtc
        });
    }

    private bool FilterGame(object item)
    {
        if (item is not GameInfo game)
            return false;

        bool platformMatches = SelectedPlatform == Platforms.All
                               || string.Equals(game.Platform, SelectedPlatform, StringComparison.OrdinalIgnoreCase);

        if (!platformMatches)
            return false;

        if (ShowOnlyFavorites && !game.IsFavorite)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        string query = SearchText.Trim();

        return game.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || game.InstallPath.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    public void ToggleFavorite(GameInfo game)
    {
        GameUserDataStore.Current.SetFavorite(game, !game.IsFavorite);

        // Обновляем вид: изменился фильтр «Только избранное».
        FilteredGames.Refresh();
        RaiseCountsChanged();
    }

    private void ApplySort()
    {
        FilteredGames.SortDescriptions.Clear();

        if (SortMode == SortByRecent)
        {
            FilteredGames.SortDescriptions.Add(
                new SortDescription(nameof(GameInfo.LastPlayedUtc), ListSortDirection.Descending));
        }

        FilteredGames.SortDescriptions.Add(
            new SortDescription(nameof(GameInfo.Name), ListSortDirection.Ascending));
    }

    private void RaiseCountsChanged()
    {
        if (_suspendCounts)
            return;

        OnPropertyChanged(nameof(VisibleGamesCount));
        OnPropertyChanged(nameof(LibrarySummary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
