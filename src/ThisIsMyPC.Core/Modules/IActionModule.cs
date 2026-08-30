using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Modules;

/// <summary>
/// A module that executes one-way actions staged through
/// <see cref="Services.IPendingActionsService"/>. The host resolves the module
/// from <see cref="ActionDescriptor.ModuleId"/> and passes each action here;
/// the module owns the meaning of <see cref="ActionDescriptor.ActionId"/>.
/// </summary>
public interface IActionModule : IModule
{
    Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action);
}
