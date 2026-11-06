using NexusOptimizer.Models;
using NexusOptimizer.Services;

namespace NexusOptimizer.Tests;

public sealed class GameSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexus-tests-" + Guid.NewGuid().ToString("N"));

    private static GameInfo Game() => new() { Name = "Test", InstallPath = @"C:\Games\Test" };

    private GameSettingsStore Store() => new(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private static GameSettings WithArgs(string args) => new() { LaunchArguments = args };

    [Fact]
    public void Missing_file_loads_as_null() => Assert.Null(Store().Load(Game()));

    [Fact]
    public void Roundtrip_keeps_flags_and_registry_backups()
    {
        var settings = WithArgs("-windowed");
        settings.HighPerformanceGpu = true;
        settings.TweakBackups[@"c:\games\test\game.exe"] = new ExeTweakBackup
        {
            Executable = @"C:\Games\Test\game.exe",
            HadGpuValue = true,
            GpuValue = "GpuPreference=1;",
            HadFullscreenToken = true
        };

        Store().Save(Game(), settings);
        GameSettings? loaded = Store().Load(Game());

        Assert.NotNull(loaded);
        Assert.Equal("-windowed", loaded.LaunchArguments);
        Assert.True(loaded.HighPerformanceGpu);

        ExeTweakBackup backup = Assert.Single(loaded.TweakBackups).Value;
        Assert.Equal("GpuPreference=1;", backup.GpuValue);
        Assert.True(backup.HadFullscreenToken);
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        Store().Save(Game(), WithArgs("a"));
        Store().Save(Game(), WithArgs("b"));

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Corrupted_file_falls_back_to_previous_copy()
    {
        Store().Save(Game(), WithArgs("first"));
        Store().Save(Game(), WithArgs("second"));

        File.WriteAllText(Directory.GetFiles(_dir, "*.json").Single(), "{ обрыв записи");

        Assert.Equal("first", Store().Load(Game())?.LaunchArguments);
    }

    [Fact]
    public void Corrupted_file_without_copy_loads_as_null()
    {
        Store().Save(Game(), WithArgs("only"));
        File.WriteAllText(Directory.GetFiles(_dir, "*.json").Single(), "не json");

        Assert.Null(Store().Load(Game()));
    }

    [Fact]
    public void Delete_removes_the_backup_copy_too()
    {
        Store().Save(Game(), WithArgs("one"));
        Store().Save(Game(), WithArgs("two"));

        Store().Delete(Game());

        Assert.Null(Store().Load(Game()));
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
