using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Win32.Services;
using ThisIsMyPC.Service;

// Session 0 SYSTEM service host (28-1). Also runs as a console app for local
// debugging (`ThisIsMyPC.Service.exe` from a terminal).

#if ACG_ENABLED
if (!ThisIsMyPC.Interop.Win32.Security.DynamicCodeHardening.Apply() ||
    !ThisIsMyPC.Interop.Win32.Security.DynamicCodeHardening.IsEnabled())
{
    Environment.FailFast("Arbitrary Code Guard could not be enabled.");
}
#endif

// First: System32 + application dir only for every DLL resolution; a SYSTEM
// process must never resolve libraries through PATH or the working directory.
if (!ThisIsMyPC.Interop.Win32.Security.DllSearchHardening.Apply())
    Environment.FailFast("Safe DLL search policy could not be enabled.");

var isService = WindowsServiceHelpers.IsWindowsService();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = isService ? AppContext.BaseDirectory : null,
});
if (isService)
    builder.Services.AddSingleton<IHostLifetime, ScmHostLifetime>();
builder.Services.AddSingleton<IRegistryService, RegistryService>();
builder.Services.AddSingleton<NativeRestorationService>();
builder.Services.AddSingleton<IRestorationServiceController>(sp => sp.GetRequiredService<NativeRestorationService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NativeRestorationService>());
builder.Services.AddSingleton<DriftWatchdog>();
builder.Services.AddSingleton<IDriftReportSource>(sp => sp.GetRequiredService<DriftWatchdog>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DriftWatchdog>());
builder.Services.AddHostedService<PipeServerWorker>();
using var host = builder.Build();
if (isService)
{
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    NativeServiceHost.Run("ThisIsMyPC",
        reportRunning => ScmHostLifetime.RunAsync(host, reportRunning).GetAwaiter().GetResult(),
        lifetime.StopApplication);
}
else
{
    host.Run();
}
