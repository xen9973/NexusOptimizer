using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NexusOptimizer.Services;

namespace NexusOptimizer.Views;

public sealed record PcTweakCategoryItem(PcTweakCategory Category, string Name, string Icon, int TweakCount)
{
    public string CountText => TweakCount == 1 ? "1 настройка" : $"{TweakCount} настройки";
}

public sealed class PcTweakItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isAvailable = true;

    public required PcTweakDefinition Definition { get; init; }
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Caution => Definition.Caution;
    public string AvailabilityText => IsAvailable
        ? string.Empty
        : Definition.ServiceName is not null
            ? $"Служба {Definition.ServiceName} не найдена на этом компьютере; настройка недоступна."
            : "Не удалось проверить эту настройку; изменение недоступно.";
    public string ScopeText => Definition.Id == "power-plan"
        ? "Вся система"
        : Definition.Scope == PcTweakScope.WholeComputer
            ? "Весь компьютер · нужны права администратора"
            : "Текущий пользователь";

    public bool IsAvailable
    {
        get => _isAvailable;
        init => _isAvailable = value;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class PcTweaksView : UserControl
{
    private static readonly IReadOnlyList<PcTweakCategoryItem> Categories =
    [
        CreateCategory(PcTweakCategory.Performance, "Производительность", "↗"),
        CreateCategory(PcTweakCategory.Gaming, "Игры", "◈"),
        CreateCategory(PcTweakCategory.Privacy, "Приватность", "◉"),
        CreateCategory(PcTweakCategory.Startup, "Автозагрузка", "▶"),
        CreateCategory(PcTweakCategory.Power, "Электропитание", "⚡"),
        CreateCategory(PcTweakCategory.Explorer, "Проводник", "▤")
    ];

    private static PcTweakCategoryItem CreateCategory(PcTweakCategory category, string name, string icon) =>
        new(category, name, icon, PcTweaksCatalog.Tweaks.Count(tweak => tweak.Category == category));

    private readonly PcTweaksService _service = new();
    private readonly ObservableCollection<PcTweakItem> _items = [];
    private HashSet<string> _readErrorIds = new(StringComparer.Ordinal);
    private IReadOnlySet<string> _unavailableIds = new HashSet<string>(StringComparer.Ordinal);
    private PcTweaksState _state = new();

    public event Action? BackRequested;

    public PcTweaksView()
    {
        InitializeComponent();

        CategoryList.ItemsSource = Categories;
        PresetList.ItemsSource = PcTweaksCatalog.Presets;
        TweaksList.ItemsSource = _items;

        try
        {
            Reload();
            CategoryList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось открыть настройки компьютера", ex);
            StatusText.Text = "Не удалось загрузить сохранённые состояния. Изменения заблокированы: " + ex.Message;
            ApplyButtonEnabled(false);
        }
    }

    private void Reload()
    {
        _state = _service.LoadState();
        HashSet<string> enabled = _service.ReadEnabled(
            _state,
            out IReadOnlyList<PcTweakResult> readErrors,
            out _unavailableIds);
        _readErrorIds = readErrors.Select(error => error.Id).ToHashSet(StringComparer.Ordinal);

        _items.Clear();
        foreach (PcTweakDefinition definition in PcTweaksCatalog.Tweaks)
        {
            _items.Add(new PcTweakItem
            {
                Definition = definition,
                IsSelected = enabled.Contains(definition.Id),
                IsAvailable = !_readErrorIds.Contains(definition.Id) && !_unavailableIds.Contains(definition.Id)
            });
        }

        StatusText.Text = readErrors.Count > 0
            ? "Не удалось проверить часть настроек: " + string.Join(" · ", readErrors.Select(error =>
                $"{GetTweakName(error.Id)}: {error.Message}"))
            : "Выберите категорию. Для системных настроек Windows запросит подтверждение UAC.";
    }

    public void SelectTweaks(IEnumerable<string> ids)
    {
        HashSet<string> selected = ids.ToHashSet(StringComparer.Ordinal);
        foreach (PcTweakItem item in _items)
            item.IsSelected = item.IsAvailable && selected.Contains(item.Definition.Id);

        PresetList.SelectedItem = null;
        PcTweakCategoryItem? firstCategory = Categories.FirstOrDefault(category =>
            _items.Any(item => item.IsSelected && item.Definition.Category == category.Category));
        if (firstCategory is not null)
            CategoryList.SelectedItem = firstCategory;

        StatusText.Text = "Рекомендации выбраны. Проверьте настройки и нажмите «Применить» для подтверждения.";
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is PcTweakCategoryItem category)
            TweaksList.ItemsSource = _items.Where(item => item.Definition.Category == category.Category).ToList();
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is not string preset)
            return;

        HashSet<string> selected = PcTweaksCatalog.GetPreset(preset).ToHashSet(StringComparer.Ordinal);

        foreach (PcTweakItem item in _items)
            item.IsSelected = item.IsAvailable && selected.Contains(item.Definition.Id);

        StatusText.Text = $"Выбран пресет «{preset}». Настройки ещё не изменены.";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        HashSet<string> selected = _items
            .Where(item => item.IsSelected && item.IsAvailable)
            .Select(item => item.Definition.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (NeedsElevation(selected) && !IsAdministrator())
        {
            await RunElevatedOperationAsync("apply", selected);
            return;
        }

        ApplyButtonEnabled(false);
        bool canContinue = true;

        try
        {
            var results = new List<PcTweakResult>();
            results.AddRange(_service.ApplyUserTweaks(_state, selected, _readErrorIds));
            results.AddRange(_service.ApplyMachineTweaks(_state, selected, _readErrorIds));
            if (!_readErrorIds.Contains("power-plan"))
                results.AddRange(_service.ApplyPowerPlan(_state, selected.Contains("power-plan")));

            string[] errors = results.Where(result => !result.Success)
                .Select(result => $"{GetTweakName(result.Id)}: {result.Message}")
                .ToArray();

            Reload();
            StatusText.Text = errors.Length == 0
                ? "Выбранные настройки применены. Перезапуск Windows может потребоваться для отдельных изменений."
                : "Часть настроек не применена: " + string.Join(" · ", errors);
        }
        catch (Exception ex)
        {
            canContinue = false;
            AppLog.Error("Не удалось применить настройки компьютера", ex);
            StatusText.Text = "Не удалось применить настройки: " + ex.Message;
        }
        finally
        {
            ApplyButtonEnabled(canContinue);
            PresetList.SelectedItem = null;
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Вернуть исходные значения всех настроек, которые меняла эта программа?",
                "Подтверждение восстановления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RestoreSettingsAsync();
    }

    private async Task RestoreSettingsAsync()
    {
        if (NeedsElevation(new HashSet<string>(StringComparer.Ordinal)) && !IsAdministrator())
        {
            await RunElevatedOperationAsync("restore", new HashSet<string>(StringComparer.Ordinal));
            return;
        }

        ApplyButtonEnabled(false);
        bool canContinue = true;

        try
        {
            var results = new List<PcTweakResult>();
            var noneSelected = new HashSet<string>(StringComparer.Ordinal);
            results.AddRange(_service.ApplyUserTweaks(_state, noneSelected, _readErrorIds, forceRestore: true));
            results.AddRange(_service.ApplyMachineTweaks(_state, noneSelected, _readErrorIds, forceRestore: true));
            if (!_readErrorIds.Contains("power-plan"))
                results.AddRange(_service.ApplyPowerPlan(_state, selected: false));

            string[] errors = results.Where(result => !result.Success)
                .Select(result => $"{GetTweakName(result.Id)}: {result.Message}")
                .ToArray();

            Reload();
            StatusText.Text = errors.Length == 0
                ? "Исходные значения восстановлены."
                : "Не удалось полностью восстановить настройки: " + string.Join(" · ", errors);
        }
        catch (Exception ex)
        {
            canContinue = false;
            AppLog.Error("Не удалось восстановить системные настройки", ex);
            StatusText.Text = "Не удалось восстановить настройки: " + ex.Message;
        }
        finally
        {
            ApplyButtonEnabled(canContinue);
        }
    }

    private bool NeedsElevation(IReadOnlySet<string> selected)
    {
        bool machineSelected = PcTweaksCatalog.Tweaks.Any(tweak =>
            tweak.Scope == PcTweakScope.WholeComputer
            && tweak.Id != "power-plan"
            && (selected.Contains(tweak.Id) || _state.RegistryBackups.ContainsKey(tweak.Id)));

        return machineSelected;
    }

    private async Task RunElevatedOperationAsync(string operation, IReadOnlySet<string> selected)
    {
        Guid requestId = Guid.NewGuid();
        string resultPath = AppPaths.GetPcTweaksResultFile(requestId);
        ApplyButtonEnabled(false);

        try
        {
            string processPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Не удалось определить путь к программе.");
            string userSid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Не удалось определить учётную запись Windows.");

            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = $"--pc-tweaks-admin {userSid} {operation} {requestId:D} {string.Join(",", selected.OrderBy(id => id, StringComparer.Ordinal))}",
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException("Windows не запустила окно с правами администратора.");

            await process.WaitForExitAsync();

            if (!File.Exists(resultPath))
            {
                StatusText.Text = process.ExitCode == 0
                    ? "Системный процесс завершился без результата."
                    : $"Не удалось выполнить системные настройки (код {process.ExitCode}).";
                return;
            }

            List<PcTweakResult>? results = JsonSerializer.Deserialize<List<PcTweakResult>>(File.ReadAllText(resultPath));
            if (results is null)
                throw new InvalidDataException("Системный процесс вернул неверный результат.");

            string[] errors = results.Where(result => !result.Success)
                .Select(result => $"{GetTweakName(result.Id)}: {result.Message}")
                .ToArray();

            Reload();
            StatusText.Text = errors.Length == 0
                ? operation == "restore"
                    ? "Исходные значения восстановлены."
                    : "Выбранные настройки применены. Перезапуск Windows может потребоваться для отдельных изменений."
                : "Не удалось применить часть настроек: " + string.Join(" · ", errors);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusText.Text = "Запрос прав администратора отменён. Настройки не изменялись.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось выполнить системные твики с правами администратора", ex);
            StatusText.Text = "Не удалось выполнить системные настройки: " + ex.Message;
        }
        finally
        {
            try
            {
                if (File.Exists(resultPath))
                    File.Delete(resultPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Не удалось удалить временный результат системных настроек", ex);
            }

            ApplyButtonEnabled(true);
        }
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string GetTweakName(string id) =>
        PcTweaksCatalog.Tweaks.FirstOrDefault(tweak => tweak.Id == id)?.Name
        ?? (id == "backup" ? "Резервная копия" : id);

    private void ApplyButtonEnabled(bool enabled)
    {
        ApplyButton.IsEnabled = enabled;
        RestoreButton.IsEnabled = enabled;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
}
