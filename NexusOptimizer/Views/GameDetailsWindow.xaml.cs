
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NexusOptimizer.Models;
using NexusOptimizer.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
LoadSettings();
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
            string arguments = LaunchArgumentsTextBox.Text.Trim();
            bool useAboveNormal =
                AboveNormalPriorityCheckBox.IsChecked == true;

            // Сохраняем параметры перед запуском.
            SaveSettings_Click(sender, e);

            // Для Steam запускаем игру через клиент.
            if (!string.IsNullOrWhiteSpace(_steamAppId))
            {
                string steamUri = $"steam://run/{_steamAppId}";

                if (!string.IsNullOrWhiteSpace(arguments))
                    steamUri += $"//{Uri.EscapeDataString(arguments)}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = steamUri,
                    UseShellExecute = true
                });

                WindowStatusText.Text = "Команда передана Steam.";

                OptimizationStatusText.Text = useAboveNormal
                    ? "Приоритет для Steam-игры автоматически не изменён."
                    : "Запуск выполнен без изменения приоритета.";

                return;
            }

            string? executable = FindExecutable(_game.InstallPath);

            if (executable is null)
            {
                MessageBox.Show(
                    "Не удалось найти EXE-файл игры. Попробуйте запустить её через лаунчер.",
                    "Nexus Optimizer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                Arguments = arguments,
                UseShellExecute = false
            };

            Process? process = Process.Start(startInfo);

            if (process is null)
            {
                WindowStatusText.Text = "Не удалось запустить игру.";
                return;
            }

            WindowStatusText.Text = "Игра запущена.";

            if (useAboveNormal)
            {
                try
                {
                    process.PriorityClass = ProcessPriorityClass.AboveNormal;

                    OptimizationStatusText.Text =
                        "Для запущенного процесса установлен приоритет «Выше обычного».";
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
                    "Игра запущена со стандартным приоритетом.";
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
    private sealed class GameSettings
    {
       public string LaunchArguments { get; set; } = "";
       public bool AboveNormalPriority { get; set; }
    }
    private string SettingsFilePath 
    {
        get
        { 
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NexusOptimizer",
                "GameSettings");
            
            Directory.CreateDirectory(directory);

            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_game.InstallPath.Trim().ToLowerInvariant())));

            return Path.Combine(directory, $"{key}.json");
        }
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return;
            string json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<GameSettings>(json);
            if (settings is null)
                return;

            LaunchArgumentsTextBox.Text = settings.LaunchArguments;
            AboveNormalPriorityCheckBox.IsChecked = settings.AboveNormalPriority;
            SettingsStatusText.Text = "Настройки загружены.";
        }
        catch
        {
            SettingsStatusText.Text = "Не удалось загрузить настройки.";
        }
    }
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new GameSettings
            {
                LaunchArguments = LaunchArgumentsTextBox.Text.Trim(),
                AboveNormalPriority = AboveNormalPriorityCheckBox.IsChecked == true
            };

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(SettingsFilePath, json);

            SettingsStatusText.Text = "Настройки сохранены.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось сохранить настройки.\n{ex.Message}",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Сбросить сохранённые параметры запуска и приоритета для этой игры?",
            "Сброс настроек",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        LaunchArgumentsTextBox.Clear();
        AboveNormalPriorityCheckBox.IsChecked = false;

        try
        {
            if (File.Exists(SettingsFilePath))
                File.Delete(SettingsFilePath);

            SettingsStatusText.Text = "Настройки сброшены.";
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = $"Не удалось удалить сохранённый профиль: {ex.Message}";
        }
    }

    private void OpenGameDirectory_Click(object sender, RoutedEventArgs e)
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

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_game.InstallPath}\"",
            UseShellExecute = true
        });
    }
}
