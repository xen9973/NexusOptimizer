using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NexusOptimizer.Models;
using NexusOptimizer.Services;
using NexusOptimizer.ViewModels;

namespace NexusOptimizer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;

        LibraryPage.GameOpened += ShowGame;
        GamePage.BackRequested += ShowLibrary;

        Loaded += async (_, _) =>
        {
            // Если прошлый запуск закрылся, не вернув схему электропитания, возвращаем её.
            _ = Task.Run(() => GameSessionService.Current.RestoreLeftoverPowerPlan());

            await _viewModel.InitializeAsync();
        };

        Closing += (_, _) =>
        {
            _viewModel.Shutdown();
            CleanupPage.Shutdown();
            GameSessionService.Current.StopAll();
        };
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string target })
            return;

        if (target == "Cleanup")
            ShowCleanup();
        else
            ShowLibrary();
    }

    private void ShowLibrary()
    {
        NavLibrary.IsChecked = true;
        ShowOnly(LibraryPage);
    }

    private void ShowGame(GameInfo game)
    {
        NavLibrary.IsChecked = true;
        GamePage.ShowGame(game);
        ShowOnly(GamePage);
    }

    private void ShowCleanup()
    {
        NavCleanup.IsChecked = true;
        ShowOnly(CleanupPage);
    }

    private void ShowOnly(UIElement page)
    {
        LibraryPage.Visibility = ReferenceEquals(page, LibraryPage) ? Visibility.Visible : Visibility.Collapsed;
        GamePage.Visibility = ReferenceEquals(page, GamePage) ? Visibility.Visible : Visibility.Collapsed;
        CleanupPage.Visibility = ReferenceEquals(page, CleanupPage) ? Visibility.Visible : Visibility.Collapsed;
    }
}
