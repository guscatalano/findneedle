using System.Threading;
using System.Threading.Tasks;

namespace FindNeedleToolInstallers;

public interface IDependencyInstaller
{
    string DependencyName { get; }
    string Description { get; }
    DependencyStatus GetStatus();
    bool IsInstalled();
    Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);
}
