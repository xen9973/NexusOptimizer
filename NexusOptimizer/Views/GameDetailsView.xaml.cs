using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using NexusOptimizer.ViewModels;

namespace NexusOptimizer.Views;

public partial class GameDetailsView : UserControl
{
    private readonly GameDetailsService _detailsService = new();
    private readonly GameSettingsStore _settingsStore = new();
    private readonly GameTweaksService _tweaks = new();
    private readonly ImageCacheService _imageCache = new();
    private readonly ObservableCollection<OptimizationActionItem> _actionItems = [];

    private GameInfo? _game;
    private GameSettings _settings = new();
    private IReadOnlyList<string> _executables = [];
    private string? _storeUrl;
    private CancellationTokenSource? _loadCts;
    private bool _updatingProfile;

    /// <summary>Пользователь вернулся к списку игр.</summary>
    public event Action? BackRequested;

    public GameDetailsView()
    {
        InitializeComponent();

        ActionsList.ItemsSource = _actionItems;
        ProfileList.ItemsSource = OptimizationCatalog.Profiles.Select(profile => profile.Name).ToList();

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

        ShowHeaderImage(details.HeaderImageUrl);

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

            GameUserDataStore.Current.RecordLaunch(game);

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

    // ───────────────────────── Обложка ─────────────────────────

    private void ShowHeaderImage(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return;

        string? cachedPath = _imageCache.TryGetCachedPath(url!);

        if (cachedPath is not null)
        {
            HeaderImage.Source = LoadBitmap(cachedPath) ?? new BitmapImage(uri);
            return;
        }

        // Показываем картинку из сети сразу и сохраняем копию: в следующий раз она откроется без интернета.
        HeaderImage.Source = new BitmapImage(uri);
        _ = _imageCache.DownloadAsync(url!);
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось открыть сохранённую картинку: {path}", ex);
            return null;
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

        bool hasExecutables = _executables.Count > 0;

        TweakTargetsText.Text = hasExecutables
            ? "Профиль применяется к файлам: " + string.Join(", ", _executables.Select(path => Path.GetFileName(path)))
            : "Exe-файл игры не найден, поэтому действия с видеокартой и полноэкранным режимом недоступны.";

        // Видеокарта и полноэкранный режим хранятся в реестре, поэтому берём их реальное состояние.
        TweakState? state = ReadTweakState();

        if (state is not null)
        {
            _settings.HighPerformanceGpu = state.AllGpuHighPerformance;
            _settings.DisableFullscreenOptimizations = state.AllFullscreenDisabled;
        }
        else if (!hasExecutables)
        {
            _settings.HighPerformanceGpu = false;
            _settings.DisableFullscreenOptimizations = false;
        }

        _updatingProfile = true;

        try
        {
            foreach (OptimizationActionItem old in _actionItems)
                old.PropertyChanged -= ActionItem_PropertyChanged;

            _actionItems.Clear();

            foreach (OptimizationActionDefinition definition in OptimizationCatalog.Actions)
            {
                var item = new OptimizationActionItem(definition)
                {
                    IsSelected = definition.Get(_settings)
                };

                item.PropertyChanged += ActionItem_PropertyChanged;
                _actionItems.Add(item);
            }
        }
        finally
        {
            _updatingProfile = false;
        }

        RefreshActionStates(state);
        SyncProfileSelection();
    }

    private TweakState? ReadTweakState()
    {
        if (_executables.Count == 0)
            return null;

        try
        {
            return _tweaks.ReadState(_executables);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось прочитать настройки Windows", ex);
            return null;
        }
    }

    /// <summary>Обновляет у каждого действия доступность и текст «Сейчас…».</summary>
    private void RefreshActionStates(TweakState? state)
    {
        bool hasExecutables = _executables.Count > 0;

        foreach (OptimizationActionItem item in _actionItems)
        {
            OptimizationActionDefinition definition = item.Definition;

            if (definition.NeedsExecutable && !hasExecutables)
            {
                item.IsAvailable = false;
                item.IsActive = false;
                item.StateText = "Недоступно: exe-файл игры не найден";
                continue;
            }

            item.IsAvailable = true;

            if (definition.RegistryCount is { } countApplied)
            {
                if (state is null)
                {
                    item.IsActive = false;
                    item.StateText = "Не удалось прочитать состояние Windows";
                    continue;
                }

                int on = countApplied(state);
                int total = state.ExecutableCount;

                item.IsActive = on > 0;
                item.StateText = on == 0
                    ? "Сейчас в Windows: выключено"
                    : on == total
                        ? "Сейчас в Windows: включено"
                        : $"Сейчас в Windows: частично ({on} из {total} файлов)";
            }
            else
            {
                bool saved = definition.Get(_settings);

                item.IsActive = saved;
                item.StateText = saved
                    ? "Сохранено в профиле: включено"
                    : "Сохранено в профиле: выключено";
            }
        }
    }

    /// <summary>После применения показывает у каждого действия, что получилось.</summary>
    private void UpdateResults(TweakState? state)
    {
        foreach (OptimizationActionItem item in _actionItems)
        {
            if (!item.IsAvailable)
            {
                item.ResultText = string.Empty;
                continue;
            }

            if (item.Definition.RegistryCount is { } countApplied)
            {
                if (state is null)
                {
                    item.ResultText = "Не удалось проверить результат.";
                    continue;
                }

                int on = countApplied(state);

                item.ResultText = item.IsSelected
                    ? on == state.ExecutableCount
                        ? $"Применено к файлам игры: {state.ExecutableCount}."
                        : "Применено не полностью, подробности в сообщении ниже."
                    : on == 0
                        ? "Выключено, исходное значение возвращено."
                        : "Не удалось полностью вернуть исходное значение.";
            }
            else
            {
                item.ResultText = item.IsSelected
                    ? "Сохранено: сработает при запуске игры кнопкой «Запустить игру»."
                    : "Выключено.";
            }
        }
    }

    // ───────────────────────── Выбор профиля ─────────────────────────

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfile || ProfileList.SelectedItem is not string name)
            return;

