using System;
using System.Linq;
using System.Windows;

namespace NexusOptimizer.Services;

public static class AppThemeService
{
    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";

    public static string CurrentTheme { get; private set; } = DarkTheme;

    public static bool IsSupportedTheme(string? theme) =>
        theme is DarkTheme or LightTheme;

    public static void ApplySavedTheme(Application application)
    {
        string theme = new AppPreferencesStore().Load().Theme;
        Apply(application, theme);
    }

    public static void Apply(Application application, string theme)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!IsSupportedTheme(theme))
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "Неизвестная тема приложения.");

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"/NexusOptimizer;component/Themes/{theme}.xaml", UriKind.Relative)
        };

        ResourceDictionary? currentTheme = application.Resources.MergedDictionaries
            .FirstOrDefault(resource => resource.Contains("BgBrush"));

        if (currentTheme is null)
            application.Resources.MergedDictionaries.Add(dictionary);
        else
        {
            int index = application.Resources.MergedDictionaries.IndexOf(currentTheme);
            application.Resources.MergedDictionaries[index] = dictionary;
        }

        CurrentTheme = theme;
    }

    public static string GetNextTheme(string currentTheme) =>
        currentTheme == DarkTheme ? LightTheme : DarkTheme;
}
