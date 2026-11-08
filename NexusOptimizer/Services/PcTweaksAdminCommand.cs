using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NexusOptimizer.Services;

public static class PcTweaksAdminCommand
{
    public static void Execute(string operation, Guid requestId, string selectedIds)
    {
        string resultPath = AppPaths.GetPcTweaksResultFile(requestId);
        IReadOnlyList<PcTweakResult> results;

        try
        {
            HashSet<string> selected = selectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> known = PcTweaksCatalog.Tweaks.Select(tweak => tweak.Id).ToHashSet(StringComparer.Ordinal);

            if ((operation != "apply" && operation != "restore") || !selected.IsSubsetOf(known))
            {
                results = [new PcTweakResult("admin", false, "Недопустимая команда системных настроек.")];
            }
            else
            {
                var service = new PcTweaksService();
                PcTweaksState state = service.LoadState();
                service.ReadEnabled(
                    state,
                    out IReadOnlyList<PcTweakResult> readErrors,
                    out IReadOnlySet<string> unavailableIds);
                var skipIds = readErrors.Select(error => error.Id)
                    .Concat(unavailableIds)
                    .ToHashSet(StringComparer.Ordinal);

                if (operation == "restore")
                {
                    var noneSelected = new HashSet<string>(StringComparer.Ordinal);
                    results =
                    [
                        .. service.ApplyUserTweaks(state, noneSelected, skipIds, forceRestore: true),
                        .. service.ApplyMachineTweaks(state, noneSelected, skipIds, forceRestore: true),
                        .. service.ApplyPowerPlan(state, selected: false)
                    ];
                }
                else
                {
                    results =
                    [
                        .. service.ApplyUserTweaks(state, selected, skipIds),
                        .. service.ApplyMachineTweaks(state, selected, skipIds),
                        .. service.ApplyPowerPlan(state, selected.Contains("power-plan"))
                    ];
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Ошибка команды системных твиков", ex);
            results = [new PcTweakResult("admin", false, ex.Message)];
        }

        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(results));
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось передать результат системных твиков", ex);
        }
    }
}