        OptimizationProfileDefinition profile = OptimizationCatalog.GetProfile(name);
        ProfileDescriptionText.Text = profile.Description;

        // «Пользовательский» набор не меняет отмеченные действия.
        if (profile.ActionIds is null)
            return;

        _updatingProfile = true;

        try
        {
            int skipped = 0;

            foreach (OptimizationActionItem item in _actionItems)
            {
                bool wanted = profile.ActionIds.Contains(item.Definition.Id);

                if (wanted && !item.IsAvailable)
                    skipped++;

                item.IsSelected = wanted && item.IsAvailable;
            }

            if (skipped > 0)
                ProfileDescriptionText.Text += " Недоступные действия пропущены.";
        }
        finally
        {
            _updatingProfile = false;
        }
    }

    private void ActionItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptimizationActionItem.IsSelected) && !_updatingProfile)
            SyncProfileSelection();
    }

    /// <summary>Подсвечивает профиль, совпадающий с отмеченными действиями, иначе «Пользовательский».</summary>
    private void SyncProfileSelection()
    {
        string name = OptimizationCatalog.MatchProfile(
            _actionItems.Where(item => item.IsSelected).Select(item => item.Definition.Id));

        _updatingProfile = true;

        try
        {
            ProfileList.SelectedItem = name;
        }
        finally
        {
            _updatingProfile = false;
        }

        ProfileDescriptionText.Text = OptimizationCatalog.GetProfile(name).Description;
    }

    // ───────────────────────── Применение и отмена ─────────────────────────

    private void CollectSettings()
    {
        _settings.LaunchArguments = LaunchArgumentsTextBox.Text.Trim();

        foreach (OptimizationActionItem item in _actionItems)
            item.Definition.Set(_settings, item.IsSelected && item.IsAvailable);
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e) => ApplyProfileAndSave();

    private void RevertAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OptimizationActionItem item })
            ApplyProfileAndSave(item);
    }

    /// <param name="revertOnly">Если задано, отменяется только это действие, остальное остаётся как сохранено.</param>
    private void ApplyProfileAndSave(OptimizationActionItem? revertOnly = null)
    {
        if (_game is null)
            return;

        if (revertOnly is null)
        {
            CollectSettings();
        }
        else
        {
            revertOnly.Definition.Set(_settings, false);
            revertOnly.IsSelected = false;
        }

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

        TweakState? state = ReadTweakState();
        RefreshActionStates(state);
        UpdateResults(state);

        if (errors.Count > 0)
            SettingsStatusText.Text = "Профиль применён не полностью: " + string.Join("; ", errors);
        else if (revertOnly is not null)
            SettingsStatusText.Text = $"Действие «{revertOnly.Title}» отменено.";
        else
            SettingsStatusText.Text = "Профиль применён и сохранён.";
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

        LoadProfile(_game);

        SettingsStatusText.Text = errors.Count == 0
            ? "Всё возвращено: настройки Windows снова исходные, профиль удалён."
            : "Возвращено не полностью: " + string.Join("; ", errors);
    }
}
