using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Diplom.Core.Interfaces;
using Diplom.Core.Models;

namespace Diplom.Services
{
    public class ProcessMonitorService : IProcessMonitorService, IDisposable
    {
        private readonly ILogger<ProcessMonitorService> _logger;
        private readonly int _refreshRateMs;
        private readonly Dictionary<int, PerformanceCounter> _cpuCountersCache = new();
        private readonly object _cacheLock = new();

        public event Action<IEnumerable<ProcessSnapshot>>? ProcessesUpdated;

        public ProcessMonitorService(
            ILogger<ProcessMonitorService> logger,
            IOptions<AppSettings> settings)
        {
            _logger = logger;
            _refreshRateMs = settings.Value.UiRefreshRateMs;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                _logger.LogInformation("Process Monitor Service started.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var snapshots = CollectProcessSnapshots();
                        ProcessesUpdated?.Invoke(snapshots);

                        await Task.Delay(_refreshRateMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error collecting process data.");
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }, cancellationToken);
        }

        private IEnumerable<ProcessSnapshot> CollectProcessSnapshots()
        {
            var result = new List<ProcessSnapshot>();
            var activePids = new HashSet<int>();

            try
            {
                var processes = Process.GetProcesses();

                foreach (var proc in processes)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(proc.ProcessName) || proc.Id == 0)
                        {
                            proc.Dispose();
                            continue;
                        }

                        activePids.Add(proc.Id);

                        double cpuUsage = 0;
                        var counter = GetOrCreateCpuCounter(proc);
                        if (counter != null)
                        {
                            cpuUsage = counter.NextValue();
                        }

                        long ioRead = 0;
                        long ioWrite = 0;

                        var snapshot = new ProcessSnapshot();
                        snapshot.Id = proc.Id;
                        snapshot.Name = proc.ProcessName;
                        snapshot.CpuUsage = Math.Round(cpuUsage, 2);
                        snapshot.MemoryPrivateBytes = proc.PrivateMemorySize64;
                        snapshot.IoReadBytes = ioRead;
                        snapshot.IoWriteBytes = ioWrite;
                        snapshot.LastUpdated = DateTime.Now;

                        result.Add(snapshot);
                    }
                    catch (InvalidOperationException)
                    {
                        // Процесс умер
                    }
                    catch (Exception)
                    {
                        // Нет доступа
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                CleanupCache(activePids);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate processes.");
            }

            return result;
        }

        private PerformanceCounter? GetOrCreateCpuCounter(Process proc)
        {
            lock (_cacheLock)
            {
                if (_cpuCountersCache.TryGetValue(proc.Id, out var existing))
                {
                    return existing;
                }

                try
                {
                    var category = new PerformanceCounterCategory("Process");
                    var instances = category.GetInstanceNames();
                    string? foundInstance = null;

                    foreach (var instance in instances)
                    {
                        if (instance.StartsWith(proc.ProcessName))
                        {
                            try
                            {
                                using var tempCounter = new PerformanceCounter("Process", "ID Process", instance, true);
                                int pidCounter = (int)tempCounter.NextValue();

                                if (pidCounter == proc.Id)
                                {
                                    foundInstance = instance;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }

                    if (foundInstance != null)
                    {
                        var counter = new PerformanceCounter("Process", "% Processor Time", foundInstance, readOnly: true);
                        counter.NextValue(); 
                        _cpuCountersCache[proc.Id] = counter;
                        return counter;
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, $"Could not create counter for {proc.ProcessName}");
                    return null;
                }
            }
        }

        private void CleanupCache(HashSet<int> activePids)
        {
            lock (_cacheLock)
            {
                var toRemove = _cpuCountersCache.Keys.Where(k => !activePids.Contains(k)).ToList();
                foreach (var key in toRemove)
                {
                    _cpuCountersCache[key].Dispose();
                    _cpuCountersCache.Remove(key);
                }
            }
        }

        public bool KillProcess(int id)
        {
            try
            {
                var proc = Process.GetProcessById(id);
                proc.Kill();
                proc.WaitForExit(2000);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to kill process {id}");
                return false;
            }
        }

        public void Dispose()
        {
            lock (_cacheLock)
            {
                foreach (var counter in _cpuCountersCache.Values)
                {
                    counter.Dispose();
                }
                _cpuCountersCache.Clear();
            }
        }
    }
}