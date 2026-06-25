using System.Management;
using System.Text;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.Services;

public sealed class HardwareInfoService
{
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
        return new HardwareSection("处理器", "\uE950", new[]
        {
            Pair("名称", Value(item, "Name")),
            Pair("厂商", Value(item, "Manufacturer")),
            Pair("核心 / 线程", $"{Value(item, "NumberOfCores")} / {Value(item, "NumberOfLogicalProcessors")}"),
            Pair("最大频率", FormatMHz(Value(item, "MaxClockSpeed"))),
            Pair("Socket", Value(item, "SocketDesignation"))
        });
    }

    private static HardwareSection BuildGpuSection()
    {
        var gpus = Query("Win32_VideoController")
            .Where(IsPhysicalGpu)
            .OrderBy(GpuSortRank)
            .ToArray();
        var items = new List<HardwareItem>();

        for (var i = 0; i < gpus.Length; i++)
        {
            var label = ClassifyGpu(gpus[i]);
            if (gpus.Count(x => ClassifyGpu(x) == label) > 1)
            {
                label = $"{label} {i + 1}";
            }

            items.Add(Pair(label, Value(gpus[i], "Name")));
            items.Add(Pair($"{label}显存", FormatBytes(Value(gpus[i], "AdapterRAM"))));
            items.Add(Pair($"{label}驱动", Value(gpus[i], "DriverVersion")));
        }

        if (items.Count == 0)
        {
            items.Add(Pair("状态", "未读取到物理显卡信息"));
        }

        return new HardwareSection("显卡", "\uE7F8", items);
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

    private static int GpuSortRank(ManagementBaseObject gpu)
    {
        var text = GpuText(gpu);
        if (ContainsAny(text, "nvidia", "ven_10de"))
        {
            return 0;
        }

        if (ContainsAny(text, "amd", "advanced micro devices", "radeon", "ven_1002")
            && !ContainsAny(text, "integrated", "radeon graphics"))
        {
            return 1;
        }

        if (ContainsAny(text, "intel", "ven_8086", "integrated", "radeon graphics"))
        {
            return 2;
        }

        return 3;
    }

    private static string ClassifyGpu(ManagementBaseObject gpu)
    {
        var text = GpuText(gpu);
        if (ContainsAny(text, "intel", "ven_8086", "integrated", "radeon graphics"))
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

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
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
            Pair("BIOS", Value(bios, "SMBIOSBIOSVersion")),
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
            items.Add(Pair(label, $"{FormatBytes(capacityText)}  {Value(modules[i], "Speed")} MHz  {Value(modules[i], "Manufacturer")}"));
        }

        items.Insert(0, Pair("总容量", total == 0 ? "未知" : FormatBytes(total.ToString())));

        return new HardwareSection("内存", "\uE950", items);
    }

    private static HardwareSection BuildDiskSection()
    {
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
                items.Add(Pair(monitors.Length > 1 ? $"显示器 {i + 1}" : "显示器", string.IsNullOrWhiteSpace(serial) ? name : $"{name}  SN {serial}"));
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

    private static IReadOnlyList<ManagementObject> Query(string className)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}");
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

    private static string FormatMHz(string value)
    {
        return double.TryParse(value, out var mhz) ? $"{mhz:0} MHz" : "未知";
    }

    private static string FormatBytes(string value)
    {
        if (!ulong.TryParse(value, out var bytes) || bytes == 0)
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
}
