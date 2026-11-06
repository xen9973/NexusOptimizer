using NexusOptimizer.Services;

namespace NexusOptimizer.Tests;

public class OptimizationCatalogTests
{
    private const string Gpu = OptimizationCatalog.GpuId;
    private const string Fso = OptimizationCatalog.FullscreenId;
    private const string Priority = OptimizationCatalog.PriorityId;
    private const string Power = OptimizationCatalog.PowerId;

    [Fact]
    public void Empty_selection_is_standard() =>
        Assert.Equal(OptimizationCatalog.StandardProfile, OptimizationCatalog.MatchProfile([]));

    [Fact]
    public void Balanced_does_not_depend_on_order()
    {
        Assert.Equal(OptimizationCatalog.BalancedProfile, OptimizationCatalog.MatchProfile([Gpu, Priority]));
        Assert.Equal(OptimizationCatalog.BalancedProfile, OptimizationCatalog.MatchProfile([Priority, Gpu]));
    }

    [Fact]
    public void All_actions_is_performance() =>
        Assert.Equal(OptimizationCatalog.PerformanceProfile, OptimizationCatalog.MatchProfile([Power, Fso, Priority, Gpu]));

    public static TheoryData<string[]> CustomSets => new()
    {
        new[] { Gpu },
        new[] { Gpu, Priority, Power },
        new[] { "unknown" },
        new[] { Gpu, Priority, "unknown" }
    };

    [Theory]
    [MemberData(nameof(CustomSets))]
    public void Other_sets_are_custom(string[] ids) =>
        Assert.Equal(OptimizationCatalog.CustomProfile, OptimizationCatalog.MatchProfile(ids));

    [Fact]
    public void Every_ready_profile_matches_itself()
    {
        foreach (OptimizationProfileDefinition profile in OptimizationCatalog.Profiles.Where(p => p.ActionIds is not null))
            Assert.Equal(profile.Name, OptimizationCatalog.MatchProfile(profile.ActionIds!));
    }

    [Fact]
    public void Profiles_reference_only_existing_actions()
    {
        var known = OptimizationCatalog.Actions.Select(a => a.Id).ToHashSet();

        foreach (OptimizationProfileDefinition profile in OptimizationCatalog.Profiles)
            Assert.All(profile.ActionIds ?? [], id => Assert.Contains(id, known));
    }

    [Fact]
    public void Ids_and_names_are_unique()
    {
        Assert.Equal(OptimizationCatalog.Actions.Count, OptimizationCatalog.Actions.Select(a => a.Id).Distinct().Count());
        Assert.Equal(OptimizationCatalog.Profiles.Count, OptimizationCatalog.Profiles.Select(p => p.Name).Distinct().Count());
    }

    [Fact]
    public void Only_custom_profile_has_no_fixed_action_set()
    {
        OptimizationProfileDefinition custom = Assert.Single(OptimizationCatalog.Profiles, p => p.ActionIds is null);
        Assert.Equal(OptimizationCatalog.CustomProfile, custom.Name);
    }

    [Fact]
    public void Get_and_set_of_each_action_are_consistent()
    {
        foreach (OptimizationActionDefinition action in OptimizationCatalog.Actions)
        {
            var settings = new GameSettings();

            action.Set(settings, true);
            Assert.True(action.Get(settings), action.Id);

            action.Set(settings, false);
            Assert.False(action.Get(settings), action.Id);
        }
    }

    [Fact]
    public void Unknown_profile_name_throws() =>
        Assert.Throws<InvalidOperationException>(() => OptimizationCatalog.GetProfile("нет такого"));
}
