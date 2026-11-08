using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using NexusOptimizer.Services;

namespace NexusOptimizer.Views;

public sealed class PcSystemRecommendationItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required PcSystemRecommendation Recommendation { get; init; }
    public string Name => Recommendation.Tweak.Name;
    public string Caution => Recommendation.Tweak.Caution;
    public string Reason => Recommendation.Reason;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class PcSystemScanView : UserControl
{
    private readonly PcSystemScanService _scanService = new();
    private readonly ObservableCollection<PcSystemRecommendationItem> _recommendations = [];

    public event Action? BackRequested;
    public event Action<IReadOnlySet<string>>? ApplyRecommendationsRequested;

    public PcSystemScanView()
    {
        InitializeComponent();
        RecommendationsList.ItemsSource = _recommendations;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        long scanStartedAt = Stopwatch.GetTimestamp();
        ScanButton.IsEnabled = false;
        ApplyRecommendationsButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        ScanStateText.Text = "Подготовка сканирования...";
        RecommendationsScroll.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = Visibility.Visible;
        EmptyStateText.Text = "Собираем сведения о компьютере...";
        WarningText.Visibility = Visibility.Collapsed;
        ScanRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });

        try
        {
            IProgress<string> progress = new Progress<string>(message => ScanStateText.Text = message);
            PcSystemScanReport report = await Task.Run(() => _scanService.Scan(progress.Report));
            TimeSpan remainingAnimationTime = TimeSpan.FromSeconds(2) - Stopwatch.GetElapsedTime(scanStartedAt);
            if (remainingAnimationTime > TimeSpan.Zero)
            {
                ScanStateText.Text = "Формируем рекомендации...";
                await Task.Delay(remainingAnimationTime);
            }
            ShowReport(report);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось просканировать конфигурацию компьютера", ex);
            ScanStateText.Text = "Не удалось завершить сканирование.";
            EmptyStateText.Text = "Ошибка сканирования: " + ex.Message;
        }
        finally
        {
            ScanRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
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
            ? $"Найдено подходящих настроек: {_recommendations.Count}. Ничего не выбрано автоматически."
            : "Подходящих изменений не найдено: доступные безопасные настройки уже включены или дополнительных рекомендаций нет.";
        RecommendationsScroll.Visibility = hasRecommendations ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Visibility = hasRecommendations ? Visibility.Collapsed : Visibility.Visible;
        if (!hasRecommendations)
            EmptyStateText.Text = "Система уже соответствует доступным рекомендациям.";

        if (report.Warnings.Count > 0)
        {
            WarningText.Text = "Не все данные удалось проверить: " + string.Join(" · ", report.Warnings);
            WarningText.Visibility = Visibility.Visible;
        }

        ScanStateText.Text = "Сканирование завершено.";
    }

    private void Recommendation_Checked(object sender, RoutedEventArgs e)
    {
        ApplyRecommendationsButton.IsEnabled = _recommendations.Any(item => item.IsSelected);
    }

    private void ApplyRecommendations_Click(object sender, RoutedEventArgs e)
    {
        string[] selectedIds = _recommendations
            .Where(item => item.IsSelected)
            .Select(item => item.Recommendation.Tweak.Id)
            .ToArray();

        if (selectedIds.Length > 0)
            ApplyRecommendationsRequested?.Invoke(selectedIds.ToHashSet(StringComparer.Ordinal));
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
}
