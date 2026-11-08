using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using NexusOptimizer.Services;

namespace NexusOptimizer.Views;

public partial class WelcomeWindow : Window
{
    private readonly PcSystemScanService _scanService = new();
    private readonly PcTweaksService _tweaksService = new();
    private readonly DriverUpdateService _driverUpdateService = new();
    private readonly ObservableCollection<PcSystemRecommendationItem> _recommendations = [];
    private PcTweaksState? _tweaksState;
    private bool _isClosed;

    public WelcomeWindow()
    {
        InitializeComponent();
        RecommendationsList.ItemsSource = _recommendations;
        Closed += (_, _) => _isClosed = true;
        Loaded += async (_, _) =>
        {
            UiEntranceAnimator.AnimateOnce(this);
            await ScanSystemAsync();
        };
    }

    private async Task ScanSystemAsync()
    {
        long startedAt = Stopwatch.GetTimestamp();
        ScanProgress.Visibility = Visibility.Visible;
        ScanRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });

        try
        {
            IProgress<string> progress = new Progress<string>(message => ScanStatusText.Text = message);
            PcSystemScanReport report = await Task.Run(() => _scanService.Scan(progress.Report));
            if (_isClosed)
                return;

            _tweaksState = _tweaksService.LoadState();
            TimeSpan remaining = TimeSpan.FromSeconds(2) - Stopwatch.GetElapsedTime(startedAt);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);

            ShowReport(report);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось выполнить начальное сканирование компьютера", ex);
            if (_isClosed)
                return;

            ScanStatusText.Text = "Не удалось завершить сканирование.";
            RecommendationsStatusText.Text = "Не удалось проверить настройки.";
            EmptyStateText.Text = "Ошибка сканирования: " + ex.Message;
            FooterStatusText.Text = "Попробуйте позже из раздела «Умная настройка».";
        }
        finally
        {
            if (!_isClosed)
            {
                ScanRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                ScanProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowReport(PcSystemScanReport report)
    {
        WindowsText.Text = report.WindowsVersion;
        ProcessorText.Text = $"{report.ProcessorName} · {report.LogicalProcessorCount} логических процессоров";
        MemoryText.Text = $"{report.TotalPhysicalMemoryBytes / (1024d * 1024 * 1024):0.#} ГБ";
        GraphicsText.Text = report.GraphicsAdapters.Count == 0
            ? "Не удалось определить"
            : string.Join(Environment.NewLine, report.GraphicsAdapters);

        _recommendations.Clear();
        foreach (PcSystemRecommendation recommendation in report.Recommendations)
            _recommendations.Add(new PcSystemRecommendationItem { Recommendation = recommendation });

        bool hasRecommendations = _recommendations.Count > 0;
        RecommendationsStatusText.Text = hasRecommendations
            ? $"Найдено рекомендаций: {_recommendations.Count}. Отметьте только те, которые хотите применить."
            : "Подходящих изменений не найдено.";
        RecommendationsScroll.Visibility = hasRecommendations ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Visibility = hasRecommendations ? Visibility.Collapsed : Visibility.Visible;
        if (!hasRecommendations)
            EmptyStateText.Text = "Сейчас дополнительные изменения не требуются.";

        if (report.Warnings.Count > 0)
        {
            WarningText.Text = "Не все данные удалось проверить: " + string.Join(" · ", report.Warnings);
            WarningText.Visibility = Visibility.Visible;
        }

        ScanStatusText.Text = "Сканирование завершено.";
    }

    private async void DriverSearch_Click(object sender, RoutedEventArgs e)
    {
        DriverSearchButton.IsEnabled = false;
        DriverStatusText.Text = "Подключаемся к Windows Update и ищем драйверы...";
        try
        {
            IReadOnlyList<DriverUpdate> updates = await Task.Run(_driverUpdateService.SearchAvailableUpdates);
            if (_isClosed)
                return;

            DriverStatusText.Text = updates.Count == 0
                ? "Windows Update не нашёл доступных обновлений драйверов."
                : "Найдены обновления:" + Environment.NewLine
                    + string.Join(Environment.NewLine, updates.Select(update => "• " + update.Title));
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось проверить обновления драйверов через Windows Update", ex);
            if (!_isClosed)
                DriverStatusText.Text = "Не удалось выполнить поиск драйверов: " + ex.Message;
        }
        finally
        {
            if (!_isClosed)
                DriverSearchButton.IsEnabled = true;
        }
    }

    private void Recommendation_Checked(object sender, RoutedEventArgs e)
    {
        ApplyButton.IsEnabled = _tweaksState is not null && _recommendations.Any(item => item.IsSelected);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        string[] selectedIds = _recommendations
            .Where(item => item.IsSelected)
            .Select(item => item.Recommendation.Tweak.Id)
            .ToArray();

        if (selectedIds.Length == 0 || _tweaksState is null)
            return;

        string selectedNames = string.Join(Environment.NewLine, _recommendations
            .Where(item => item.IsSelected)
            .Select(item => "• " + item.Name));
        if (MessageBox.Show(
                "Применить выбранные настройки для текущего пользователя?" + Environment.NewLine
                + Environment.NewLine + selectedNames
                + Environment.NewLine + Environment.NewLine
                + "Изменения можно отменить на странице настройки компьютера.",
                "Подтверждение оптимизации",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        try
        {
            IReadOnlySet<string> selected = selectedIds.ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<PcTweakResult> results = _tweaksService.ApplySelectedUserTweaks(_tweaksState, selected);
            string[] errors = results
                .Where(result => !result.Success)
                .Select(result => $"{PcTweaksCatalog.Tweaks.First(tweak => tweak.Id == result.Id).Name}: {result.Message}")
                .ToArray();

            if (errors.Length > 0)
            {
                FooterStatusText.Text = "Не удалось применить часть настроек: " + string.Join(" · ", errors);
                ApplyButton.IsEnabled = true;
                return;
            }

            FooterStatusText.Text = "Выбранные настройки применены. Их можно восстановить на странице настройки компьютера.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось применить выбранные начальные настройки", ex);
            FooterStatusText.Text = "Ошибка применения настроек: " + ex.Message;
            ApplyButton.IsEnabled = true;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
