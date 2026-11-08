using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NexusOptimizer.Services;

public sealed record DriverUpdate(string Title, string Description);

public sealed class DriverUpdateService
{
    public IReadOnlyList<DriverUpdate> SearchAvailableUpdates()
    {
        Type sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")
            ?? throw new PlatformNotSupportedException("Компонент поиска обновлений Windows недоступен.");

        object? session = null;
        object? searcher = null;
        object? searchResult = null;
        object? updates = null;

        try
        {
            session = Activator.CreateInstance(sessionType)
                ?? throw new InvalidOperationException("Не удалось запустить поиск обновлений Windows.");

            dynamic updateSession = session;
            searcher = updateSession.CreateUpdateSearcher();
            dynamic updateSearcher = searcher;
            updateSearcher.Online = true;
            searchResult = updateSearcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");

            dynamic result = searchResult;
            updates = result.Updates;
            dynamic updateCollection = updates;
            int count = updateCollection.Count;
            var drivers = new List<DriverUpdate>(count);

            for (int index = 0; index < count; index++)
            {
                object? update = null;
                try
                {
                    update = updateCollection.Item(index);
                    dynamic driver = update;
                    string title = (string)driver.Title;
                    string description = (string)driver.Description;
                    drivers.Add(new DriverUpdate(title, description));
                }
                finally
                {
                    ReleaseComObject(update);
                }
            }

            return drivers;
        }
        finally
        {
            ReleaseComObject(updates);
            ReleaseComObject(searchResult);
            ReleaseComObject(searcher);
            ReleaseComObject(session);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
