using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NexusOptimizer.Models;
using NexusOptimizer.Services;

namespace NexusOptimizer.Views;

public partial class GameDetailsView : UserControl
{
    private readonly GameDetailsService _detailsService = new();
    private readonly GameSettingsStore _settingsStore = new();

    private GameInfo? _game;
    private string? _storeUrl;
    private CancellationTokenSource? _loadCts;

    /// <summary>Пользователь вернулся к списку игр.</summary>
    public event Action? BackRequested;

    public GameDetailsView()
    {
        InitializeComponent();

        // Лента миниатюр иначе «съедает» прокрутку колесом, и страница перестаёт листаться.
        ScreenshotList.PreviewMouseWheel += (_, e) =>
        {
            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta);
            e.Handled = true;
        };
    }

    public void ShowGame(GameInfo game)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        _game = game;
        _storeUrl = null;

        PageScroll.ScrollToTop();

        GameNameText.Text = game.Name;
        GamePlatformText.Text = game.Platform;
        GamePathText.Text = game.InstallPath;
        GameIconImage.Source = GameIconHelper.GetIcon(game);
        HeaderImage.Source = null;

        GenresText.Visibility = Visibility.Collapsed;
        MetaText.Visibility = Visibility.Collapsed;
        StoreButton.Visibility = Visibility.Collapsed;

        GalleryCard.Visibility = Visibility.Collapsed;
        ScreenshotList.ItemsSource = null;

        ShortDescriptionText.Visibility = Visibility.Collapsed;
        DescriptionText.Text = "Загружаем описание из Steam…";

        OptimizationStatusText.Text = "Профиль ещё не применялся.";
        WindowStatusText.Text = "Получение информации об игре…";

        LoadSettings(game);

        _ = LoadDetailsAsync(game, _loadCts.Token);
    }

    // ───────────────────────── Данные со Steam ─────────────────────────

    private async Task LoadDetailsAsync(GameInfo game, CancellationToken token)
    {
        try
        {
            GameDetails details = await _detailsService.GetDetailsAsync(game, token);

            if (token.IsCancellationRequested || !ReferenceEquals(_game, game))
                return;

            ApplyDetails(game, details);
        }
        catch (OperationCanceledException)
        {
            // Пользователь ушёл со страницы — ничего показывать не нужно.
        }
        catch (Exception ex)
        {
            AppLog.Error("Страница игры: не удалось показать данные", ex);

            if (!token.IsCancellationRequested)
            {
                DescriptionText.Text = "Не удалось показать описание игры.";
                WindowStatusText.Text = "Ошибка. Подробности записаны в журнал.";
            }
        }
    }

    private void ApplyDetails(GameInfo game, GameDetails details)
    {
        bool hasDescription = !string.IsNullOrWhiteSpace(details.Description);

        if (hasDescription
            && !string.IsNullOrWhiteSpace(details.ShortDescription)
            && details.ShortDescription != details.Description)
        {
            ShortDescriptionText.Text = details.ShortDescription;
            ShortDescriptionText.Visibility = Visibility.Visible;
        }

        DescriptionText.Text = hasDescription
            ? details.Description
            : details.Notice ?? "Описание для этой игры отсутствует.";

        if (Uri.TryCreate(details.HeaderImageUrl, UriKind.Absolute, out Uri? headerUri))
            HeaderImage.Source = new BitmapImage(headerUri);

        if (details.Genres.Count > 0)
        {
            GenresText.Text = string.Join(", ", details.Genres);
            GenresText.Visibility = Visibility.Visible;
        }

        var meta = new List<string>();

        if (details.Developers.Count > 0)
            meta.Add("Разработчик: " + string.Join(", ", details.Developers));

        if (details.Publishers.Count > 0)
            meta.Add("Издатель: " + string.Join(", ", details.Publishers));

        if (!string.IsNullOrWhiteSpace(details.ReleaseDate))
            meta.Add("Дата выхода: " + details.ReleaseDate);

        if (meta.Count > 0)
        {
            MetaText.Text = string.Join(Environment.NewLine, meta);
            MetaText.Visibility = Visibility.Visible;
        }

        if (details.Screenshots.Count > 0)
        {
            ScreenshotList.ItemsSource = details.Screenshots;
            ScreenshotList.SelectedIndex = 0;
            GalleryCard.Visibility = Visibility.Visible;
        }

        _storeUrl = details.StoreUrl;
        StoreButton.Visibility = string.IsNullOrWhiteSpace(_storeUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (details.Notice is not null)
            WindowStatusText.Text = details.Notice;
        else if (details.StoreAppId is null)
            WindowStatusText.Text = "Локальная игра";
        else if (game.Platform == Platforms.Steam)
            WindowStatusText.Text = $"Данные из Steam. App ID: {details.StoreAppId}";
        else
            WindowStatusText.Text = "Описание найдено в магазине Steam по названию игры";
    }

    // ───────────────────────── Кнопки ─────────────────────────

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null)
            return;

        if (!Directory.Exists(_game.InstallPath))
        {
            MessageBox.Show(
                "Папка игры не найдена:\n" + _game.InstallPath,
                "Папка недоступна",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_game.InstallPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось открыть папку: {_game.InstallPath}", ex);
            MessageBox.Show(ex.Message, "Не удалось открыть папку", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenStore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_storeUrl))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _storeUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось открыть страницу Steam", ex);
            MessageBox.Show(ex.Message, "Не удалось открыть страницу", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null)
            return;

        try
        {
            string arguments = LaunchArgumentsTextBox.Text.Trim();
            bool useAboveNormalPriority = AboveNormalPriorityCheckBox.IsChecked == true;

            SaveSettings();

            // Steam-игры запускаем через клиент: AppId известен из манифеста, гонки с загрузкой описания нет.
            if (_game.Platform == Platforms.Steam && !string.IsNullOrWhiteSpace(_game.AppId))
            {
                string steamUri = $"steam://run/{_game.AppId}";

                if (arguments.Length > 0)
                    steamUri += "//" + Uri.EscapeDataString(arguments);

                Process.Start(new ProcessStartInfo { FileName = steamUri, UseShellExecute = true });

                WindowStatusText.Text = "Команда запуска передана Steam";
                OptimizationStatusText.Text = useAboveNormalPriority
                    ? "Приоритет Steam-игры автоматически не изменён."
                    : "Игра запущена без изменения приоритета.";
                return;
            }

            string? executable = ExecutableLocator.Resolve(_game);

            if (executable is null)
            {
                MessageBox.Show(
                    "Не удалось найти exe-файл игры. Откройте папку игры и запустите её вручную.",
                    "Запуск невозможен",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = true
            });

            WindowStatusText.Text = "Игра запущена";

            if (process is null)
            {
                OptimizationStatusText.Text = "Не удалось получить процесс игры.";
                return;
            }

            if (!useAboveNormalPriority)
            {
                OptimizationStatusText.Text = "Игра запущена без изменения приоритета.";
                return;
            }

            try
            {
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
                OptimizationStatusText.Text = "Для процесса игры установлен приоритет «Выше обычного».";
            }
            catch (Exception ex)
            {
                AppLog.Warn("Не удалось изменить приоритет процесса", ex);
                OptimizationStatusText.Text = "Игра запущена, но приоритет изменить не удалось.";
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Не удалось запустить игру: {_game.Name}", ex);
            MessageBox.Show(ex.Message, "Не удалось запустить игру", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ───────────────────────── Настройки игры ─────────────────────────

    private void LoadSettings(GameInfo game)
    {
        LaunchArgumentsTextBox.Text = string.Empty;
        AboveNormalPriorityCheckBox.IsChecked = false;
        SettingsStatusText.Text = "Настройки ещё не сохранены";

        try
        {
            GameSettings? settings = _settingsStore.Load(game);

            if (settings is null)
                return;

            LaunchArgumentsTextBox.Text = settings.LaunchArguments;
            AboveNormalPriorityCheckBox.IsChecked = settings.AboveNormalPriority;
            SettingsStatusText.Text = "Загружены сохранённые настройки";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось загрузить настройки: {game.Name}", ex);
            SettingsStatusText.Text = "Не удалось загрузить сохранённые настройки";
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => SaveSettings();

    private void SaveSettings()
    {
        if (_game is null)
            return;

        try
        {
            _settingsStore.Save(_game, new GameSettings
            {
                LaunchArguments = LaunchArgumentsTextBox.Text.Trim(),
                AboveNormalPriority = AboveNormalPriorityCheckBox.IsChecked == true
            });

            SettingsStatusText.Text = "Настройки сохранены";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось сохранить настройки: {_game.Name}", ex);
            SettingsStatusText.Text = "Не удалось сохранить настройки: " + ex.Message;
        }
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null)
            return;

        try
        {
            _settingsStore.Delete(_game);

            LaunchArgumentsTextBox.Text = string.Empty;
            AboveNormalPriorityCheckBox.IsChecked = false;
            SettingsStatusText.Text = "Настройки сброшены";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось сбросить настройки: {_game.Name}", ex);
            SettingsStatusText.Text = "Не удалось сбросить настройки: " + ex.Message;
        }
    }
}
