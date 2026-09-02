using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ThisIsMyPC.Installer.Services;
using ThisIsMyPC.Installer.ViewModels;
using ThisIsMyPC.Installer.Views;

namespace ThisIsMyPC.Installer;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var package = new EmbeddedPackage();
            var engine = new MsiInstallEngine(package);
            var viewModel = new InstallerViewModel(engine, EmbeddedPackage.LoadLicenseText(), ExistingSettings.Read());
            var window = new MainWindow { DataContext = viewModel };
            viewModel.FolderPicker = new StorageFolderPicker(window);
            viewModel.RequestClose = () => desktop.Shutdown();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
