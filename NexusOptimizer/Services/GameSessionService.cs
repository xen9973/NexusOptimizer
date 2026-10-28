using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NexusOptimizer.Models;

namespace NexusOptimizer.Services;

/// <summary>
/// Следит за процессами запущенной игры: повышает им приоритет и держит схему электропитания.
/// Игру определяет по расположению exe в папке установки, поэтому работает и для Steam, и для прямого запуска.
/// </summary>
public sealed class GameSessionService
{
    public static GameSessionService Current { get; } = new();

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly PowerPlanService _power = new();

    /// <returns>true, если слежение запущено.</returns>
    public bool Start(GameInfo game, GameSettings settings, IProgress<string> report)
    {
        bool applyPriority = settings.AboveNormalPriority;
        bool applyPower = settings.HighPerformancePowerPlan;

        if (!applyPriority && !applyPower)
            return false;

        CancellationTokenSource cts;

        lock (_gate)
        {
            if (_sessions.ContainsKey(game.InstallPath))
            {
                report.Report("Эта игра уже отслеживается: профиль применится к её процессам.");
                return false;
            }

            cts = new CancellationTokenSource();
            _sessions[game.InstallPath] = cts;
        }

        _ = Task.Run(() => RunAsync(game, applyPriority, applyPower, report, cts));
        return true;
    }

    /// <summary>Останавливает слежение и сразу возвращает схему электропитания (закрытие программы).</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            foreach (CancellationTokenSource cts in _sessions.Values)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        _power.ReleaseAll();
    }

    public void RestoreLeftoverPowerPlan() => _power.RestoreLeftover();

    private async Task RunAsync(
        GameInfo game,
        bool applyPriority,
        bool applyPower,
        IProgress<string> report,
        CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;

        string prefix = Path.GetFullPath(game.InstallPath).TrimEnd('\\', '/') + "\\";
        var handled = new HashSet<int>();

        bool seen = false;
        bool powerAcquired = false;
        bool failureReported = false;
        int prioritized = 0;

        DateTime startedAt = DateTime.UtcNow;
        DateTime lastSeen = startedAt;

        report.Report("Ждём запуск процесса игры…");

        try
        {
            while (!token.IsCancellationRequested)
            {
                List<int> processIds = FindProcessIds(prefix);

                if (processIds.Count > 0)
                {
                    lastSeen = DateTime.UtcNow;

                    if (!seen)
                    {
                        seen = true;

                        if (applyPower)
                        {
                            PowerResult power = _power.Acquire();
                            powerAcquired = power.Acquired;
                            report.Report(power.Message);
                        }
                    }

                    if (applyPriority)
                    {
                        int before = prioritized;
                        int failed = 0;

                        foreach (int processId in processIds)
                        {
                            if (!handled.Add(processId))
                                continue;

                            if (TrySetAboveNormal(processId))
                                prioritized++;
                            else
                                failed++;
                        }

                        if (prioritized != before)
                        {
                            report.Report($"Приоритет «Выше обычного» установлен для процессов игры: {prioritized}.");
                        }
                        else if (failed > 0 && prioritized == 0 && !failureReported)
                        {
                            failureReported = true;
                            report.Report("Не удалось изменить приоритет: возможно, игра запущена от имени администратора.");
                        }
                    }
                }
                else if (!seen && DateTime.UtcNow - startedAt > StartTimeout)
                {
                    report.Report("Процесс игры не появился за 2 минуты, поэтому профиль запуска не применён.");
                    return;
                }
                else if (seen && DateTime.UtcNow - lastSeen > ExitGrace)
                {
                    report.Report(powerAcquired
                        ? "Игра закрыта. Схема электропитания возвращена."
                        : "Игра закрыта.");
                    return;
                }

                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Программа закрывается: схему вернёт StopAll.
        }
        catch (Exception ex)
        {
            AppLog.Error($"Слежение за игрой «{game.Name}»", ex);
            report.Report("Не удалось отследить процесс игры. Подробности записаны в журнал.");
        }
        finally
        {
            if (powerAcquired)
                _power.Release();

            lock (_gate)
            {
                _sessions.Remove(game.InstallPath);
            }

            cts.Dispose();
        }
    }

    // ───────────────────────── Процессы ─────────────────────────

    private static List<int> FindProcessIds(string prefix)
    {
        var result = new List<int>();
        int self = Environment.ProcessId;

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    int id = process.Id;

                    if (id == self || id <= 4)
                        continue;

                    string? path = GetImagePath(id);

                    if (path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result.Add(id);
                }
                catch
                {
                    // Процесс мог завершиться, пока мы его проверяли.
                }
            }
        }

        return result;
    }

    private static bool TrySetAboveNormal(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.PriorityClass = ProcessPriorityClass.AboveNormal;
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    private static string? GetImagePath(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;

            return QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString(0, size)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
