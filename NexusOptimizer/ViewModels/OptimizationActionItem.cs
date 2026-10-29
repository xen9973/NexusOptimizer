using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NexusOptimizer.Services;

namespace NexusOptimizer.ViewModels;

/// <summary>Действие оптимизации в списке на странице игры: выбор, текущее состояние и результат применения.</summary>
public sealed class OptimizationActionItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isAvailable = true;
    private bool _isActive;
    private string _stateText = string.Empty;
    private string _resultText = string.Empty;

    public OptimizationActionItem(OptimizationActionDefinition definition)
    {
        Definition = definition;
    }

    public OptimizationActionDefinition Definition { get; }

    public string Title => Definition.Title;

    public string Description => Definition.Description;

    public string DetailsText => $"Что изменится: {Definition.Effect}\nКак отменить: {Definition.RevertHint}";

    public string RiskNote => Definition.RiskNote;

    public string RiskText => Definition.Risk == OptimizationRisk.Low ? "Риск: низкий" : "Риск: средний";

    public bool IsMediumRisk => Definition.Risk == OptimizationRisk.Medium;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>false, если действие нельзя применить (например, не найден exe игры).</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetField(ref _isAvailable, value);
    }

    /// <summary>Действие сейчас включено: можно его отменить.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public string StateText
    {
        get => _stateText;
        set => SetField(ref _stateText, value);
    }

    public string ResultText
    {
        get => _resultText;
        set => SetField(ref _resultText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
