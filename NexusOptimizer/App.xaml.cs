using System;
using System.Windows;
using System.Windows.Threading;
using NexusOptimizer.Services;

namespace NexusOptimizer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Необработанное исключение", args.ExceptionObject as Exception);

        base.OnStartup(e);
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
