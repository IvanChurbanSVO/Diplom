using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Diplom.Core.Interfaces;
using Diplom.Core.Models;

namespace Diplom.Services
{
    public class SystemMetricsService : ISystemMetricsService, IDisposable
    {
        private readonly ILogger<SystemMetricsService> _logger;
        private readonly int _refreshRateMs;
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramAvailableCounter;
        private bool _isInitialized = false;

        public event Action<SystemMetrics>? MetricsUpdated;

        public SystemMetricsService(
            ILogger<SystemMetricsService> logger,
            IOptions<AppSettings> settings)
        {
            _logger = logger;
            _refreshRateMs = settings.Value.UiRefreshRateMs;

            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                _logger.LogInformation("Metrics Service started.");
                _cpuCounter.NextValue();
                await Task.Delay(500, cancellationToken);
                _isInitialized = true;

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var metrics = CollectMetrics();
                        MetricsUpdated?.Invoke(metrics);
                        await Task.Delay(_refreshRateMs, cancellationToken);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Metrics error");
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }, cancellationToken);
        }

        private SystemMetrics CollectMetrics()
        {
            float cpuLoad = _cpuCounter.NextValue();
            float availableRamMB = _ramAvailableCounter.NextValue();
            long totalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            long availableRamBytes = (long)(availableRamMB * 1024 * 1024);

            return new SystemMetrics(
                TotalCpuUsage: Math.Round(cpuLoad, 2),
                TotalPhysicalMemory: totalRamBytes,
                AvailablePhysicalMemory: availableRamBytes,
                TotalPageFile: totalRamBytes,
                AvailablePageFile: availableRamBytes,
                Timestamp: DateTime.Now
            );
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _ramAvailableCounter?.Dispose();
        }
    }
}