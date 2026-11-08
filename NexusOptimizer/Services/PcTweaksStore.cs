using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace NexusOptimizer.Services;

public sealed class PcTweakBackup
{
    public RegistryHive Hive { get; set; }
    public string RegistryPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public RegistryValueKind ValueKind { get; set; }
    public JsonElement? Value { get; set; }
}

public sealed class PcTweaksState
{
    public Dictionary<string, PcTweakBackup> RegistryBackups { get; set; } = new(StringComparer.Ordinal);
    public string? OriginalPowerScheme { get; set; }
}

public sealed class PcTweaksStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string? _filePath;

    public PcTweaksStore(string? filePath = null) => _filePath = filePath;

    private string FilePath => _filePath ?? AppPaths.PcTweaksFile;

    public PcTweaksState Load()
    {
        if (!File.Exists(FilePath))
            return new PcTweaksState();

        try
        {
            PcTweaksState? state = JsonSerializer.Deserialize<PcTweaksState>(File.ReadAllText(FilePath), Options);

            if (state is null)
                throw new JsonException("Файл состояния пуст.");

            state.RegistryBackups ??= new Dictionary<string, PcTweakBackup>(StringComparer.Ordinal);
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Не удалось прочитать резервные копии системных настроек", ex);
            throw new InvalidDataException("Не удалось прочитать резервные копии настроек ПК. Для безопасности изменения не применены.", ex);
        }
    }

    public void Save(PcTweaksState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("Не удалось определить папку резервных копий.");
        Directory.CreateDirectory(directory);

        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
