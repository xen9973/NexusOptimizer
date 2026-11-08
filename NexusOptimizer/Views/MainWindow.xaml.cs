using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using NexusOptimizer.Models;
using NexusOptimizer.Services;
using NexusOptimizer.ViewModels;
using NexusOptimizer.Views;

namespace NexusOptimizer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly AppPreferencesStore _preferencesStore = new();
    private bool _isThemeTransitioning;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;
        UpdateThemeButton();
        UpdateWindowStateButton();
        StateChanged += (_, _) => UpdateWindowStateButton();

        LibraryPage.GameOpened += ShowGame;
        GamePage.BackRequested += ShowLibrary;
        PcTweaksPage.BackRequested += ShowLibrary;
        PcSystemScanPage.BackRequested += ShowLibrary;
        PcSystemScanPage.ApplyRecommendationsRequested += recommendations =>
        {
            PcTweaksPage.SelectTweaks(recommendations);
            ShowPcTweaks();
        };

        Loaded += async (_, _) =>
        {
            UiEntranceAnimator.AnimateOnce(LibraryPage);

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
        else if (target == "PcTweaks")
            ShowPcTweaks();
        else if (target == "SystemScan")
            ShowSystemScan();
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

    private void ShowPcTweaks()
    {
        NavPcTweaks.IsChecked = true;
        ShowOnly(PcTweaksPage);
    }

    private void ShowSystemScan()
    {
        NavSystemScan.IsChecked = true;
        ShowOnly(PcSystemScanPage);
    }

    private void ShowOnly(UIElement page)
    {
        LibraryPage.Visibility = ReferenceEquals(page, LibraryPage) ? Visibility.Visible : Visibility.Collapsed;
        GamePage.Visibility = ReferenceEquals(page, GamePage) ? Visibility.Visible : Visibility.Collapsed;
        CleanupPage.Visibility = ReferenceEquals(page, CleanupPage) ? Visibility.Visible : Visibility.Collapsed;
        PcTweaksPage.Visibility = ReferenceEquals(page, PcTweaksPage) ? Visibility.Visible : Visibility.Collapsed;
        PcSystemScanPage.Visibility = ReferenceEquals(page, PcSystemScanPage) ? Visibility.Visible : Visibility.Collapsed;

        if (page is FrameworkElement frameworkElement)
            UiEntranceAnimator.AnimateOnce(frameworkElement);
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isThemeTransitioning)
            return;

        string nextTheme = AppThemeService.GetNextTheme(AppThemeService.CurrentTheme);
        BitmapSource previousFrame;
        Point revealCenter;

        try
        {
            ShellRoot.UpdateLayout();
            double dpi = VisualTreeHelper.GetDpi(ShellRoot).DpiScaleX;
            var snapshot = new RenderTargetBitmap(
                (int)Math.Ceiling(ShellRoot.ActualWidth * dpi),
                (int)Math.Ceiling(ShellRoot.ActualHeight * dpi),
                96 * dpi,
                96 * dpi,
                PixelFormats.Pbgra32);
            snapshot.Render(ShellRoot);
            previousFrame = snapshot;

            Point buttonCenter = ThemeToggleButton.TransformToVisual(ShellRoot)
                .Transform(new Point(ThemeToggleButton.ActualWidth / 2, ThemeToggleButton.ActualHeight / 2));
            revealCenter = buttonCenter;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            AppLog.Warn("Не удалось подготовить анимацию смены темы", ex);
            previousFrame = null!;
            revealCenter = new Point(ShellRoot.ActualWidth / 2, ShellRoot.ActualHeight / 2);
        }

        try
        {
            _preferencesStore.SaveTheme(nextTheme);
            AppThemeService.Apply(Application.Current, nextTheme);
        }
        catch (Exception ex)
        {
            try
            {
                _preferencesStore.SaveTheme(AppThemeService.CurrentTheme);
            }
            catch (Exception rollbackException)
            {
                AppLog.Error("Не удалось восстановить сохранённую тему после ошибки", rollbackException);
            }

            AppLog.Error("Не удалось изменить тему приложения", ex);
            MessageBox.Show(
                "Не удалось изменить или сохранить тему:\n" + ex.Message,
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        UpdateThemeButton();

        if (previousFrame is not null)
        {
            try
            {
                PlayThemeReveal(previousFrame, revealCenter);
            }
            catch (Exception ex)
            {
                _isThemeTransitioning = false;
                ThemeToggleButton.IsEnabled = true;
                AppLog.Warn("Тема изменена, но анимацию перехода не удалось запустить", ex);
            }
        }
    }

    private void PlayThemeReveal(BitmapSource previousFrame, Point center)
    {
        _isThemeTransitioning = true;
        ThemeToggleButton.IsEnabled = false;
        double width = ShellRoot.ActualWidth;
        double height = ShellRoot.ActualHeight;
        double radius = Math.Sqrt(width * width + height * height);
        var hole = new EllipseGeometry(center, 0, 0);
        var clip = new GeometryGroup { FillRule = FillRule.EvenOdd };
        clip.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
        clip.Children.Add(hole);

        var overlay = new Image
        {
            Source = previousFrame,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            Clip = clip
        };
        Grid.SetColumnSpan(overlay, 2);
        Panel.SetZIndex(overlay, 1000);
        ShellRoot.Children.Add(overlay);

        var reveal = new DoubleAnimation(0, radius, TimeSpan.FromMilliseconds(440))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        reveal.Completed += (_, _) =>
        {
            ShellRoot.Children.Remove(overlay);
            _isThemeTransitioning = false;
            ThemeToggleButton.IsEnabled = true;
        };
        hole.BeginAnimation(EllipseGeometry.RadiusXProperty, reveal);
        hole.BeginAnimation(EllipseGeometry.RadiusYProperty, reveal.Clone());
    }

    private void UpdateThemeButton()
    {
        bool isDark = AppThemeService.CurrentTheme == AppThemeService.DarkTheme;
        ThemeToggleButton.Content = isDark ? "☀   Светлая тема" : "☾   Тёмная тема";
        ThemeToggleButton.ToolTip = isDark ? "Переключиться на светлую тему" : "Переключиться на тёмную тему";
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateWindowStateButton()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreGlyph.Text = isMaximized ? "❐" : "□";
        MaximizeRestoreButton.ToolTip = isMaximized ? "Восстановить" : "Развернуть";
    }
}
