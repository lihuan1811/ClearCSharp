using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    public enum GpuVendor
    {
        Nvidia,
        Amd,
        Intel,
        Unknown
    }

    public sealed class GpuInfo
    {
        public string Name { get; set; }
        public GpuVendor Vendor { get; set; }
        public string DriverVersion { get; set; }
        public long DedicatedMemoryBytes { get; set; }
        public int? TemperatureCelsius { get; set; }
        public int? UtilizationPercent { get; set; }
        public long? UsedMemoryBytes { get; set; }
        public bool NvidiaSmiAvailable { get; set; }
        public bool NvApiAvailable { get; set; }
        public bool AdlxEntryAvailable { get; set; }
        public string ControlPanelPath { get; set; }
        public List<string> SupportedOperations { get; set; }

        public GpuInfo()
        {
            SupportedOperations = new List<string>();
        }
    }

    public sealed class GpuService
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr module);

        public Task<IList<GpuInfo>> DetectAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IList<GpuInfo>>(() =>
            {
                var items = ReadWmi(cancellationToken);
                EnrichNvidia(items, cancellationToken);
                DetectOfficialEntries(items);
                return items;
            }, cancellationToken);
        }

        private static List<GpuInfo> ReadWmi(CancellationToken cancellationToken)
        {
            var result = new List<GpuInfo>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController"))
            using (ManagementObjectCollection objects = searcher.Get())
            {
                foreach (ManagementObject item in objects)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Convert.ToString(item["Name"], CultureInfo.CurrentCulture);
                    ulong memory = 0;
                    try { memory = Convert.ToUInt64(item["AdapterRAM"], CultureInfo.InvariantCulture); } catch { }
                    result.Add(new GpuInfo
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "未知显示适配器" : name,
                        Vendor = DetectVendor(name),
                        DriverVersion = Convert.ToString(item["DriverVersion"], CultureInfo.CurrentCulture),
                        DedicatedMemoryBytes = memory > long.MaxValue ? long.MaxValue : (long)memory
                    });
                }
            }
            return result;
        }

        private static void EnrichNvidia(IList<GpuInfo> items, CancellationToken cancellationToken)
        {
            string executable = FindFile(
                "nvidia-smi.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "nvidia-smi.exe"));
            if (string.IsNullOrWhiteSpace(executable)) return;

            ProcessResult query = ProcessRunner.Run(
                executable,
                "--query-gpu=index,name,driver_version,memory.total,memory.used,temperature.gpu,utilization.gpu --format=csv,noheader,nounits",
                15000,
                cancellationToken);
            if (!query.Success) return;

            foreach (string line in query.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split(',').Select(value => value.Trim()).ToArray();
                if (fields.Length < 7) continue;
                string name = fields[1];
                GpuInfo target = items.FirstOrDefault(value =>
                    value.Vendor == GpuVendor.Nvidia &&
                    (value.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf(value.Name, StringComparison.OrdinalIgnoreCase) >= 0));
                if (target == null) target = items.FirstOrDefault(value => value.Vendor == GpuVendor.Nvidia);
                if (target == null)
                {
                    target = new GpuInfo { Name = name, Vendor = GpuVendor.Nvidia };
                    items.Add(target);
                }

                long totalMb;
                long usedMb;
                int temperature;
                int utilization;
                if (long.TryParse(fields[3], out totalMb)) target.DedicatedMemoryBytes = totalMb * 1024L * 1024L;
                if (long.TryParse(fields[4], out usedMb)) target.UsedMemoryBytes = usedMb * 1024L * 1024L;
                if (int.TryParse(fields[5], out temperature)) target.TemperatureCelsius = temperature;
                if (int.TryParse(fields[6], out utilization)) target.UtilizationPercent = utilization;
                target.DriverVersion = fields[2];
                target.NvidiaSmiAvailable = true;
                target.SupportedOperations.Add("读取温度、负载与显存占用");
                target.SupportedOperations.Add("生成 NVIDIA 状态报告");
            }

            string nvapi = FindFile(
                Environment.Is64BitOperatingSystem ? "nvapi64.dll" : "nvapi.dll",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32",
                    Environment.Is64BitOperatingSystem ? "nvapi64.dll" : "nvapi.dll"));
            foreach (GpuInfo gpu in items.Where(value => value.Vendor == GpuVendor.Nvidia))
            {
                gpu.NvApiAvailable = HasExport(nvapi, "nvapi_QueryInterface");
                if (gpu.NvApiAvailable) gpu.SupportedOperations.Add("检测到 NVAPI 驱动入口");
            }
        }

        private static void DetectOfficialEntries(IList<GpuInfo> items)
        {
            foreach (GpuInfo gpu in items)
            {
                switch (gpu.Vendor)
                {
                    case GpuVendor.Nvidia:
                        gpu.ControlPanelPath = FindFile(
                            "nvcplui.exe",
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe"));
                        break;
                    case GpuVendor.Amd:
                        gpu.ControlPanelPath = FindFile(
                            "RadeonSoftware.exe",
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                "AMD", "CNext", "CNext", "RadeonSoftware.exe"));
                        string adlx = FindFile(
                            Environment.Is64BitOperatingSystem ? "amdadlx64.dll" : "amdadlx32.dll",
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32",
                                Environment.Is64BitOperatingSystem ? "amdadlx64.dll" : "amdadlx32.dll"),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "CNext", "CNext",
                                Environment.Is64BitOperatingSystem ? "amdadlx64.dll" : "amdadlx32.dll"));
                        gpu.AdlxEntryAvailable = !string.IsNullOrWhiteSpace(adlx);
                        if (gpu.AdlxEntryAvailable) gpu.SupportedOperations.Add("检测到 AMD ADLX 驱动入口");
                        if (!string.IsNullOrWhiteSpace(gpu.ControlPanelPath)) gpu.SupportedOperations.Add("打开 AMD Software 官方调优入口");
                        break;
                    case GpuVendor.Intel:
                        gpu.ControlPanelPath = FindFile(
                            "IntelGraphicsSoftware.exe",
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                "Intel", "Intel Graphics Software", "IntelGraphicsSoftware.exe"));
                        if (!string.IsNullOrWhiteSpace(gpu.ControlPanelPath))
                            gpu.SupportedOperations.Add("打开 Intel Graphics Software");
                        break;
                }

                if (!string.IsNullOrWhiteSpace(gpu.ControlPanelPath) &&
                    !gpu.SupportedOperations.Any(value => value.IndexOf("打开", StringComparison.Ordinal) >= 0))
                    gpu.SupportedOperations.Add("打开厂商官方控制面板");
            }
        }

        private static GpuVendor DetectVendor(string name)
        {
            string value = name ?? string.Empty;
            if (value.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return GpuVendor.Nvidia;
            if (value.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) return GpuVendor.Amd;
            if (value.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) return GpuVendor.Intel;
            return GpuVendor.Unknown;
        }

        private static string FindFile(string commandName, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string folder in path.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(folder.Trim(), commandName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            return null;
        }

        private static bool HasExport(string libraryPath, string exportName)
        {
            if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath)) return false;
            IntPtr module = IntPtr.Zero;
            try
            {
                module = LoadLibrary(libraryPath);
                return module != IntPtr.Zero && GetProcAddress(module, exportName) != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (module != IntPtr.Zero) FreeLibrary(module);
            }
        }
    }
}
