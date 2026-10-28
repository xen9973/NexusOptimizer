using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private readonly GameTweaksService _tweaks = new();

    private GameInfo? _game;
    private GameSettings _settings = new();
    private IReadOnlyList<string> _executables = [];
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

        OptimizationStatusText.Text = string.Empty;
        WindowStatusText.Text = "Получение информации об игре…";

        LoadProfile(game);

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

    // ───────────────────────── Кнопки страницы ─────────────────────────

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

    // ───────────────────────── Запуск ─────────────────────────

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null)
            return;

        GameInfo game = _game;

        try
        {
            // Перед запуском применяем и сохраняем профиль: игре нужны актуальные настройки Windows.
            ApplyProfileAndSave();

            string arguments = _settings.LaunchArguments;

            // Сообщения слежения приходят из фона; показываем их, только если страница всё ещё этой игры.
            var report = new Progress<string>(message =>
            {
                if (ReferenceEquals(_game, game))
                    OptimizationStatusText.Text = message;
            });

            // Steam-игры запускаем через клиент: AppId известен из манифеста, гонки с загрузкой описания нет.
            if (game.Platform == Platforms.Steam && !string.IsNullOrWhiteSpace(game.AppId))
            {
                string steamUri = $"steam://run/{game.AppId}";

                if (arguments.Length > 0)
                    steamUri += "//" + Uri.EscapeDataString(arguments);

                Process.Start(new ProcessStartInfo { FileName = steamUri, UseShellExecute = true });
                WindowStatusText.Text = "Команда запуска передана Steam";
            }
            else
            {
                string? executable = ExecutableLocator.Resolve(game);

                if (executable is null)
                {
                    MessageBox.Show(
                        "Не удалось найти exe-файл игры. Откройте папку игры и запустите её вручную.",
                        "Запуск невозможен",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = true
                });

                WindowStatusText.Text = "Игра запущена";
            }

            if (_settings.AboveNormalPriority || _settings.HighPerformancePowerPlan)
                GameSessionService.Current.Start(game, _settings, report);
            else
                OptimizationStatusText.Text = "Игра запущена без изменения приоритета и электропитания.";
        }
        catch (Exception ex)
        {
            AppLog.Error($"Не удалось запустить игру: {game.Name}", ex);
            MessageBox.Show(ex.Message, "Не удалось запустить игру", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ───────────────────────── Профиль оптимизации ─────────────────────────

    private void LoadProfile(GameInfo game)
    {
        _executables = ExecutableLocator.FindAll(game);
        _settings = new GameSettings();
        SettingsStatusText.Text = "Профиль ещё не применялся";

        try
        {
            GameSettings? saved = _settingsStore.Load(game);

            if (saved is not null)
            {
                _settings = saved;
                SettingsStatusText.Text = "Загружен сохранённый профиль";
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось загрузить профиль: {game.Name}", ex);
            SettingsStatusText.Text = "Не удалось загрузить сохранённый профиль";
        }

        LaunchArgumentsTextBox.Text = _settings.LaunchArguments;
        AboveNormalPriorityCheckBox.IsChecked = _settings.AboveNormalPriority;
        PowerPlanCheckBox.IsChecked = _settings.HighPerformancePowerPlan;

        bool hasExecutables = _executables.Count > 0;
        GpuCheckBox.IsEnabled = hasExecutables;
        FullscreenCheckBox.IsEnabled = hasExecutables;

        TweakTargetsText.Text = hasExecutables
            ? "Профиль применяется к файлам: " + string.Join(", ", _executables.Select(path => Path.GetFileName(path)))
            : "Exe-файл игры не найден, поэтому настройки видеокарты и полноэкранного режима недоступны.";

        RefreshTweakCheckboxes();
    }

    /// <summary>Видеокарта и полноэкранный режим хранятся в реестре, поэтому показываем их реальное состояние.</summary>
    private void RefreshTweakCheckboxes()
    {
        GpuCheckBox.IsChecked = false;
        FullscreenCheckBox.IsChecked = false;

        if (_executables.Count == 0)
            return;

        try
        {
            TweakState state = _tweaks.ReadState(_executables);

            GpuCheckBox.IsChecked = state.AllGpuHighPerformance;
            FullscreenCheckBox.IsChecked = state.AllFullscreenDisabled;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось прочитать настройки Windows", ex);
        }
    }

    private void CollectSettings()
    {
        _settings.LaunchArguments = LaunchArgumentsTextBox.Text.Trim();
        _settings.AboveNormalPriority = AboveNormalPriorityCheckBox.IsChecked == true;
        _settings.HighPerformancePowerPlan = PowerPlanCheckBox.IsChecked == true;
        _settings.HighPerformanceGpu = GpuCheckBox.IsChecked == true;
        _settings.DisableFullscreenOptimizations = FullscreenCheckBox.IsChecked == true;
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e) => ApplyProfileAndSave();

    private void ApplyProfileAndSave()
    {
        if (_game is null)
            return;

        CollectSettings();

        var errors = new List<string>();

        if (_executables.Count > 0 || _settings.TweakBackups.Count > 0)
            errors.AddRange(_tweaks.ApplyProfile(_executables, _settings));

        try
        {
            _settingsStore.Save(_game, _settings);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось сохранить профиль: {_game.Name}", ex);
            errors.Add("не удалось сохранить профиль: " + ex.Message);
        }

        RefreshTweakCheckboxes();

        SettingsStatusText.Text = errors.Count == 0
            ? "Профиль применён и сохранён."
            : "Профиль применён не полностью: " + string.Join("; ", errors);
    }

    private void RevertProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null)
            return;

        List<string> errors = _tweaks.RevertAll(_settings);

        try
        {
            if (_settings.TweakBackups.Count == 0)
            {
                _settingsStore.Delete(_game);
                _settings = new GameSettings();
            }
            else
            {
                // Часть значений вернуть не удалось: оставляем резервные копии, чтобы повторить попытку.
                _settings.LaunchArguments = string.Empty;
                _settings.AboveNormalPriority = false;
                _settings.HighPerformancePowerPlan = false;
                _settingsStore.Save(_game, _settings);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось сбросить профиль: {_game.Name}", ex);
            errors.Add(ex.Message);
        }

        LaunchArgumentsTextBox.Text = string.Empty;
        AboveNormalPriorityCheckBox.IsChecked = false;
        PowerPlanCheckBox.IsChecked = false;
        RefreshTweakCheckboxes();

        SettingsStatusText.Text = errors.Count == 0
            ? "Профиль сброшен: настройки Windows возвращены к исходным."
            : "Профиль сброшен не полностью: " + string.Join("; ", errors);
    }
}
