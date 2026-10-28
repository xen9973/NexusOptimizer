using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NexusOptimizer.Services;

namespace NexusOptimizer.ViewModels;

/// <summary>Одна категория очистки в списке.</summary>
public sealed class CleanupItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isMeasured;
    private long _bytes;
    private int _files;
    private string? _message;

    public CleanupItem(CleanupDefinition definition)
    {
        Definition = definition;
        _isSelected = definition.SelectedByDefault;
    }

    public CleanupDefinition Definition { get; }

    public string Title => Definition.Title;

    public string Description => Definition.Description;

    public string FoldersHint => Definition.Kind == CleanupKind.RecycleBin
        ? "Корзина Windows"
        : string.Join(Environment.NewLine, Definition.Folders);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public long Bytes => _bytes;

    public int Files => _files;

    /// <summary>Анализ выполнен и есть что удалять.</summary>
    public bool HasData => _isMeasured && (_bytes > 0 || _files > 0);

    public string SizeText
    {
        get
        {
            if (_message is not null)
                return _message;

            if (!_isMeasured)
                return "Не анализировалось";

            if (!HasData)
                return "Нечего чистить";

            return ByteSize.Format(_bytes);
        }
    }

    public string FilesText => HasData ? $"Файлов: {_files}" : string.Empty;

    public void SetMeasuring()
    {
        _message = "Анализ…";
        OnPropertyChanged(nameof(SizeText));
    }

    public void SetMeasured(CleanupSize size)
    {
        _bytes = size.Bytes;
        _files = size.Files;
        _isMeasured = true;
        _message = null;

        OnPropertyChanged(nameof(Bytes));
        OnPropertyChanged(nameof(Files));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(FilesText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class CleanupViewModel : INotifyPropertyChanged
{
    private readonly CleanupService _service = new();

    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private string _status = "Нажмите «Проанализировать», чтобы увидеть, сколько места можно освободить.";

    public CleanupViewModel()
    {
        foreach (CleanupDefinition definition in _service.Definitions)
        {
            var item = new CleanupItem(definition);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CleanupItem.IsSelected) or nameof(CleanupItem.HasData))
                    RaiseStateChanged();
            };

            Items.Add(item);
        }
    }

    public ObservableCollection<CleanupItem> Items { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            OnPropertyChanged();
            RaiseStateChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged();
        }
    }

    public bool CanAnalyze => !IsBusy;

    public bool CanClean => !IsBusy && Items.Any(item => item.IsSelected && item.HasData);

    public string Summary
    {
        get
        {
            if (!Items.Any(item => item.HasData))
                return "Анализ ещё не выполнялся или чистить нечего";

            long total = Items.Where(item => item.HasData).Sum(item => item.Bytes);
            long selected = Items.Where(item => item.IsSelected && item.HasData).Sum(item => item.Bytes);

            return $"Найдено: {ByteSize.Format(total)}. Будет освобождено: {ByteSize.Format(selected)}";
        }
    }

    // ───────────────────────── Действия ─────────────────────────

    public async Task AnalyzeAsync()
    {
        if (IsBusy)
            return;

        CancellationToken token = BeginBusy();

        try
        {
            foreach (CleanupItem item in Items)
            {
                token.ThrowIfCancellationRequested();

                Status = $"Анализ: {item.Title}…";
                item.SetMeasuring();

                CleanupSize size = await _service.MeasureAsync(item.Definition, token);
                item.SetMeasured(size);
            }

            Status = "Анализ завершён. Отметьте, что нужно удалить.";
        }
        catch (OperationCanceledException)
        {
            Status = "Анализ отменён.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Очистка: ошибка анализа", ex);
            Status = "Не удалось выполнить анализ. Подробности записаны в журнал.";
        }
        finally
        {
            EndBusy();
        }
    }

    public async Task CleanSelectedAsync()
    {
        if (IsBusy)
            return;

        var selected = Items.Where(item => item.IsSelected && item.HasData).ToList();

        if (selected.Count == 0)
            return;

        string list = string.Join(
            Environment.NewLine,
            selected.Select(item => $"• {item.Title}: {ByteSize.Format(item.Bytes)}"));

        long total = selected.Sum(item => item.Bytes);
        bool hasRecycleBin = selected.Any(item => item.Definition.Kind == CleanupKind.RecycleBin);

        string message = $"Будут удалены файлы из выбранных разделов:{Environment.NewLine}{Environment.NewLine}"
                         + $"{list}{Environment.NewLine}{Environment.NewLine}"
                         + $"Всего около {ByteSize.Format(total)}.";

        if (hasRecycleBin)
            message += $"{Environment.NewLine}{Environment.NewLine}Файлы из корзины восстановить будет нельзя.";

        message += $"{Environment.NewLine}{Environment.NewLine}Продолжить?";

        MessageBoxResult answer = MessageBox.Show(
            message,
            "Подтвердите очистку",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        CancellationToken token = BeginBusy();

        long freed = 0;
        int deleted = 0;
        int skipped = 0;
        bool cancelled = false;

        try
        {
            foreach (CleanupItem item in selected)
            {
                token.ThrowIfCancellationRequested();

                Status = $"Очистка: {item.Title}…";

                CleanupOutcome outcome = await _service.CleanAsync(item.Definition, token);

                freed += outcome.FreedBytes;
                deleted += outcome.DeletedFiles;
                skipped += outcome.SkippedFiles;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Очистка: ошибка удаления", ex);
            Status = "Очистка остановилась из-за ошибки. Подробности записаны в журнал.";
            EndBusy();
            return;
        }

        EndBusy();

        string result = (cancelled ? "Очистка прервана. " : string.Empty)
                        + $"Освобождено: {ByteSize.Format(freed)}. Удалено файлов: {deleted}";

        if (skipped > 0)
            result += $". Пропущено (заняты или нет доступа): {skipped}";

        result += ".";

        // Пересчитываем размеры, чтобы список показывал актуальное состояние.
        await AnalyzeAsync();
        Status = result;
    }

    public void Cancel() => _cts?.Cancel();

    // ───────────────────────── Внутреннее ─────────────────────────

    private CancellationToken BeginBusy()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        return _cts.Token;
    }

    private void EndBusy()
    {
        IsBusy = false;
        _cts?.Dispose();
        _cts = null;
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(CanAnalyze));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(Summary));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
