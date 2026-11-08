using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace NexusOptimizer.Services;

public enum PcTweakCategory
{
    Performance,
    Gaming,
    Privacy,
    Startup,
    Power,
    Explorer
}

public enum PcTweakScope
{
    CurrentUser,
    WholeComputer
}

public sealed record PcTweakDefinition(
    string Id,
    PcTweakCategory Category,
    string Name,
    string Description,
    string Caution,
    PcTweakScope Scope,
    string? RegistryPath,
    string? ValueName,
    RegistryValueKind? ValueKind,
    object? EnabledValue)
{
    public string? ServiceName { get; init; }
}

public static class PcTweaksCatalog
{
    public static IReadOnlyList<PcTweakDefinition> Tweaks { get; } =
    [
        new("window-animations", PcTweakCategory.Performance, "Отключить анимацию окон",
            "Сокращает анимацию сворачивания и разворачивания окон текущего пользователя.",
            "Изменение только визуальное; производительность игр не гарантируется.", PcTweakScope.CurrentUser,
            @"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegistryValueKind.String, "0"),
        new("menu-delay", PcTweakCategory.Performance, "Уменьшить задержку меню",
            "Меню Windows открываются без стандартной задержки.",
            "Меняется только интерфейс текущего пользователя.", PcTweakScope.CurrentUser,
            @"Control Panel\Desktop", "MenuShowDelay", RegistryValueKind.String, "0"),
        new("transparency-effects", PcTweakCategory.Performance, "Отключить эффекты прозрачности",
            "Отключает прозрачность меню «Пуск», панели задач и некоторых окон.",
            "Это визуальное изменение; на современных компьютерах прирост скорости может быть незаметен.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryValueKind.DWord, 0),
        new("mouse-hover-delay", PcTweakCategory.Performance, "Уменьшить задержку наведения",
            "Уменьшает время ожидания перед появлением всплывающих подсказок интерфейса.",
            "Подсказки могут появляться почти сразу при наведении.", PcTweakScope.CurrentUser,
            @"Control Panel\Mouse", "MouseHoverTime", RegistryValueKind.String, "10"),
        new("game-mode", PcTweakCategory.Gaming, "Включить игровой режим",
            "Разрешает Windows автоматически использовать игровой режим.",
            "Фактический эффект зависит от версии Windows и игры.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\GameBar", "AutoGameModeEnabled", RegistryValueKind.DWord, 1),
        new("game-capture", PcTweakCategory.Gaming, "Отключить фоновую запись игр",
            "Отключает фоновую запись клипов Xbox Game Bar для текущего пользователя.",
            "Фоновая запись и связанные с ней клипы перестанут работать.", PcTweakScope.CurrentUser,
            @"System\GameConfigStore", "GameDVR_Enabled", RegistryValueKind.DWord, 0),
        new("game-bar-startup-panel", PcTweakCategory.Gaming, "Скрыть приветственную панель Xbox Game Bar",
            "Отключает стартовую подсказку Game Bar при первом запуске игр.",
            "Не отключает саму Game Bar и её функции записи или оверлея.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\GameBar", "ShowStartupPanel", RegistryValueKind.DWord, 0),
        new("game-capture-all-users", PcTweakCategory.Gaming, "Отключить запись игр для всех пользователей",
            "Запрещает Xbox Game DVR на уровне компьютера.",
            "Системная политика действует для всех пользователей и может переопределять их настройки.", PcTweakScope.WholeComputer,
            @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", RegistryValueKind.DWord, 0),
        ServiceStart("xbl-auth-manager", PcTweakCategory.Gaming, "Перевести Xbox Live Auth Manager в ручной запуск",
            "Меняет запуск службы Xbox Live Auth Manager на ручной. Служба останется доступна приложениям, которые запросят её.",
            "Не выбирайте, если используете Xbox/Game Pass, вход в Xbox-профиль или игры, которым нужна служба.", "XblAuthManager"),
        ServiceStart("xbl-game-save", PcTweakCategory.Gaming, "Перевести Xbox Live Game Save в ручной запуск",
            "Меняет запуск службы сохранений Xbox на ручной; служба сможет запускаться по запросу.",
            "Не выбирайте, если используете синхронизацию сохранений Xbox/Game Pass.", "XblGameSave"),
        ServiceStart("xbox-networking", PcTweakCategory.Gaming, "Перевести Xbox Networking Service в ручной запуск",
            "Переводит сетевую службу Xbox в ручной запуск.",
            "Не выбирайте, если используете Xbox Live, Game Pass или сетевые функции Xbox.", "XboxNetApiSvc"),
        ServiceStart("xbox-accessory", PcTweakCategory.Gaming, "Перевести Xbox Accessory Management в ручной запуск",
            "Переводит службу аксессуаров Xbox в ручной запуск.",
            "Не выбирайте, если используете аксессуары Xbox или приложение Xbox Accessories.", "XboxGipSvc"),
        new("advertising-id", PcTweakCategory.Privacy, "Отключить рекламный идентификатор",
            "Запрещает приложениям использовать рекламный идентификатор Windows текущего пользователя.",
            "Некоторые приложения могут показывать менее персонализированную рекламу.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", RegistryValueKind.DWord, 0),
        new("start-suggestions", PcTweakCategory.Privacy, "Отключить предложения в меню «Пуск»",
            "Скрывает рекомендации приложений и предложения в меню «Пуск».",
            "В меню «Пуск» станет меньше рекомендованного содержимого.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", RegistryValueKind.DWord, 0),
        new("start-app-tracking", PcTweakCategory.Privacy, "Отключить отслеживание запуска приложений",
            "Запрещает Windows учитывать часто запускаемые приложения при формировании меню «Пуск».",
            "Список часто используемых приложений и персонализация меню «Пуск» могут измениться.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", RegistryValueKind.DWord, 0),
        new("tailored-experiences", PcTweakCategory.Privacy, "Отключить персонализированные рекомендации",
            "Отключает рекомендации, основанные на диагностических данных текущего пользователя.",
            "Некоторые подсказки Windows станут менее персонализированными.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", RegistryValueKind.DWord, 0),
        new("startup-delay", PcTweakCategory.Startup, "Отключить задержку автозапуска",
            "Убирает задержку перед запуском приложений из автозагрузки.",
            "Приложения стартуют раньше после входа и могут увеличить нагрузку при входе.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", RegistryValueKind.DWord, 0),
        ServiceStart("sysmain-manual", PcTweakCategory.Performance, "Перевести SysMain в ручной запуск",
            "Меняет запуск SysMain (ранее Superfetch) на ручной, если служба установлена.",
            "Не гарантирует рост FPS; на некоторых системах SysMain полезна. Может потребоваться перезагрузка.", "SysMain"),
        ServiceStart("search-manual", PcTweakCategory.Performance, "Перевести Windows Search в ручной запуск",
            "Меняет запуск индексатора поиска Windows на ручной.",
            "Индексация и поиск по содержимому могут работать медленнее или запускаться по запросу. Может потребоваться перезагрузка.", "WSearch"),
        new("restart-apps-after-signin", PcTweakCategory.Startup, "Не перезапускать приложения после входа",
            "Отключает автоматический повторный запуск поддерживаемых приложений после выхода из системы или перезагрузки.",
            "Открытые приложения не будут автоматически восстановлены после входа.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "RestartApps", RegistryValueKind.DWord, 0),
        new("power-plan", PcTweakCategory.Power, "Включить схему «Высокая производительность»",
            "Переключает активную схему питания Windows на высокую производительность.",
            "Может увеличить энергопотребление, нагрев и шум; на некоторых устройствах схема недоступна.", PcTweakScope.WholeComputer,
            null, null, null, null),
        new("disable-power-throttling", PcTweakCategory.Power, "Отключить энергосбережение для фоновых процессов",
            "Отключает механизм Windows Power Throttling, который ограничивает энергопотребление фоновых процессов.",
            "Может заметно увеличить расход батареи, нагрев и энергопотребление. Требуются права администратора; может потребоваться перезагрузка.", PcTweakScope.WholeComputer,
            @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", RegistryValueKind.DWord, 1),
        new("show-file-extensions", PcTweakCategory.Explorer, "Показывать расширения файлов",
            "Показывает расширения файлов (например, .txt и .exe) в Проводнике.",
            "Рекомендуется для распознавания типов файлов; само по себе не меняет содержимое файлов.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", RegistryValueKind.DWord, 0),
        new("show-hidden-files", PcTweakCategory.Explorer, "Показывать скрытые файлы",
            "Показывает скрытые файлы и папки в Проводнике.",
            "Среди них могут быть служебные файлы Windows и приложений — не удаляйте их без необходимости.", PcTweakScope.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", RegistryValueKind.DWord, 1)
    ];

    public static IReadOnlyList<string> Presets { get; } = ["Сбалансированный", "Производительность", "Приватность"];

    public static IReadOnlyList<string> GetPreset(string name) => name switch
    {
        "Сбалансированный" => ["game-mode", "advertising-id", "show-file-extensions"],
        "Производительность" => ["window-animations", "menu-delay", "transparency-effects", "mouse-hover-delay", "game-mode", "game-capture", "startup-delay", "power-plan"],
        "Приватность" => ["game-capture", "game-capture-all-users", "advertising-id", "start-suggestions", "start-app-tracking", "tailored-experiences"],
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Неизвестный пресет.")
    };

    private static PcTweakDefinition ServiceStart(
        string id,
        PcTweakCategory category,
        string name,
        string description,
        string caution,
        string serviceName) =>
        new(id, category, name, description, caution, PcTweakScope.WholeComputer,
            $@"SYSTEM\CurrentControlSet\Services\{serviceName}", "Start", RegistryValueKind.DWord, 3)
        {
            ServiceName = serviceName
        };
}
