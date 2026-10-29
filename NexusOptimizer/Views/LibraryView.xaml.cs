using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexusOptimizer.Models;
using NexusOptimizer.ViewModels;

namespace NexusOptimizer.Views;

public partial class LibraryView : UserControl
{
    /// <summary>Пользователь кликнул по карточке игры.</summary>
    public event Action<GameInfo>? GameOpened;

    public LibraryView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && viewModel.AddFolder())
            await viewModel.ScanAsync();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.ToggleScanAsync();
    }

    private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { Tag: string folder })
            await viewModel.RemoveFolderAsync(folder);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { Tag: GameInfo game })
            viewModel.OpenGameFolder(game);
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { Tag: GameInfo game })
            viewModel.CopyGamePath(game);
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { Tag: GameInfo game })
            viewModel.ToggleFavorite(game);
    }

    private void GameCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: GameInfo game })
            GameOpened?.Invoke(game);
    }
}
