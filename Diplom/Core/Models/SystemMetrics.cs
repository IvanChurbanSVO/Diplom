using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Diplom.Core.Models
{
    public record SystemMetrics(
        double TotalCpuUsage,
        long TotalPhysicalMemory,
        long AvailablePhysicalMemory,
        long TotalPageFile,
        long AvailablePageFile,
        DateTime Timestamp
    );
}