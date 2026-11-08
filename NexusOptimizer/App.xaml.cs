using System;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using NexusOptimizer.Services;
using NexusOptimizer.Views;

namespace NexusOptimizer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Необработанное исключение", args.ExceptionObject as Exception);

        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0] == "--pc-tweaks-admin")
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            bool sameUser = e.Args.Length >= 2 && string.Equals(
                identity.User?.Value,
                e.Args[1],
                StringComparison.OrdinalIgnoreCase);
            bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

            if (!sameUser || !elevated)
            {
                MessageBox.Show(
                    "Для системных твиков запустите программу с повышенными правами той же учётной записи Windows.",
                    "Нужны права администратора",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            if (e.Args.Length < 5 || !Guid.TryParse(e.Args[3], out Guid requestId))
            {
                Shutdown(1);
                return;
            }

            PcTweaksAdminCommand.Execute(e.Args[2], requestId, e.Args[4]);
            Shutdown();
            return;
        }

        AppThemeService.ApplySavedTheme(this);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (!new AppPreferencesStore().Load().HasCompletedWelcome)
        {
            mainWindow.Dispatcher.BeginInvoke(() =>
            {
                var welcomeWindow = new WelcomeWindow { Owner = mainWindow };
                welcomeWindow.ShowDialog();

                try
                {
                    new AppPreferencesStore().SaveWelcomeCompleted();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Не удалось сохранить завершение приветственного окна", ex);
                    MessageBox.Show(
                        "Не удалось сохранить завершение приветствия. При следующем запуске оно появится снова.\n" + ex.Message,
                        "Nexus Optimizer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Ошибка в интерфейсе", e.Exception);

        MessageBox.Show(
            "Произошла непредвиденная ошибка. Подробности записаны в журнал:\n" + AppPaths.LogFile,
            "Nexus Optimizer",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
