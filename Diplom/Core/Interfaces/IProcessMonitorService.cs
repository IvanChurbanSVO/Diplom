using Diplom.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Diplom.Core.Models;

namespace Diplom.Core.Interfaces
{
    public interface IProcessMonitorService
    {
        event Action<IEnumerable<ProcessSnapshot>>? ProcessesUpdated;
        Task StartAsync(CancellationToken cancellationToken);
        bool KillProcess(int id);
    }
}
