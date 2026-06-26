using KuikuiTelemetryService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = TelemetryConstants.ServiceDisplayName;
});

builder.Services.AddSingleton<FrameCollector>();
builder.Services.AddSingleton<HardwareCollector>();
builder.Services.AddHostedService<TelemetryWorker>();

await builder.Build().RunAsync();
