using System.Management;
using System.Text;
using KuikuiGameAssistant.Models;
using Microsoft.Win32;

namespace KuikuiGameAssistant.Services;

public sealed class HardwareInfoService
{
    private const ulong FourGiB = 4UL * 1024 * 1024 * 1024;

    public Task<IReadOnlyList<HardwareSection>> GetHardwareSectionsAsync()
    {
        return Task.Run<IReadOnlyList<HardwareSection>>(() =>
        {
            var sections = new List<HardwareSection>
            {
                BuildCpuSection(),
                BuildGpuSection(),
                BuildMotherboardSection(),
                BuildMemorySection(),
                BuildDiskSection(),
                BuildMonitorSection()
            };

            return sections;
        });
    }

    private static HardwareSection BuildCpuSection()
    {
        var item = QueryFirst("Win32_Processor");
        var currentClock = FormatMHz(Value(item, "CurrentClockSpeed"));
        var maxClock = FormatMHz(Value(item, "MaxClockSpeed"));

        return new HardwareSection("处理器", "\uE950", new[]
        {
            Pair("名称", Value(item, "Name")),
            Pair("厂商", Value(item, "Manufacturer")),
            Pair("核心 / 线程", $"{Value(item, "NumberOfCores")} / {Value(item, "NumberOfLogicalProcessors")}"),
            Pair("频率", currentClock == "未知" ? maxClock : $"{currentClock} / 最大 {maxClock}"),
            Pair("缓存", FormatCpuCache(item)),
            Pair("Socket", Value(item, "SocketDesignation"))
        });
    }

    private static HardwareSection BuildGpuSection()
    {
        var registryAdapters = ReadDisplayRegistryAdapters();
        var gpus = Query("Win32_VideoController")
            .Where(IsPhysicalGpu)
            .Select(gpu => BuildGpuInfo(gpu, registryAdapters))
            .OrderBy(GpuSortRank)
            .ThenBy(x => x.Name)
            .ToArray();
        var items = new List<HardwareItem>();

        for (var i = 0; i < gpus.Length; i++)
        {
            var label = gpus[i].Kind;
            if (gpus.Count(x => x.Kind == label) > 1)
            {
                label = $"{label} {i + 1}";
            }

            items.Add(Pair(label, gpus[i].Name));
            items.Add(Pair(gpus[i].IsIntegrated ? $"{label}共享显存" : $"{label}显存", FormatGpuMemory(gpus[i])));
            items.Add(Pair($"{label}驱动", FormatDriver(gpus[i].DriverVersion, gpus[i].DriverDate)));
            items.Add(Pair($"{label}设备", FormatGpuDevice(gpus[i])));
        }

        if (items.Count == 0)
        {
            items.Add(Pair("状态", "未读取到物理显卡信息"));
        }

        return new HardwareSection("显卡", "\uE7F8", items);
    }

    private static GpuInfo BuildGpuInfo(ManagementBaseObject gpu, IReadOnlyList<DisplayRegistryAdapter> registryAdapters)
    {
        var name = Value(gpu, "Name");
        var text = GpuText(gpu);
        var isIntegrated = ContainsAny(text, "intel", "ven_8086", "integrated", "radeon graphics")
                           && !ContainsAny(text, "nvidia", "ven_10de");
        var registry = registryAdapters.FirstOrDefault(adapter => MatchesRegistryAdapter(gpu, adapter));
        var wmiMemory = ParseUInt64(Value(gpu, "AdapterRAM"));
        var dedicatedMemory = registry?.DedicatedMemoryBytes;
        var sharedMemory = ParseMemoryFromName(name) ?? registry?.DisplayMemoryBytes;
        var memorySource = registry?.DedicatedMemoryBytes is not null
            ? "驱动注册表 64-bit"
            : "WMI";

        if (isIntegrated)
        {
            dedicatedMemory = null;
            sharedMemory ??= wmiMemory;
            memorySource = sharedMemory is not null
                ? sharedMemory == ParseMemoryFromName(name) ? "驱动名称" : "驱动/WMI"
                : "未知";
        }
        else if (dedicatedMemory is null && wmiMemory is > 0)
        {
            dedicatedMemory = wmiMemory;
            memorySource = wmiMemory >= FourGiB - 16 * 1024 * 1024
                ? "WMI 32-bit，可能被截断"
                : "WMI";
        }

        return new GpuInfo(
            Name: name,
            Kind: ClassifyGpu(gpu),
            Vendor: Value(gpu, "AdapterCompatibility"),
            PnpDeviceId: Value(gpu, "PNPDeviceID"),
            DriverVersion: Value(gpu, "DriverVersion"),
            DriverDate: Value(gpu, "DriverDate"),
            VideoProcessor: Value(gpu, "VideoProcessor"),
            DedicatedMemoryBytes: dedicatedMemory,
            SharedMemoryBytes: sharedMemory,
            MemorySource: memorySource,
            IsIntegrated: isIntegrated);
    }

