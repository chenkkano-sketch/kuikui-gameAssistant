using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KuikuiTelemetryService;

internal sealed class TelemetryWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FrameCollector _collector;
    private readonly HardwareCollector _hardware;
    private readonly ILogger<TelemetryWorker> _logger;

    public TelemetryWorker(FrameCollector collector, HardwareCollector hardware, ILogger<TelemetryWorker> logger)
    {
        _collector = collector;
        _hardware = hardware;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _collector.Start();
        _logger.LogInformation("{ServiceName} started.", TelemetryConstants.ServiceDisplayName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = CreatePipe();

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = Task.Run(() => HandleClientAsync(pipe, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch (Exception ex)
            {
                await pipe.DisposeAsync();
                _logger.LogWarning(ex, "Telemetry pipe accept failed.");
                await Task.Delay(500, stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        if (!CanCreateAclPipe())
        {
            return CreateDefaultPipe();
        }

        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        try
        {
            return NamedPipeServerStreamAcl.Create(
                TelemetryConstants.PipeName,
                PipeDirection.InOut,
                16,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                security);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateDefaultPipe();
        }
    }

    private static NamedPipeServerStream CreateDefaultPipe()
    {
        return new NamedPipeServerStream(
            TelemetryConstants.PipeName,
            PipeDirection.InOut,
            16,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static bool CanCreateAclPipe()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem)
        {
            return true;
        }

        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _collector.Dispose();
        _hardware.Dispose();
        return base.StopAsync(cancellationToken);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true)
                {
                    AutoFlush = true
                };

                var line = (await reader.ReadLineAsync(stoppingToken))?.TrimStart('\uFEFF');
                var request = TryParseRequest(line);

                var frameSnapshot = _collector.GetSnapshot(request.TargetProcessId, request.TargetApplication);
                var hardwareSnapshot = _hardware.GetSnapshot();
                var snapshot = MergeSnapshots(frameSnapshot, hardwareSnapshot);
                await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot, JsonOptions));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Telemetry pipe request failed.");
            }
        }
    }

    private static TelemetrySnapshot MergeSnapshots(TelemetrySnapshot frame, HardwareSnapshot hardware)
    {
        return frame with
        {
            CpuLoad = hardware.CpuLoad,
            CpuTemperature = hardware.CpuTemperature,
            CpuTemperatureSource = hardware.CpuTemperatureSource,
            GpuLoad = hardware.GpuLoad,
            GpuLoadSource = hardware.GpuLoadSource,
            GpuTemperature = hardware.GpuTemperature,
            GpuTemperatureSource = hardware.GpuTemperatureSource,
            DiskTemperature = hardware.DiskTemperature,
            DiskTemperatureSource = hardware.DiskTemperatureSource,
            MemoryLoad = hardware.MemoryLoad,
            MemoryUsedGb = hardware.MemoryUsedGb,
            MemoryTotalGb = hardware.MemoryTotalGb,
            TemperatureSensors = hardware.TemperatureSensors,
            Sensors = hardware.Sensors,
            Status = $"{hardware.Status}；{frame.Status}"
        };
    }

    private static TelemetryRequest TryParseRequest(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new TelemetryRequest(null, null);
        }

        try
        {
            return JsonSerializer.Deserialize<TelemetryRequest>(line, JsonOptions) ?? new TelemetryRequest(null, null);
        }
        catch
        {
            return new TelemetryRequest(null, null);
        }
    }
}
