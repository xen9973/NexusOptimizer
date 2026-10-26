
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using NexusOptimizer.Models;
using NexusOptimizer.Services;

namespace NexusOptimizer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly GameScanner _scanner = new();

    private string _status = "Готово к сканированию";
    private string _searchText = string.Empty;
    private string _selectedPlatform = "Все источники";
    private bool _isScanning;

    public ObservableCollection<GameInfo> Games { get; } = [];
    public ObservableCollection<string> ExtraFolders { get; } = [];

    public ObservableCollection<string> PlatformFilters { get; } =
    [
        "Все источники",
        "Steam",
        "Epic Games",
        "Дополнительная папка"
    ];

    public ICollectionView FilteredGames { get; }

    public MainViewModel()
    {
        FilteredGames = CollectionViewSource.GetDefaultView(Games);
        FilteredGames.Filter = FilterGame;

        Games.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalGamesCount));
            OnPropertyChanged(nameof(VisibleGamesCount));
        };
    }

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;

            _searchText = value;
            OnPropertyChanged();

            FilteredGames.Refresh();
            OnPropertyChanged(nameof(VisibleGamesCount));
        }
    }

    public string SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            if (_selectedPlatform == value) return;

            _selectedPlatform = value;
            OnPropertyChanged();

            FilteredGames.Refresh();
            OnPropertyChanged(nameof(VisibleGamesCount));
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value) return;

            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScanButtonText));
        }
    }

    public string ScanButtonText =>
        IsScanning ? "Сканирование…" : "Сканировать игры";

    public int TotalGamesCount => Games.Count;

    public int VisibleGamesCount =>
        FilteredGames.Cast<object>().Count();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для поиска игр"
        };

        if (dialog.ShowDialog() != true)
            return;

        string folder = dialog.FolderName;

        if (!ExtraFolders.Contains(
                folder, StringComparer.OrdinalIgnoreCase))
        {
            ExtraFolders.Add(folder);
        }

        Status = $"Добавлена папка: {folder}";
    }

    public async Task ScanAsync()
    {
        if (IsScanning)
            return;

        IsScanning = true;
        Status = "Подготовка к сканированию…";

        try
        {
            var progress = new Progress<string>(
                message => Status = message);

            var results = await _scanner.ScanAsync(
                ExtraFolders.ToArray(), progress);

            Games.Clear();

            foreach (var game in results)
                Games.Add(game);

            FilteredGames.Refresh();

            OnPropertyChanged(nameof(TotalGamesCount));
            OnPropertyChanged(nameof(VisibleGamesCount));

            Status = $"Сканирование завершено. Найдено игр: {Games.Count}";
        }
        catch (Exception ex)
        {
            Status = "Ошибка сканирования";

            MessageBox.Show(
                ex.Message,
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void OpenGameFolder(GameInfo? game)
    {
        if (game is null || !Directory.Exists(game.InstallPath))
        {
            MessageBox.Show(
                "Папка игры не найдена или недоступна.",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

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
            MessageBox.Show(
                $"Не удалось открыть папку игры.\n{ex.Message}",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void CopyGamePath(GameInfo? game)
    {
        if (game is null)
            return;

        try
        {
            Clipboard.SetText(game.InstallPath);
            Status = $"Путь скопирован: {game.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось скопировать путь.\n{ex.Message}",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool FilterGame(object item)
    {
        if (item is not GameInfo game)
            return false;

        bool matchesPlatform =
            SelectedPlatform == "Все источники" ||
            string.Equals(
                game.Platform,
                SelectedPlatform,
                StringComparison.OrdinalIgnoreCase);

        string query = SearchText.Trim();

        bool matchesSearch =
            query.Length == 0 ||
            game.Name.Contains(
                query, StringComparison.OrdinalIgnoreCase) ||
            game.Platform.Contains(
                query, StringComparison.OrdinalIgnoreCase) ||
            game.InstallPath.Contains(
                query, StringComparison.OrdinalIgnoreCase);

        return matchesPlatform && matchesSearch;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(
            this, new PropertyChangedEventArgs(name));
    }
}
