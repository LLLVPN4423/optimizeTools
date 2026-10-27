using OptiForge.Core.Models;

namespace OptiForge.SysMon;

public interface ISystemMonitor : IDisposable
{
    SystemSnapshot GetSnapshot();
}
