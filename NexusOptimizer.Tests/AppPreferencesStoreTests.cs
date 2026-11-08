using NexusOptimizer.Services;

namespace NexusOptimizer.Tests;

public sealed class AppPreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nexus-preferences-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "preferences.json");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Missing_file_uses_standard_profile()
    {
        Assert.Equal(OptimizationCatalog.StandardProfile, new AppPreferencesStore(FilePath).Load().DefaultGameProfile);
    }

    [Theory]
    [InlineData(OptimizationCatalog.StandardProfile)]
    [InlineData(OptimizationCatalog.BalancedProfile)]
    [InlineData(OptimizationCatalog.PerformanceProfile)]
    public void Save_and_load_keep_default_profile(string profile)
    {
        var store = new AppPreferencesStore(FilePath);

        store.Save(new AppPreferences { DefaultGameProfile = profile });

        Assert.Equal(profile, store.Load().DefaultGameProfile);
    }

    [Theory]
    [InlineData(AppThemeService.DarkTheme)]
    [InlineData(AppThemeService.LightTheme)]
    public void Save_theme_preserves_default_game_profile(string theme)
    {
        var store = new AppPreferencesStore(FilePath);
        store.Save(new AppPreferences
        {
            DefaultGameProfile = OptimizationCatalog.PerformanceProfile,
            Theme = AppThemeService.DarkTheme
        });

        store.SaveTheme(theme);

        AppPreferences loaded = store.Load();
        Assert.Equal(OptimizationCatalog.PerformanceProfile, loaded.DefaultGameProfile);
        Assert.Equal(theme, loaded.Theme);
    }

    [Fact]
    public void Legacy_preferences_keep_dark_theme_as_default()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"DefaultGameProfile":"Сбалансированный"}""");

        Assert.Equal(AppThemeService.DarkTheme, new AppPreferencesStore(FilePath).Load().Theme);
    }

    [Fact]
    public void Save_rejects_unknown_theme()
    {
        var store = new AppPreferencesStore(FilePath);

        Assert.Throws<ArgumentException>(() =>
            store.Save(new AppPreferences { Theme = "Custom" }));
    }

    [Fact]
    public void Completing_welcome_preserves_other_preferences()
    {
        var store = new AppPreferencesStore(FilePath);
        store.Save(new AppPreferences
        {
            DefaultGameProfile = OptimizationCatalog.BalancedProfile,
            Theme = AppThemeService.LightTheme
        });

        store.SaveWelcomeCompleted();

        AppPreferences loaded = store.Load();
        Assert.True(loaded.HasCompletedWelcome);
        Assert.Equal(OptimizationCatalog.BalancedProfile, loaded.DefaultGameProfile);
        Assert.Equal(AppThemeService.LightTheme, loaded.Theme);
    }

    [Fact]
    public void Legacy_preferences_show_welcome_once()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"DefaultGameProfile":"Сбалансированный","Theme":"Dark"}""");

        Assert.False(new AppPreferencesStore(FilePath).Load().HasCompletedWelcome);
    }

    [Fact]
    public void Unknown_profile_falls_back_to_standard()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"DefaultGameProfile":"Пользовательский"}""");

        Assert.Equal(OptimizationCatalog.StandardProfile, new AppPreferencesStore(FilePath).Load().DefaultGameProfile);
    }

    [Fact]
    public void Save_rejects_custom_profile()
    {
        var store = new AppPreferencesStore(FilePath);

        Assert.Throws<ArgumentException>(() =>
            store.Save(new AppPreferences { DefaultGameProfile = OptimizationCatalog.CustomProfile }));
    }
}
