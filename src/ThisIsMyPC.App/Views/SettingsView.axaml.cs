using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ThisIsMyPC.App.ViewModels;

namespace ThisIsMyPC.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    // File dialogs need the TopLevel, so they live in code-behind; all logic stays in
    // the VM, which works on JSON strings.
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
                return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export ThisIsMyPC settings",
                SuggestedFileName = vm.DefaultExportFileName,
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (file is null)
                return;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(vm.BuildExportJson());
            vm.ReportExport(file.Name);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Settings export failed");
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
                return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import ThisIsMyPC settings",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count != 1)
                return;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            vm.LoadImportPreview(await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Settings import failed");
        }
    }
}
