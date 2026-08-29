using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// SCM service registration (28-2). Distinct from <see cref="IServiceControlService"/>
/// (state/start-type of existing services): this creates and removes the
/// ThisIsMyPC Owner Mode service registration itself.
/// </summary>
public interface IServiceInstaller
{
    /// <summary>Registers a SERVICE_AUTO_START own-process service. Fails with AlreadyExists semantics folded to success.</summary>
    OperationResult<bool> Install(string serviceName, string displayName, string description, string binaryPath);

    OperationResult<bool> Uninstall(string serviceName);

    /// <summary>True when a service with this name is registered.</summary>
    OperationResult<bool> IsInstalled(string serviceName);
}
