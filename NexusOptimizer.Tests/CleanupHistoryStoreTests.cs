using NexusOptimizer.Services;

namespace NexusOptimizer.Tests;

public sealed class CleanupHistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexus-tests-" + Guid.NewGuid().ToString("N"));

    private CleanupHistoryStore Store() => new(Path.Combine(_dir, "history.json"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private static CleanupRecord Record(long bytes, int minutesAgo = 0) => new()
    {
        FinishedUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
        FreedBytes = bytes,
        DeletedFiles = 3,
        Categories = ["Временные файлы"]
    };

    [Fact]
    public void Missing_file_gives_empty_history() => Assert.Empty(Store().Load());

    [Fact]
    public void Append_then_load_roundtrip()
    {
        Store().Append(Record(1024));
        Store().Append(Record(2048));

        List<CleanupRecord> records = Store().Load();

        Assert.Equal(new[] { 1024L, 2048L }, records.Select(r => r.FreedBytes));
        Assert.Equal(new[] { "Временные файлы" }, records[0].Categories);
    }

    [Fact]
    public void History_is_capped_to_the_latest_records()
    {
        var store = Store();

        for (int i = 1; i <= CleanupHistoryStore.MaxRecords + 5; i++)
            store.Append(Record(i));

        List<CleanupRecord> records = store.Load();

        Assert.Equal(CleanupHistoryStore.MaxRecords, records.Count);
        Assert.Equal(6, records[0].FreedBytes);
    }

    [Fact]
    public void Corrupted_file_gives_empty_history_and_can_be_overwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "history.json"), "не json");

        Assert.Empty(Store().Load());

        Store().Append(Record(10));
        Assert.Single(Store().Load());
    }

    [Fact]
    public void Summary_for_empty_history()
    {
        Assert.Equal("История очисток пока пуста.", CleanupHistoryStore.Summarize([]));
    }

    [Fact]
    public void Summary_counts_total_and_uses_the_newest_record_as_last()
    {
        string text = CleanupHistoryStore.Summarize([Record(1024, minutesAgo: 60), Record(2048, minutesAgo: 1)]);

        Assert.Contains("за 2 очистки", text);
        Assert.Contains(ByteSize.Format(3072), text);
        Assert.Contains(ByteSize.Format(2048), text);
    }

    [Theory]
    [InlineData(1, "очистку")]
    [InlineData(2, "очистки")]
    [InlineData(4, "очистки")]
    [InlineData(5, "очисток")]
    [InlineData(11, "очисток")]
    [InlineData(12, "очисток")]
    [InlineData(21, "очистку")]
    [InlineData(22, "очистки")]
    [InlineData(100, "очисток")]
    public void Russian_plural(int n, string expected) =>
        Assert.Equal(expected, CleanupHistoryStore.Plural(n, "очистку", "очистки", "очисток"));
}
