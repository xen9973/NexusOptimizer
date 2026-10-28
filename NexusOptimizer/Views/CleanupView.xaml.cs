using System.Windows;
using System.Windows.Controls;
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
}
