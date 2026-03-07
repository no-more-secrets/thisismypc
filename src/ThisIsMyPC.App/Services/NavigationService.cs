using System.ComponentModel;
using System.Runtime.CompilerServices;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.App.Services;

public sealed class NavigationService : INotifyPropertyChanged
{
    private readonly IReadOnlyList<IModule> _injectedModules;
    private readonly List<ModuleRegistration> _modules = [];
    private ModuleRegistration? _currentModule;

    public NavigationService(IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        _injectedModules = modules.ToList();
    }

    public IReadOnlyList<ModuleRegistration> Modules => _modules;

    public ModuleRegistration? CurrentModule
    {
        get => _currentModule;
        private set
        {
            if (_currentModule == value)
                return;

            _currentModule = value;
            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync()
    {
        _modules.Clear();

        foreach (var module in _injectedModules)
        {
            var availability = await module.CheckAvailabilityAsync().ConfigureAwait(false);
            _modules.Add(new ModuleRegistration(module, availability));
        }

        _modules.Sort((a, b) =>
        {
            var groupCompare = a.Module.Info.Group.CompareTo(b.Module.Info.Group);
            return groupCompare != 0 ? groupCompare : a.Module.Info.LoadOrder.CompareTo(b.Module.Info.LoadOrder);
        });
    }

    public void NavigateToModule(string moduleName)
    {
        var registration = _modules.Find(m =>
            m.Module.Info.Name == moduleName && m.Availability.IsAvailable);

        if (registration is not null)
        {
            CurrentModule = registration;
        }
    }

    public void NavigateToFirstAvailable()
    {
        var first = _modules.Find(m => m.Availability.IsAvailable);
        if (first is not null)
        {
            CurrentModule = first;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public record ModuleRegistration(IModule Module, ModuleAvailability Availability);
