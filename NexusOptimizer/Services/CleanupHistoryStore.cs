using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NexusOptimizer.Services;

/// <summary>Итог одного запуска очистки.</summary>
public sealed class CleanupRecord
{
    public DateTime FinishedUtc { get; set; }
    public long FreedBytes { get; set; }
    public int DeletedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public bool Cancelled { get; set; }
    public List<string> Categories { get; set; } = [];
}

/// <summary>История очисток между запусками: сколько места освобождено и когда.</summary>
public sealed class CleanupHistoryStore
{
    /// <summary>Сколько последних запусков хранить.</summary>
    public const int MaxRecords = 200;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string? _path;

    /// <param name="path">Файл истории; по умолчанию — AppPaths.CleanupHistoryFile (нужен тестам).</param>
    public CleanupHistoryStore(string? path = null) => _path = path;

    private string FilePath => _path ?? AppPaths.CleanupHistoryFile;

    public List<CleanupRecord> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            return JsonSerializer.Deserialize<List<CleanupRecord>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось прочитать историю очисток", ex);
            return [];
        }
    }

    public void Append(CleanupRecord record)
    {
        try
        {
            List<CleanupRecord> records = Load();
            records.Add(record);

            if (records.Count > MaxRecords)
                records.RemoveRange(0, records.Count - MaxRecords);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(records, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось сохранить историю очисток", ex);
        }
    }

    /// <summary>Строка для экрана очистки: итог за всё время и последняя очистка.</summary>
    public static string Summarize(IReadOnlyCollection<CleanupRecord> records)
    {
        if (records.Count == 0)
            return "История очисток пока пуста.";

        long total = records.Sum(record => record.FreedBytes);
        CleanupRecord last = records.MaxBy(record => record.FinishedUtc)!;

        return $"За всё время освобождено {ByteSize.Format(total)} за {records.Count} {Plural(records.Count, "очистку", "очистки", "очисток")}. "
               + $"Последняя: {last.FinishedUtc.ToLocalTime():dd.MM.yyyy HH:mm}, {ByteSize.Format(last.FreedBytes)}.";
    }

    internal static string Plural(int n, string one, string few, string many)
    {
        int mod100 = n % 100;
        int mod10 = n % 10;

        if (mod100 is >= 11 and <= 14)
            return many;

        return mod10 switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }
}
