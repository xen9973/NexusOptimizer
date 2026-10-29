using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusOptimizer.Services;

public enum OptimizationRisk
{
    Low,
    Medium
}

/// <summary>Одно действие оптимизации: описание, риск и способ прочитать и записать его в настройки игры.</summary>
public sealed class OptimizationActionDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>Что делает действие.</summary>
    public required string Description { get; init; }

    /// <summary>Что именно меняется на компьютере.</summary>
    public required string Effect { get; init; }

    /// <summary>Как вернуть всё как было.</summary>
    public required string RevertHint { get; init; }

    public required OptimizationRisk Risk { get; init; }
    public required string RiskNote { get; init; }

    /// <summary>Действие записывается для exe-файлов игры, поэтому без exe оно недоступно.</summary>
    public bool NeedsExecutable { get; init; }

    /// <summary>Сколько exe игры уже имеют это действие в Windows. null — действие срабатывает только при запуске.</summary>
    public Func<TweakState, int>? RegistryCount { get; init; }

    public required Func<GameSettings, bool> Get { get; init; }
    public required Action<GameSettings, bool> Set { get; init; }
}

public sealed class OptimizationProfileDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Набор действий профиля. null — пользовательский набор.</summary>
    public string[]? ActionIds { get; init; }
}

public static class OptimizationCatalog
{
    public const string GpuId = "gpu";
    public const string FullscreenId = "fso";
    public const string PriorityId = "priority";
    public const string PowerId = "power";

    public const string StandardProfile = "Стандартный";
    public const string BalancedProfile = "Сбалансированный";
    public const string PerformanceProfile = "Производительность";
    public const string CustomProfile = "Пользовательский";

    public static IReadOnlyList<OptimizationActionDefinition> Actions { get; } =
    [
        new OptimizationActionDefinition
        {
            Id = GpuId,
            Title = "Производительная видеокарта",
            Description = "Windows будет запускать игру на самой мощной видеокарте. Это важно для ноутбуков и ПК с двумя видеокартами, а если она одна, ничего не изменится.",
            Effect = "Запись в настройках графики Windows для exe-файлов игры (раздел реестра текущего пользователя).",
            RevertHint = "«Отменить» возвращает исходное значение; то же можно сделать в Параметры → Система → Дисплей → Графика.",
            Risk = OptimizationRisk.Low,
            RiskNote = "Затрагивает только файлы этой игры.",
            NeedsExecutable = true,
            RegistryCount = state => state.GpuHighPerformance,
            Get = settings => settings.HighPerformanceGpu,
            Set = (settings, value) => settings.HighPerformanceGpu = value
        },
        new OptimizationActionDefinition
        {
            Id = FullscreenId,
            Title = "Отключить полноэкранные оптимизации Windows",
            Description = "Игра будет работать в обычном полноэкранном режиме. Иногда это уменьшает задержку ввода, иногда ничего не меняет.",
            Effect = "Флаг совместимости для exe-файлов игры (раздел реестра текущего пользователя), как галочка на вкладке «Совместимость» в свойствах файла.",
            RevertHint = "«Отменить» возвращает исходное состояние флага; галочку можно снять и вручную в свойствах exe.",
            Risk = OptimizationRisk.Low,
            RiskNote = "Затрагивает только файлы этой игры. В редких случаях меняется поведение оконного режима.",
            NeedsExecutable = true,
            RegistryCount = state => state.FullscreenDisabled,
            Get = settings => settings.DisableFullscreenOptimizations,
            Set = (settings, value) => settings.DisableFullscreenOptimizations = value
        },
        new OptimizationActionDefinition
        {
            Id = PriorityId,
            Title = "Повышенный приоритет процессов игры",
            Description = "Процессы игры получают приоритет «Выше обычного», когда игра запущена кнопкой «Запустить игру». Работает и для игр Steam: программа дождётся процесса игры.",
            Effect = "Приоритет меняется только у запущенных процессов игры и сбрасывается, когда они закрываются. Ничего не записывается в систему.",
            RevertHint = "Снимите галочку или нажмите «Отменить»: при следующем запуске игра получит обычный приоритет.",
            Risk = OptimizationRisk.Low,
            RiskNote = "Если игра сильно нагружает процессор, другие программы могут отвечать медленнее.",
            Get = settings => settings.AboveNormalPriority,
            Set = (settings, value) => settings.AboveNormalPriority = value
        },
        new OptimizationActionDefinition
        {
            Id = PowerId,
            Title = "Схема электропитания «Высокая производительность»",
            Description = "Включается, когда запускается игра, и возвращается после её закрытия или закрытия программы. На некоторых ноутбуках такой схемы нет.",
            Effect = "Временно переключает активную схему питания Windows. Прежняя схема запоминается и возвращается.",
            RevertHint = "«Отменить» отключает действие для этой игры; текущую схему всегда можно сменить вручную в параметрах питания.",
            Risk = OptimizationRisk.Medium,
            RiskNote = "Растут энергопотребление и нагрев, на ноутбуке быстрее разряжается батарея.",
            Get = settings => settings.HighPerformancePowerPlan,
            Set = (settings, value) => settings.HighPerformancePowerPlan = value
        }
    ];

    public static IReadOnlyList<OptimizationProfileDefinition> Profiles { get; } =
    [
        new OptimizationProfileDefinition
        {
            Name = StandardProfile,
            Description = "Игра запускается как обычно. Все действия выключены, а применение профиля возвращает исходные настройки.",
            ActionIds = []
        },
        new OptimizationProfileDefinition
        {
            Name = BalancedProfile,
            Description = "Производительная видеокарта и повышенный приоритет: минимум побочных эффектов.",
            ActionIds = [GpuId, PriorityId]
        },
        new OptimizationProfileDefinition
        {
            Name = PerformanceProfile,
            Description = "Все действия, включая схему «Высокая производительность»: выше энергопотребление и нагрев.",
            ActionIds = [GpuId, FullscreenId, PriorityId, PowerId]
        },
        new OptimizationProfileDefinition
        {
            Name = CustomProfile,
            Description = "Свой набор действий: отметьте нужные ниже.",
            ActionIds = null
        }
    ];

    public static OptimizationProfileDefinition GetProfile(string name) =>
        Profiles.First(profile => profile.Name == name);

    /// <summary>Название готового профиля, если набор совпал с ним, иначе «Пользовательский».</summary>
    public static string MatchProfile(IEnumerable<string> selectedIds)
    {
        var selected = new HashSet<string>(selectedIds, StringComparer.Ordinal);

        foreach (OptimizationProfileDefinition profile in Profiles)
        {
            if (profile.ActionIds is null)
                continue;

            if (selected.SetEquals(profile.ActionIds))
                return profile.Name;
        }

        return CustomProfile;
    }
}
