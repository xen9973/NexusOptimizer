using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NexusOptimizer.Models;

namespace NexusOptimizer.Services;

public sealed class LibraryData
{
    public List<string> ExtraFolders { get; set; } = [];
    public List<GameInfo> Games { get; set; } = [];
    public DateTime? LastScanUtc { get; set; }
}

/// <summary>Сохраняет список игр и добавленные папки между запусками.</summary>
public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public LibraryData Load()
    {
        try
        {
            string path = AppPaths.LibraryFile;

            if (!File.Exists(path))
                return new LibraryData();

            return JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(path), Options)
                   ?? new LibraryData();
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось прочитать сохранённую библиотеку", ex);
            return new LibraryData();
        }
    }

    public void Save(LibraryData data)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);

            string path = AppPaths.LibraryFile;
            string temp = path + ".tmp";

            // Пишем во временный файл и подменяем целиком, чтобы не получить обрезанный JSON.
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось сохранить библиотеку", ex);
        }
    }
}
