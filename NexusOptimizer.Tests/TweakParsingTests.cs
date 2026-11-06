using NexusOptimizer.Services;

namespace NexusOptimizer.Tests;

public class TweakParsingTests
{
    private const string Fso = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    [Fact]
    public void Layers_null_or_empty_gives_no_tokens()
    {
        Assert.Empty(GameTweaksService.ParseLayerTokens(null));
        Assert.Empty(GameTweaksService.ParseLayerTokens(""));
    }

    [Fact]
    public void Layers_tilde_is_not_a_token()
    {
        Assert.Empty(GameTweaksService.ParseLayerTokens("~"));
        Assert.Equal(new[] { Fso }, GameTweaksService.ParseLayerTokens("~ " + Fso));
    }

    [Fact]
    public void Layers_split_on_spaces_and_tabs()
    {
        Assert.Equal(new[] { "HIGHDPIAWARE", "RUNASADMIN" }, GameTweaksService.ParseLayerTokens("~  HIGHDPIAWARE\tRUNASADMIN"));
    }

    [Fact]
    public void Gpu_null_gives_no_pairs() => Assert.Empty(GameTweaksService.ParseGpuValue(null));

    [Fact]
    public void Gpu_single_pair()
    {
        var pair = Assert.Single(GameTweaksService.ParseGpuValue("GpuPreference=2;"));
        Assert.Equal(new KeyValuePair<string, string>("GpuPreference", "2"), pair);
    }

    [Fact]
    public void Gpu_keeps_foreign_settings_in_order()
    {
        var pairs = GameTweaksService.ParseGpuValue("GpuPreference=2;SwapEffectUpgradeEnable=1;");

        Assert.Equal(
            new[]
            {
                new KeyValuePair<string, string>("GpuPreference", "2"),
                new KeyValuePair<string, string>("SwapEffectUpgradeEnable", "1")
            },
            pairs);
    }

    [Fact]
    public void Gpu_trims_spaces()
    {
        var pair = Assert.Single(GameTweaksService.ParseGpuValue(" GpuPreference = 2 ;"));
        Assert.Equal(new KeyValuePair<string, string>("GpuPreference", "2"), pair);
    }

    [Fact]
    public void Gpu_part_without_equals_keeps_empty_value()
    {
        var pair = Assert.Single(GameTweaksService.ParseGpuValue("Foo"));
        Assert.Equal(new KeyValuePair<string, string>("Foo", ""), pair);
    }

    [Fact]
    public void Gpu_preference_is_found_case_insensitively()
    {
        Assert.Equal("2", GameTweaksService.GetGpuPreference("gpupreference=2;"));
        Assert.Equal("1", GameTweaksService.GetGpuPreference("Other=5;GpuPreference=1;"));
    }

    [Fact]
    public void Gpu_preference_missing_is_null()
    {
        Assert.Null(GameTweaksService.GetGpuPreference(null));
        Assert.Null(GameTweaksService.GetGpuPreference("Other=5;"));
    }
}