    private static string FormatGpuMemory(GpuInfo gpu)
    {
        var bytes = gpu.IsIntegrated ? gpu.SharedMemoryBytes : gpu.DedicatedMemoryBytes;
        if (bytes is null or 0)
        {
            return "未知";
        }

        return $"{FormatBytes(bytes.Value)}  ({gpu.MemorySource})";
    }

    private static string FormatGpuDevice(GpuInfo gpu)
    {
        var id = ExtractHardwareId(gpu.PnpDeviceId);
        var parts = new[] { gpu.Vendor, id, gpu.VideoProcessor }
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("未知", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join("  ", parts);
    }

    private static string FormatDriver(string version, string date)
    {
        var driverDate = FormatWmiDate(date);
        if (driverDate == "未知")
        {
            return version;
        }

        return $"{version}  {driverDate}";
    }

    private static bool IsPhysicalGpu(ManagementBaseObject gpu)
    {
        var pnp = Value(gpu, "PNPDeviceID");
        if (!pnp.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = GpuText(gpu);
        if (ContainsAny(text, "virtual", "indirect", "remote", "mirror", "basic display", "mumu", "android", "oray", "idd"))
        {
            return false;
        }

        return ContainsAny(text, "nvidia", "intel", "amd", "advanced micro devices", "radeon", "ven_10de", "ven_8086", "ven_1002");
    }

    private static int GpuSortRank(GpuInfo gpu)
    {
        var text = $"{gpu.Name} {gpu.Vendor} {gpu.PnpDeviceId}";
        if (ContainsAny(text, "nvidia", "ven_10de"))
        {
            return 0;
        }

        if (ContainsAny(text, "amd", "advanced micro devices", "radeon", "ven_1002")
            && !ContainsAny(text, "integrated", "radeon graphics"))
        {
            return 1;
        }

        if (gpu.IsIntegrated)
        {
            return 2;
        }

        return 3;
    }

    private static string ClassifyGpu(ManagementBaseObject gpu)
    {
        var text = GpuText(gpu);
        if (ContainsAny(text, "intel", "ven_8086", "integrated", "radeon graphics")
            && !ContainsAny(text, "nvidia", "ven_10de"))
        {
            return "核显";
        }

        if (ContainsAny(text, "nvidia", "ven_10de", "amd", "advanced micro devices", "radeon", "ven_1002"))
        {
            return "独显";
        }

        return "显卡";
    }

    private static string GpuText(ManagementBaseObject gpu)
    {
        return string.Join(' ',
            Value(gpu, "Name"),
            Value(gpu, "AdapterCompatibility"),
            Value(gpu, "VideoProcessor"),
            Value(gpu, "PNPDeviceID"));
    }

    private static HardwareSection BuildMotherboardSection()
    {
        var item = QueryFirst("Win32_BaseBoard");
        var bios = QueryFirst("Win32_BIOS");

        return new HardwareSection("主板", "\uE964", new[]
        {
            Pair("厂商", Value(item, "Manufacturer")),
            Pair("型号", Value(item, "Product")),
            Pair("版本", Value(item, "Version")),
            Pair("BIOS", $"{Value(bios, "SMBIOSBIOSVersion")}  {FormatWmiDate(Value(bios, "ReleaseDate"))}"),
            Pair("序列号", MaskSerial(Value(item, "SerialNumber")))
        });
    }

    private static HardwareSection BuildMemorySection()
    {
        var modules = Query("Win32_PhysicalMemory").ToArray();
        var items = new List<HardwareItem>();
        ulong total = 0;

        for (var i = 0; i < modules.Length; i++)
        {
            var capacityText = Value(modules[i], "Capacity");
            if (ulong.TryParse(capacityText, out var capacity))
            {
                total += capacity;
            }

            var label = modules.Length > 1 ? $"内存 {i + 1}" : "内存";
            items.Add(Pair(label, FormatMemoryModule(modules[i], capacityText)));
        }

        items.Insert(0, Pair("总容量", total == 0 ? "未知" : FormatBytes(total)));

        return new HardwareSection("内存", "\uE950", items);
    }

    private static string FormatMemoryModule(ManagementBaseObject module, string capacityText)
    {
        var configuredSpeed = Value(module, "ConfiguredClockSpeed");
        var speed = Value(module, "Speed");
        var speedText = configuredSpeed != "未知" && configuredSpeed != "0"
            ? $"{configuredSpeed} MT/s"
            : speed != "未知" && speed != "0"
                ? $"{speed} MT/s"
                : "未知频率";
        var part = Value(module, "PartNumber");
        var manufacturer = Value(module, "Manufacturer");
        var locator = Value(module, "DeviceLocator");
        return $"{FormatBytes(capacityText)}  {speedText}  {manufacturer}  {part}  {locator}";
    }

    private static HardwareSection BuildDiskSection()
    {
        var storageDisks = Query(@"root\Microsoft\Windows\Storage", "MSFT_PhysicalDisk").ToArray();
        if (storageDisks.Length > 0)
        {
            var storageItems = storageDisks
                .OrderByDescending(x => ParseUInt64(Value(x, "Size")) ?? 0)
                .Select((disk, index) => Pair(
                    storageDisks.Length > 1 ? $"硬盘 {index + 1}" : "硬盘",
                    $"{Value(disk, "FriendlyName")}  {FormatBytes(Value(disk, "Size"))}  {FormatBusType(Value(disk, "BusType"))}  {FormatMediaType(Value(disk, "MediaType"))}  {FormatHealth(Value(disk, "HealthStatus"))}"))
                .ToList();
            return new HardwareSection("硬盘", "\uEDA2", storageItems);
        }

        var disks = Query("Win32_DiskDrive").ToArray();
        var items = new List<HardwareItem>();

        for (var i = 0; i < disks.Length; i++)
        {
            var label = disks.Length > 1 ? $"硬盘 {i + 1}" : "硬盘";
            items.Add(Pair(label, $"{Value(disks[i], "Model")}  {FormatBytes(Value(disks[i], "Size"))}  {Value(disks[i], "InterfaceType")}"));
        }

        if (items.Count == 0)
        {
            items.Add(Pair("状态", "未读取到硬盘信息"));
        }

        return new HardwareSection("硬盘", "\uEDA2", items);
    }

    private static HardwareSection BuildMonitorSection()
    {
        var items = new List<HardwareItem>();

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorID");
            var monitors = searcher.Get().Cast<ManagementObject>().ToArray();
            for (var i = 0; i < monitors.Length; i++)
            {
                var name = DecodeMonitorString(monitors[i]["UserFriendlyName"]);
                var serial = DecodeMonitorString(monitors[i]["SerialNumberID"]);
                items.Add(Pair(monitors.Length > 1 ? $"显示器 {i + 1}" : "显示器", string.IsNullOrWhiteSpace(serial) ? name : $"{name}  SN {MaskSerial(serial)}"));
            }
        }
        catch
        {
            foreach (var monitor in Query("Win32_DesktopMonitor"))
            {
                items.Add(Pair("显示器", Value(monitor, "Name")));
            }
        }

        if (items.Count == 0)
        {
            items.Add(Pair("状态", "未读取到显示器信息"));
        }

        return new HardwareSection("显示器", "\uE7F4", items);
    }

    private static IReadOnlyList<DisplayRegistryAdapter> ReadDisplayRegistryAdapters()
    {
        var adapters = new List<DisplayRegistryAdapter>();
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (videoKey is null)
            {
                return adapters;
            }

            foreach (var adapterKeyName in videoKey.GetSubKeyNames())
            {
                using var adapterKey = videoKey.OpenSubKey(adapterKeyName);
                if (adapterKey is null)
                {
                    continue;
                }

                foreach (var childKeyName in adapterKey.GetSubKeyNames())
                {
                    using var childKey = adapterKey.OpenSubKey(childKeyName);
                    if (childKey is null)
                    {
                        continue;
                    }

                    var adapterName = RegistryString(childKey.GetValue("HardwareInformation.AdapterString"))
                                      ?? RegistryString(childKey.GetValue("DriverDesc"));
                    var matchingDeviceId = RegistryString(childKey.GetValue("MatchingDeviceId"));
                    var dedicatedMemory = ReadRegistryMemory(childKey.GetValue("HardwareInformation.qwMemorySize"));
                    var displayMemory = ReadRegistryMemory(childKey.GetValue("HardwareInformation.MemorySize"));
                    if (string.IsNullOrWhiteSpace(adapterName)
                        && string.IsNullOrWhiteSpace(matchingDeviceId)
                        && dedicatedMemory is null
                        && displayMemory is null)
                    {
                        continue;
                    }

                    adapters.Add(new DisplayRegistryAdapter(
                        adapterName ?? string.Empty,
                        matchingDeviceId ?? string.Empty,
                        dedicatedMemory,
                        displayMemory));
                }
            }
        }
        catch
        {
        }

        return adapters
            .GroupBy(x => $"{x.AdapterName}|{x.MatchingDeviceId}|{x.DedicatedMemoryBytes}|{x.DisplayMemoryBytes}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private static bool MatchesRegistryAdapter(ManagementBaseObject gpu, DisplayRegistryAdapter adapter)
    {
        var pnp = NormalizeHardwareId(Value(gpu, "PNPDeviceID"));
        var matchingDeviceId = NormalizeHardwareId(adapter.MatchingDeviceId);
        if (!string.IsNullOrWhiteSpace(pnp)
            && !string.IsNullOrWhiteSpace(matchingDeviceId)
            && pnp.Contains(matchingDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = Value(gpu, "Name");
        return !string.IsNullOrWhiteSpace(adapter.AdapterName)
               && name.Equals(adapter.AdapterName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHardwareId(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value == "未知"
            ? string.Empty
            : value.Replace(@"\\", @"\", StringComparison.Ordinal).Trim().ToLowerInvariant();
    }

    private static ulong? ReadRegistryMemory(object? value)
    {
        return value switch
        {
            null => null,
            ulong ulongValue when ulongValue > 0 => ulongValue,
            long longValue when longValue > 0 => (ulong)longValue,
            uint uintValue when uintValue > 0 => uintValue,
            int intValue when intValue > 0 => (uint)intValue,
            byte[] bytes when bytes.Length >= 8 => BitConverter.ToUInt64(bytes, 0),
            byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0),
            string text when ulong.TryParse(text, out var parsed) && parsed > 0 => parsed,
            _ => null
        };
    }

    private static string? RegistryString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            string[] values => string.Join(", ", values.Where(x => !string.IsNullOrWhiteSpace(x))),
            byte[] bytes => Encoding.Unicode.GetString(bytes).TrimEnd('\0'),
            _ => value.ToString()
        };
    }

    private static IReadOnlyList<ManagementObject> Query(string className) => Query(null, className);

    private static IReadOnlyList<ManagementObject> Query(string? scope, string className)
    {
        try
        {
            using var searcher = string.IsNullOrWhiteSpace(scope)
                ? new ManagementObjectSearcher($"SELECT * FROM {className}")
                : new ManagementObjectSearcher(scope, $"SELECT * FROM {className}");
            return searcher.Get().Cast<ManagementObject>().ToArray();
        }
        catch
        {
            return Array.Empty<ManagementObject>();
        }
    }

    private static ManagementObject? QueryFirst(string className) => Query(className).FirstOrDefault();

    private static HardwareItem Pair(string name, string value) => new(name, string.IsNullOrWhiteSpace(value) ? "未知" : value.Trim());

    private static string Value(ManagementBaseObject? item, string property)
    {
        if (item is null)
        {
            return "未知";
        }

        try
        {
            return item[property]?.ToString() ?? "未知";
        }
        catch
        {
            return "未知";
        }
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static ulong? ParseUInt64(string value)
    {
        return ulong.TryParse(value, out var result) && result > 0 ? result : null;
    }

    private static ulong? ParseMemoryFromName(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"\((\d+(?:\.\d+)?)\s*GB\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, out var gb) || gb <= 0)
        {
            return null;
        }

        return (ulong)Math.Round(gb * 1024d * 1024d * 1024d);
    }

    private static string FormatMHz(string value)
    {
        return double.TryParse(value, out var mhz) ? $"{mhz:0} MHz" : "未知";
    }

    private static string FormatBytes(string value)
    {
        return ulong.TryParse(value, out var bytes) ? FormatBytes(bytes) : "未知";
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes == 0)
        {
            return "未知";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    private static string FormatCpuCache(ManagementBaseObject? item)
    {
        var l2 = Value(item, "L2CacheSize");
        var l3 = Value(item, "L3CacheSize");
        return $"L2 {FormatKb(l2)} / L3 {FormatKb(l3)}";
    }

    private static string FormatKb(string value)
    {
        return ulong.TryParse(value, out var kb) && kb > 0 ? FormatBytes(kb * 1024) : "未知";
    }

    private static string FormatWmiDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "未知")
        {
            return "未知";
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value).ToString("yyyy-MM-dd");
        }
        catch
        {
            return "未知";
        }
    }

    private static string FormatBusType(string value)
    {
        return value switch
        {
            "3" => "SCSI",
            "7" => "USB",
            "11" => "SATA",
            "16" => "PCIe",
            "17" => "NVMe",
            _ => value == "未知" ? "未知总线" : $"Bus {value}"
        };
    }

    private static string FormatMediaType(string value)
    {
        return value switch
        {
            "3" => "HDD",
            "4" => "SSD",
            "5" => "SCM",
            _ => "未知介质"
        };
    }

    private static string FormatHealth(string value)
    {
        return value switch
        {
            "0" => "健康",
            "1" => "警告",
            "2" => "异常",
            "5" => "未知健康状态",
            _ => string.IsNullOrWhiteSpace(value) || value == "未知" ? "未知健康状态" : $"健康状态 {value}"
        };
    }

    private static string ExtractHardwareId(string pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId) || pnpDeviceId == "未知")
        {
            return string.Empty;
        }

        var parts = pnpDeviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? pnpDeviceId : parts[0] == "PCI" && parts.Length > 1 ? parts[1] : parts[0];
    }

    private static string MaskSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "未知";
        }

        return serial.Length <= 4 ? serial : $"{serial[..2]}****{serial[^2..]}";
    }

    private static string DecodeMonitorString(object? value)
    {
        if (value is not ushort[] values)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in values)
        {
            if (item == 0)
            {
                break;
            }

            builder.Append((char)item);
        }

        return builder.ToString();
    }

    private sealed record GpuInfo(
        string Name,
        string Kind,
        string Vendor,
        string PnpDeviceId,
        string DriverVersion,
        string DriverDate,
        string VideoProcessor,
        ulong? DedicatedMemoryBytes,
        ulong? SharedMemoryBytes,
        string MemorySource,
        bool IsIntegrated);

    private sealed record DisplayRegistryAdapter(
        string AdapterName,
        string MatchingDeviceId,
        ulong? DedicatedMemoryBytes,
        ulong? DisplayMemoryBytes);
}
