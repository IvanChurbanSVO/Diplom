using System.Diagnostics;
using System.Net.NetworkInformation;
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

        // Счетчики CPU и RAM
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramAvailableCounter;

        public event Action<SystemMetrics>? MetricsUpdated;

        public SystemMetricsService(
            ILogger<SystemMetricsService> logger,
            IOptions<AppSettings> settings)
        {
            _logger = logger;
            _refreshRateMs = settings.Value.UiRefreshRateMs;

            // Инициализация счетчиков CPU
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");

            // Инициализация счетчиков RAM
            _ramAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");

            _logger.LogInformation("System Metrics Service initialized.");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                _logger.LogInformation("System Metrics Service started.");

                // Первый замер для прогрева счетчика CPU (иначе будет 0)
                _cpuCounter.NextValue();
                await Task.Delay(500, cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var metrics = CollectMetrics();
                        MetricsUpdated?.Invoke(metrics);

                        await Task.Delay(_refreshRateMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error collecting metrics.");
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }, cancellationToken);
        }

        private SystemMetrics CollectMetrics()
        {
            // 1. CPU
            float cpuLoad = _cpuCounter.NextValue();

            // 2. RAM
            float availableRamMB = _ramAvailableCounter.NextValue();

            // Получаем общий объем ОЗУ через GC 
            long totalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            long availableRamBytes = (long)(availableRamMB * 1024 * 1024);

            // Возвращаем объект ТОЛЬКО с теми полями, которые есть в SystemMetrics.cs
            return new SystemMetrics(
                TotalCpuUsage: Math.Round(cpuLoad, 2),
                TotalPhysicalMemory: totalRamBytes,
                AvailablePhysicalMemory: availableRamBytes,
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