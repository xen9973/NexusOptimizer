
using System.Windows;
using System.Windows.Controls;
using NexusOptimizer.Models;
using NexusOptimizer.ViewModels;

namespace NexusOptimizer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddFolder();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ScanAsync();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameInfo game })
            _viewModel.OpenGameFolder(game);
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameInfo game })
            _viewModel.CopyGamePath(game);
    }
}
