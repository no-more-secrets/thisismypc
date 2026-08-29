using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Service;

// Session 0 SYSTEM service host (28-1). Also runs as a console app for local
// debugging (`ThisIsMyPC.Service.exe` from a terminal) — UseWindowsService is a
// no-op outside the SCM.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "ThisIsMyPC");
builder.Services.AddSingleton<IRegistryService, RegistryService>();
builder.Services.AddSingleton<DriftWatchdog>();
builder.Services.AddSingleton<IDriftReportSource>(sp => sp.GetRequiredService<DriftWatchdog>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DriftWatchdog>());
builder.Services.AddHostedService<PipeServerWorker>();
builder.Build().Run();
