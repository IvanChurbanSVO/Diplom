using System.Net.NetworkInformation;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Diplom.Core.Interfaces;

namespace Diplom.Services
{
    public record NetworkConnectionInfo(
        string LocalAddress,
        string RemoteAddress,
        string State,
        string ProcessName,
        int ProcessId
    );

    public interface INetworkMonitorService
    {
        event Action<List<NetworkConnectionInfo>>? ConnectionsUpdated;
        Task StartAsync(CancellationToken cancellationToken);
    }

    public class NetworkMonitorService : INetworkMonitorService
    {
        private readonly ILogger<NetworkMonitorService> _logger;
        public event Action<List<NetworkConnectionInfo>>? ConnectionsUpdated;

        public NetworkMonitorService(ILogger<NetworkMonitorService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                _logger.LogInformation("Network Monitor started.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var connections = GetActiveConnections();
                        ConnectionsUpdated?.Invoke(connections);

                        await Task.Delay(2000, cancellationToken);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting network info.");
                        await Task.Delay(5000, cancellationToken);
                    }
                }
            }, cancellationToken);
        }

        private List<NetworkConnectionInfo> GetActiveConnections()
        {
            var list = new List<NetworkConnectionInfo>();
            try
            {
                var ipProps = IPGlobalProperties.GetIPGlobalProperties();
                var tcpConns = ipProps.GetActiveTcpConnections();

                foreach (var conn in tcpConns)
                {
                    string processName = "Unknown";
                    int processId = -1;

                    var prop = conn.GetType().GetProperty("OwningProcess");
                    if (prop != null)
                    {
                        try
                        {
                            var val = prop.GetValue(conn);
                            if (val is int id)
                            {
                                processId = id;
                                try
                                {
                                    using var proc = Process.GetProcessById(id);
                                    processName = proc.ProcessName;
                                }
                                catch { processName = "Unknown"; }
                            }
                        }
                        catch { }
                    }

                    if (processId == -1)
                    {
                        processName = "N/A";
                    }

                    list.Add(new NetworkConnectionInfo(
                        LocalAddress: $"{conn.LocalEndPoint.Address}:{conn.LocalEndPoint.Port}",
                        RemoteAddress: $"{conn.RemoteEndPoint.Address}:{conn.RemoteEndPoint.Port}",
                        State: conn.State.ToString(),
                        ProcessName: processName,
                        ProcessId: processId
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get TCP connections.");
            }
            return list;
        }
    }
}