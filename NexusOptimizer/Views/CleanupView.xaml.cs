using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NexusOptimizer.Services;
using NexusOptimizer.ViewModels;

namespace NexusOptimizer.Views;

public partial class CleanupView : UserControl
{
    private readonly CleanupViewModel _viewModel = new();
    private bool _analyzedOnce;

    public CleanupView()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // Анализ только читает файлы, поэтому запускаем его при первом открытии раздела.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && !_analyzedOnce)
            {
                _analyzedOnce = true;
                await _viewModel.AnalyzeAsync();
            }
        };
    }

    public void Shutdown() => _viewModel.Cancel();

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await _viewModel.AnalyzeAsync();

    private async void Clean_Click(object sender, RoutedEventArgs e) => await _viewModel.CleanSelectedAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.Cancel();

    private void OpenLog_Click(object sender, RoutedEventArgs e) => _viewModel.OpenLogFolder();

    private async void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.AddExcludedFolder())
            await _viewModel.AnalyzeAsync();
    }

    private async void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder } && _viewModel.RemoveExcludedFolder(folder))
            await _viewModel.AnalyzeAsync();
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CleanupItem item })
            return;

        CleanupPreview? preview = await _viewModel.LoadPreviewAsync(item);

        if (preview is null)
            return;

        string summary = item.Definition.Kind == CleanupKind.RecycleBin
            ? $"В корзине элементов: {preview.TotalFiles}, всего {ByteSize.Format(preview.TotalBytes)}. Содержимое корзины здесь не показывается."
            : preview.TotalFiles > preview.Items.Count
                ? $"Показаны {preview.Items.Count} самых крупных файлов из {preview.TotalFiles}. Всего {ByteSize.Format(preview.TotalBytes)}. Ничего не удалено."
                : $"Файлов: {preview.TotalFiles}, всего {ByteSize.Format(preview.TotalBytes)}. Ничего не удалено.";

        var rows = preview.Items
            .Select(entry => new CleanupPreviewRow(entry.Path, ByteSize.Format(entry.Bytes)))
            .ToList();

        var window = new CleanupPreviewWindow(item.Title, summary, rows)
        {
            Owner = Window.GetWindow(this)
        };

        window.ShowDialog();
    }
}
