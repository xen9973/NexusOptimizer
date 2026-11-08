using System.Text.Json;
using Microsoft.Win32;
using NexusOptimizer.Services;

namespace NexusOptimizer.Tests;

public sealed class PcTweaksTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nexus-pc-tweaks-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "pc-tweaks.json");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Missing_state_has_no_backups()
    {
        PcTweaksState state = new PcTweaksStore(FilePath).Load();

        Assert.Empty(state.RegistryBackups);
        Assert.Null(state.OriginalPowerScheme);
    }

    [Fact]
    public void Store_roundtrip_preserves_registry_value_type_and_data()
    {
        var state = new PcTweaksState
        {
            OriginalPowerScheme = "7e4f2a13-3b22-4abc-91d1-1f30f03a7d61"
        };
        state.RegistryBackups["menu-delay"] = new PcTweakBackup
        {
            Hive = RegistryHive.CurrentUser,
            RegistryPath = @"Control Panel\Desktop",
            ValueName = "MenuShowDelay",
            Existed = true,
            ValueKind = RegistryValueKind.String,
            Value = JsonSerializer.SerializeToElement("400")
        };
        var store = new PcTweaksStore(FilePath);

        store.Save(state);

        PcTweaksState loaded = store.Load();
        PcTweakBackup backup = Assert.Single(loaded.RegistryBackups).Value;
        Assert.Equal(RegistryHive.CurrentUser, backup.Hive);
        Assert.Equal(RegistryValueKind.String, backup.ValueKind);
        Assert.Equal("400", backup.Value?.GetString());
        Assert.Equal(state.OriginalPowerScheme, loaded.OriginalPowerScheme);
    }

    [Fact]
    public void Catalog_has_unique_ids_and_presets_reference_known_tweaks()
    {
        var ids = PcTweaksCatalog.Tweaks.Select(tweak => tweak.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(PcTweaksCatalog.Tweaks.Count, ids.Count);

        foreach (string preset in PcTweaksCatalog.Presets)
            Assert.All(PcTweaksCatalog.GetPreset(preset), id => Assert.Contains(id, ids));
    }

    [Fact]
    public void Catalog_covers_all_requested_categories_and_does_not_disable_security()
    {
        Assert.Equal(
            Enum.GetValues<PcTweakCategory>().ToHashSet(),
            PcTweaksCatalog.Tweaks.Select(tweak => tweak.Category).ToHashSet());
        Assert.DoesNotContain(PcTweaksCatalog.Tweaks, tweak =>
            tweak.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase)
            || tweak.Name.Contains("Firewall", StringComparison.OrdinalIgnoreCase)
            || tweak.Name.Contains("обновлен", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_registry_tweak_has_a_supported_value_definition()
    {
        Assert.All(
            PcTweaksCatalog.Tweaks.Where(tweak => tweak.RegistryPath is not null),
            tweak =>
            {
                Assert.False(string.IsNullOrWhiteSpace(tweak.ValueName), tweak.Id);
                Assert.True(tweak.ValueKind is RegistryValueKind.DWord or RegistryValueKind.String, tweak.Id);
                Assert.NotNull(tweak.EnabledValue);
            });
    }

    [Fact]
    public void Additional_tweaks_are_in_their_expected_categories()
    {
        Assert.Equal(6, PcTweaksCatalog.Tweaks.Count(tweak => tweak.Category == PcTweakCategory.Performance));
        Assert.Equal(8, PcTweaksCatalog.Tweaks.Count(tweak => tweak.Category == PcTweakCategory.Gaming));
        Assert.Equal(4, PcTweaksCatalog.Tweaks.Count(tweak => tweak.Category == PcTweakCategory.Privacy));
        Assert.Equal(2, PcTweaksCatalog.Tweaks.Count(tweak => tweak.Category == PcTweakCategory.Startup));
        Assert.Equal(2, PcTweaksCatalog.Tweaks.Count(tweak => tweak.Category == PcTweakCategory.Power));
        Assert.Equal(2, PcTweaksCatalog.Tweaks.Count(tweak => tweak.Category == PcTweakCategory.Explorer));
    }

    [Fact]
    public void Machine_wide_registry_tweaks_are_explicitly_scoped()
    {
        PcTweakDefinition[] systemTweaks = PcTweaksCatalog.Tweaks
            .Where(tweak => tweak.Scope == PcTweakScope.WholeComputer && tweak.RegistryPath is not null)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "disable-power-throttling",
                "game-capture-all-users",
                "search-manual",
                "sysmain-manual",
                "xbl-auth-manager",
                "xbl-game-save",
                "xbox-accessory",
                "xbox-networking"
            },
            systemTweaks.Select(tweak => tweak.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Contains(systemTweaks, tweak =>
            tweak.Id == "game-capture-all-users"
            && tweak.RegistryPath == @"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
    }

    [Fact]
    public void Optional_service_recommendations_only_change_startup_to_manual_and_are_not_in_presets()
    {
        PcTweakDefinition[] serviceTweaks = PcTweaksCatalog.Tweaks
            .Where(tweak => tweak.ServiceName is not null)
            .ToArray();

        Assert.Equal(6, serviceTweaks.Length);
        Assert.All(serviceTweaks, tweak =>
        {
            Assert.Equal(PcTweakScope.WholeComputer, tweak.Scope);
            Assert.Equal("Start", tweak.ValueName);
            Assert.Equal(RegistryValueKind.DWord, tweak.ValueKind);
            Assert.Equal(3, tweak.EnabledValue);
            Assert.DoesNotContain(PcTweaksCatalog.Presets, preset =>
                PcTweaksCatalog.GetPreset(preset).Contains(tweak.Id, StringComparer.Ordinal));
        });
    }

    [Fact]
    public void System_scan_recommends_only_unapplied_user_settings_and_adapts_to_low_memory()
    {
        IReadOnlyList<PcSystemRecommendation> recommendations = PcSystemScanService.BuildRecommendations(
            8UL * 1024 * 1024 * 1024,
            new HashSet<string>(StringComparer.Ordinal) { "game-mode" },
            new HashSet<string>(StringComparer.Ordinal) { "game-capture" },
            new HashSet<string>(StringComparer.Ordinal));

        Assert.DoesNotContain(recommendations, recommendation => recommendation.Tweak.Id == "game-mode");
        Assert.DoesNotContain(recommendations, recommendation => recommendation.Tweak.Id == "game-capture");
        Assert.Contains(recommendations, recommendation => recommendation.Tweak.Id == "window-animations");
        Assert.Contains(recommendations, recommendation => recommendation.Tweak.Id == "transparency-effects");
        Assert.All(recommendations, recommendation =>
        {
            Assert.Equal(PcTweakScope.CurrentUser, recommendation.Tweak.Scope);
            Assert.Null(recommendation.Tweak.ServiceName);
        });
    }

    [Fact]
    public void System_scan_does_not_recommend_visual_tweaks_for_high_memory_or_unreadable_settings()
    {
        IReadOnlyList<PcSystemRecommendation> recommendations = PcSystemScanService.BuildRecommendations(
            16UL * 1024 * 1024 * 1024,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "game-mode" });

        Assert.DoesNotContain(recommendations, recommendation =>
            recommendation.Tweak.Id is "window-animations" or "transparency-effects" or "game-mode");
        Assert.Contains(recommendations, recommendation => recommendation.Tweak.Id == "game-capture");
    }
}
