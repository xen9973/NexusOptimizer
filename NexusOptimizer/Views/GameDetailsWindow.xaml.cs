
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NexusOptimizer.Models;
using NexusOptimizer.Services;

namespace NexusOptimizer.Views;

public partial class GameDetailsWindow : Window
{
    private readonly GameInfo _game;
    private readonly GameDetailsService _detailsService = new();
    private string? _steamAppId;

    public GameDetailsWindow(GameInfo game)
    {
        InitializeComponent();

        _game = game;

        Title = $"{game.Name} — Nexus Optimizer";
        GameNameText.Text = game.Name;
        GamePlatformText.Text = game.Platform;
        GamePathText.Text = game.InstallPath;

        GameIconImage.Source = GameIconHelper.GetIcon(game.InstallPath);

        Loaded += async (_, _) => await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        DescriptionText.Text = "Загружаем описание из интернет-магазина...";
        WindowStatusText.Text = "Получение информации...";

        var details = await _detailsService.GetDetailsAsync(
            _game.Name,
            _game.InstallPath);

        DescriptionText.Text = details.Description;
        _steamAppId = details.AppId;

        WindowStatusText.Text = _steamAppId is null
            ? "Локальная игра"
            : $"Steam App ID: {_steamAppId}";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_game.InstallPath))
        {
            MessageBox.Show(
                "Папка игры не найдена.",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_game.InstallPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось открыть папку.\n{ex.Message}",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Для Steam предпочитаем штатный запуск через клиент.
            if (!string.IsNullOrWhiteSpace(_steamAppId))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"steam://run/{_steamAppId}",
                    UseShellExecute = true
                });

                WindowStatusText.Text = "Команда запуска передана Steam.";

                OptimizationStatusText.Text =
                    OptimizeOnLaunchCheckBox.IsChecked == true
                        ? "Steam запускает игру. Приоритет процесса не изменялся."
                        : "Запуск через Steam без изменения приоритета.";

                return;
            }

            string? executable = FindExecutable(_game.InstallPath);

            if (executable is null)
            {
                MessageBox.Show(
                    "Не удалось найти EXE-файл игры. " +
                    "Попробуйте запустить игру через её лаунчер.",
                    "Nexus Optimizer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = true
            });

            WindowStatusText.Text = "Команда запуска выполнена.";

            if (process is not null &&
                OptimizeOnLaunchCheckBox.IsChecked == true)
            {
                try
                {
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;

                    OptimizationStatusText.Text =
                        "Приоритет «Выше обычного» применён к запущенному процессу.";
                }
                catch
                {
                    OptimizationStatusText.Text =
                        "Игра запущена, но изменить приоритет процесса не удалось.";
                }
            }
            else
            {
                OptimizationStatusText.Text =
                    "Игра запущена без изменения приоритета.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось запустить игру.\n{ex.Message}",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string? FindExecutable(string installPath)
    {
        try
        {
            if (!Directory.Exists(installPath))
                return null;

            var files = Directory
                .EnumerateFiles(
                    installPath,
                    "*.exe",
                    SearchOption.TopDirectoryOnly)
                .ToArray();

            if (files.Length == 0)
            {
                files = Directory
                    .EnumerateDirectories(installPath)
                    .Take(20)
                    .SelectMany(folder =>
                    {
                        try
                        {
                            return Directory.EnumerateFiles(
                                folder,
                                "*.exe",
                                SearchOption.TopDirectoryOnly);
                        }
                        catch
                        {
                            return Array.Empty<string>();
                        }
                    })
                    .ToArray();
            }

            string folderName = Path.GetFileName(
                installPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

            return files
                .OrderByDescending(path =>
                    string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        folderName,
                        StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path =>
                {
                    try { return new FileInfo(path).Length; }
                    catch { return 0L; }
                })
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
