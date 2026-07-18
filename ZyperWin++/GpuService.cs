using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
        public List<GpuOptimizationOperation> OptimizationOperations { get; set; }

        public GpuInfo()
        {
            SupportedOperations = new List<string>();
            OptimizationOperations = new List<GpuOptimizationOperation>();
        }
    }

    public sealed class GpuOptimizationOperation
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool CanApply { get; set; }
        public bool CanRestore { get; set; }
    }

    internal sealed class GpuOptimizationJournalRecord
    {
        public DateTime CreatedAt { get; set; }
        public string OperationId { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public sealed class GpuService
    {
        private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private static readonly object JournalSync = new object();
        private static readonly string JournalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CDiskGlow", "gpu_optimization.tsv");
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr module);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr NvApiQueryInterfaceDelegate(uint interfaceId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvApiFunctionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlxQueryFullVersionDelegate(out ulong fullVersion);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlxInitializeDelegate(ulong version, out IntPtr systemServices);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AdlxTerminateDelegate();

        public static int JournalCount
        {
            get { return ReadJournal().Select(value => value.OperationId).Distinct(StringComparer.Ordinal).Count(); }
        }

        public Task<IList<GpuInfo>> DetectAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IList<GpuInfo>>(() =>
            {
                var items = ReadWmi(cancellationToken);
                EnrichHardwareSensors(items, cancellationToken);
                EnrichNvidia(items, cancellationToken);
                DetectOfficialEntries(items);
                DetectSafeOperations(items, cancellationToken);
                return items;
            }, cancellationToken);
        }

        public Task<FileOperationSummary> ApplyAsync(IEnumerable<GpuOptimizationOperation> operations, CancellationToken cancellationToken)
        {
            return ApplyAsync(operations, null, cancellationToken);
        }

        public Task<FileOperationSummary> ApplyAsync(
            IEnumerable<GpuOptimizationOperation> operations,
            string confirmedBackupRoot,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => Apply(operations, confirmedBackupRoot, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RestoreAsync(IEnumerable<GpuOptimizationOperation> operations, CancellationToken cancellationToken)
        {
            return Task.Run(() => Restore(operations, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RestoreAllAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                IList<GpuOptimizationOperation> operations = ReadJournal()
                    .Select(value => value.OperationId)
                    .Distinct(StringComparer.Ordinal)
                    .Select(value => new GpuOptimizationOperation
                    {
                        Id = value,
                        Name = OperationName(value),
                        CanRestore = true
                    }).ToList();
                return Restore(operations, cancellationToken);
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

        private static void EnrichHardwareSensors(IList<GpuInfo> items, CancellationToken cancellationToken)
        {
            var computer = new Computer { IsGpuEnabled = true };
            var matched = new HashSet<GpuInfo>();
            try
            {
                computer.Open();
                foreach (IHardware hardware in computer.Hardware)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GpuVendor vendor = VendorFromHardwareType(hardware.HardwareType);
                    if (vendor == GpuVendor.Unknown) continue;
                    hardware.Update();

                    GpuInfo target = FindSensorTarget(items, matched, vendor, hardware.Name);
                    if (target == null)
                    {
                        target = new GpuInfo { Name = hardware.Name, Vendor = vendor };
                        items.Add(target);
                    }
                    matched.Add(target);

                    ISensor temperature = PreferredSensor(hardware.Sensors, SensorType.Temperature, "GPU Core");
                    ISensor utilization = PreferredSensor(hardware.Sensors, SensorType.Load, "GPU Core", "D3D 3D");
                    ISensor memoryUsed = PreferredSensor(hardware.Sensors, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
                    ISensor memoryTotal = PreferredSensor(hardware.Sensors, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");

                    if (temperature != null) target.TemperatureCelsius = ClampPercentOrTemperature(temperature.Value.Value, -100, 300);
                    if (utilization != null) target.UtilizationPercent = ClampPercentOrTemperature(utilization.Value.Value, 0, 100);
                    if (memoryUsed != null && memoryUsed.Value.Value >= 0)
                        target.UsedMemoryBytes = (long)(memoryUsed.Value.Value * 1024f * 1024f);
                    if (memoryTotal != null && memoryTotal.Value.Value > 0)
                        target.DedicatedMemoryBytes = (long)(memoryTotal.Value.Value * 1024f * 1024f);

                    if (temperature != null || utilization != null || memoryUsed != null)
                        AddSupportedOperation(target, "读取真实温度、负载与显存传感器");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OperationLogger.Info("显卡检测", "硬件传感器接口不可用：" + ex.Message);
            }
            finally
            {
                try { computer.Close(); } catch { }
            }
        }

        private static GpuInfo FindSensorTarget(IList<GpuInfo> items, ISet<GpuInfo> matched, GpuVendor vendor, string hardwareName)
        {
            List<GpuInfo> candidates = items.Where(value => value.Vendor == vendor && !matched.Contains(value)).ToList();
            if (candidates.Count == 0) candidates = items.Where(value => value.Vendor == vendor).ToList();
            return candidates
                .OrderByDescending(value => DeviceNameScore(value.Name, hardwareName))
                .FirstOrDefault();
        }

        private static int DeviceNameScore(string left, string right)
        {
            string first = NormalizeDeviceName(left);
            string second = NormalizeDeviceName(right);
            if (first.Length == 0 || second.Length == 0) return 0;
            if (first.Contains(second) || second.Contains(first)) return 1000 + Math.Min(first.Length, second.Length);
            return first.Split(' ').Where(token => token.Length > 1 && second.Contains(token)).Sum(token => token.Length);
        }

        private static string NormalizeDeviceName(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"\b(nvidia|amd|intel|corporation|graphics|gpu)\b", " ")
                .Replace("(r)", string.Empty)
                .Replace("(tm)", string.Empty)
                .Trim();
        }

        private static ISensor PreferredSensor(IEnumerable<ISensor> sensors, SensorType type, params string[] preferredNames)
        {
            List<ISensor> available = sensors.Where(value => value.SensorType == type && value.Value.HasValue).ToList();
            foreach (string name in preferredNames)
            {
                ISensor exact = available.FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }
            return available.FirstOrDefault();
        }

        private static int ClampPercentOrTemperature(float value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, Convert.ToInt32(Math.Round(value))));
        }

        private static GpuVendor VendorFromHardwareType(HardwareType type)
        {
            switch (type)
            {
                case HardwareType.GpuNvidia: return GpuVendor.Nvidia;
                case HardwareType.GpuAmd: return GpuVendor.Amd;
                case HardwareType.GpuIntel: return GpuVendor.Intel;
                default: return GpuVendor.Unknown;
            }
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
                AddSupportedOperation(target, "通过 nvidia-smi 读取温度、负载与显存占用");
                AddSupportedOperation(target, "生成 NVIDIA 状态报告");
            }

            string nvapiName = Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll";
            string nvapi = FindFile(
                nvapiName,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32",
                    nvapiName));
            foreach (GpuInfo gpu in items.Where(value => value.Vendor == GpuVendor.Nvidia))
            {
                gpu.NvApiAvailable = ProbeNvApi(nvapi);
                if (gpu.NvApiAvailable) AddSupportedOperation(gpu, "NVAPI 初始化与支持检测通过");
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
                        string adlxName = Environment.Is64BitProcess ? "amdadlx64.dll" : "amdadlx32.dll";
                        string adlx = FindFile(
                            adlxName,
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32",
                                adlxName),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "CNext", "CNext",
                                adlxName));
                        string adlxVersion;
                        gpu.AdlxEntryAvailable = ProbeAdlx(adlx, out adlxVersion);
                        if (gpu.AdlxEntryAvailable) AddSupportedOperation(gpu, "AMD ADLX " + adlxVersion + " 初始化与支持检测通过");
                        if (!string.IsNullOrWhiteSpace(gpu.ControlPanelPath)) AddSupportedOperation(gpu, "打开 AMD Software 官方调优入口");
                        break;
                    case GpuVendor.Intel:
                        gpu.ControlPanelPath = FindFile(
                            "IntelGraphicsSoftware.exe",
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                "Intel", "Intel Graphics Software", "IntelGraphicsSoftware.exe"));
                        if (!string.IsNullOrWhiteSpace(gpu.ControlPanelPath))
                            AddSupportedOperation(gpu, "打开 Intel Graphics Software");
                        break;
                }

                if (!string.IsNullOrWhiteSpace(gpu.ControlPanelPath) &&
                    !gpu.SupportedOperations.Any(value => value.IndexOf("打开", StringComparison.Ordinal) >= 0))
                    AddSupportedOperation(gpu, "打开厂商官方控制面板");
            }
        }

        private static void DetectSafeOperations(IList<GpuInfo> items, CancellationToken cancellationToken)
        {
            ProcessResult plans = ProcessRunner.Run("powercfg.exe", "/list", 15000, cancellationToken);
            ProcessResult active = ProcessRunner.Run("powercfg.exe", "/getactivescheme", 15000, cancellationToken);
            bool highPerformanceAvailable = plans.Success && plans.Output.IndexOf(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) >= 0;
            string activeGuid = active.Success ? ParsePowerSchemeGuid(active.Output) : null;
            IList<GpuOptimizationJournalRecord> journal = ReadJournal();

            foreach (GpuInfo gpu in items)
            {
                bool powerRestore = journal.Any(record => record.OperationId == "power-high-performance");
                if (highPerformanceAvailable && (!string.Equals(activeGuid, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) || powerRestore))
                {
                    gpu.OptimizationOperations.Add(new GpuOptimizationOperation
                    {
                        Id = "power-high-performance",
                        Name = "高性能电源计划",
                        Description = "切换 Windows 已安装的高性能电源计划；还原时恢复修改前的计划。",
                        CanApply = !powerRestore && !string.Equals(activeGuid, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase),
                        CanRestore = powerRestore
                    });
                }

                string shaderId = "shader-cache-" + gpu.Vendor.ToString().ToLowerInvariant();
                bool shaderRestore = journal.Any(record => record.OperationId == shaderId);
                bool externalBackup;
                string ignored;
                externalBackup = BackupStore.TryGetSpaceReleasingRoot(out ignored);
                bool hasCacheFiles = ShaderCachePaths(gpu.Vendor).Any(path => EnumerateFiles(path).Any());
                if (shaderRestore || (externalBackup && hasCacheFiles))
                {
                    gpu.OptimizationOperations.Add(new GpuOptimizationOperation
                    {
                        Id = shaderId,
                        Name = VendorName(gpu.Vendor) + " 着色器缓存清理",
                        Description = "逐文件备份到其它磁盘后删除可再生成的厂商着色器缓存；可从操作记录还原。",
                        CanApply = !shaderRestore && externalBackup && hasCacheFiles,
                        CanRestore = shaderRestore
                    });
                }
            }
        }

        private static FileOperationSummary Apply(
            IEnumerable<GpuOptimizationOperation> operations,
            string confirmedBackupRoot,
            CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            foreach (GpuOptimizationOperation operation in (operations ?? Enumerable.Empty<GpuOptimizationOperation>())
                .Where(value => value.CanApply)
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => value.First()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation.Id == "power-high-performance") ApplyPowerPlan(operation, result, cancellationToken);
                else if (operation.Id.StartsWith("shader-cache-", StringComparison.Ordinal))
                    ApplyShaderCache(operation, confirmedBackupRoot, result, cancellationToken);
            }
            return result;
        }

        private static void ApplyPowerPlan(GpuOptimizationOperation operation, FileOperationSummary result, CancellationToken cancellationToken)
        {
            ProcessResult current = ProcessRunner.Run("powercfg.exe", "/getactivescheme", 15000, cancellationToken);
            string previousGuid = current.Success ? ParsePowerSchemeGuid(current.Output) : null;
            if (string.IsNullOrWhiteSpace(previousGuid))
            {
                result.Errors.Add("无法读取当前电源计划，未执行切换。");
                return;
            }
            var record = new GpuOptimizationJournalRecord
            {
                CreatedAt = DateTime.UtcNow,
                OperationId = operation.Id,
                Key = "previous-scheme",
                Value = previousGuid
            };
            AddJournal(record);
            ProcessResult change = ProcessRunner.Run("powercfg.exe", "/setactive " + HighPerformanceGuid, 30000, cancellationToken);
            if (!change.Success)
            {
                RemoveJournal(new[] { record });
                result.Errors.Add("切换高性能电源计划失败：" + ResultMessage(change));
                return;
            }
            result.AffectedPaths.Add("Windows 电源计划：" + HighPerformanceGuid);
            OperationLogger.Info("显卡优化", "切换高性能电源计划，原计划 " + previousGuid);
        }

        private static void ApplyShaderCache(
            GpuOptimizationOperation operation,
            string confirmedBackupRoot,
            FileOperationSummary result,
            CancellationToken cancellationToken)
        {
            GpuVendor vendor;
            if (!Enum.TryParse(operation.Id.Substring("shader-cache-".Length), true, out vendor))
            {
                result.Errors.Add("未知显卡缓存操作：" + operation.Id);
                return;
            }
            if (string.IsNullOrWhiteSpace(confirmedBackupRoot))
            {
                result.Errors.Add(operation.Name + "：确认的非 C 盘备份位置不可用，未删除缓存。");
                return;
            }
            foreach (string file in ShaderCachePaths(vendor).SelectMany(EnumerateFiles).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    BackupRecord backup;
                    string error;
                    if (!BackupStore.TryBackup(file, confirmedBackupRoot, out backup, out error) || backup == null)
                    {
                        result.Errors.Add(error ?? ("备份失败：" + file));
                        continue;
                    }
                    var record = new GpuOptimizationJournalRecord
                    {
                        CreatedAt = DateTime.UtcNow,
                        OperationId = operation.Id,
                        Key = backup.SourcePath,
                        Value = backup.BackupPath
                    };
                    AddJournal(record);
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    result.AffectedPaths.Add(file);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(file + "：" + ex.Message);
                }
            }
            OperationLogger.Info("显卡优化", operation.Name + "，" + result.Message);
        }

        private static FileOperationSummary Restore(IEnumerable<GpuOptimizationOperation> operations, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            IList<GpuOptimizationJournalRecord> journal = ReadJournal();
            foreach (GpuOptimizationOperation operation in (operations ?? Enumerable.Empty<GpuOptimizationOperation>())
                .Where(value => value.CanRestore)
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => value.First()))
            {
                IList<GpuOptimizationJournalRecord> records = journal.Where(record => record.OperationId == operation.Id).ToList();
                var restored = new List<GpuOptimizationJournalRecord>();
                foreach (GpuOptimizationJournalRecord record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (operation.Id == "power-high-performance")
                        {
                            ProcessResult change = ProcessRunner.Run("powercfg.exe", "/setactive " + record.Value, 30000, cancellationToken);
                            if (!change.Success) throw new IOException(ResultMessage(change));
                            result.AffectedPaths.Add("Windows 电源计划：" + record.Value);
                        }
                        else
                        {
                            if (!File.Exists(record.Value)) throw new FileNotFoundException("备份文件不存在。", record.Value);
                            string parent = Path.GetDirectoryName(record.Key);
                            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                            File.Copy(record.Value, record.Key, true);
                            result.AffectedPaths.Add(record.Key);
                        }
                        restored.Add(record);
                    }
                    catch (Exception ex) { result.Errors.Add(operation.Name + "：" + ex.Message); }
                }
                RemoveJournal(restored);
                OperationLogger.Info("显卡优化还原", operation.Name + "，" + restored.Count + " 项");
            }
            return result;
        }

        internal static string ParsePowerSchemeGuid(string output)
        {
            Match match = Regex.Match(output ?? string.Empty, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }

        internal static IList<string> ShaderCachePaths(GpuVendor vendor)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            switch (vendor)
            {
                case GpuVendor.Nvidia:
                    return new[] { Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "NVIDIA", "GLCache"),
                        Path.Combine(programData, "NVIDIA Corporation", "NV_Cache") };
                case GpuVendor.Amd:
                    return new[] { Path.Combine(local, "AMD", "DxCache"), Path.Combine(local, "AMD", "GLCache"), Path.Combine(local, "AMD", "VkCache") };
                case GpuVendor.Intel:
                    return new[] { Path.Combine(local, "Intel", "ShaderCache"), Path.Combine(local, "Intel", "ShaderCacheD3D12") };
                default:
                    return new string[0];
            }
        }

        private static IEnumerable<string> EnumerateFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) yield break;
            IEnumerator<string> enumerator;
            try { enumerator = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).GetEnumerator(); }
            catch { yield break; }
            using (enumerator)
            {
                while (true)
                {
                    string current;
                    try
                    {
                        if (!enumerator.MoveNext()) yield break;
                        current = enumerator.Current;
                    }
                    catch { yield break; }
                    yield return current;
                }
            }
        }

        private static IList<GpuOptimizationJournalRecord> ReadJournal()
        {
            lock (JournalSync)
            {
                var result = new List<GpuOptimizationJournalRecord>();
                if (!File.Exists(JournalPath)) return result;
                string[] lines = File.ReadAllLines(JournalPath, Encoding.UTF8);
                for (int index = 0; index < lines.Length; index++)
                {
                    string line = lines[index];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] fields = line.Split('\t');
                    if (fields.Length != 4)
                        throw new InvalidDataException("显卡优化还原记录第 " + (index + 1) + " 行格式无效。");
                    try
                    {
                        result.Add(new GpuOptimizationJournalRecord
                        {
                            CreatedAt = DateTime.Parse(fields[0]).ToUniversalTime(),
                            OperationId = fields[1],
                            Key = Decode(fields[2]),
                            Value = Decode(fields[3])
                        });
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("显卡优化还原记录第 " + (index + 1) + " 行无法解析。", ex);
                    }
                }
                return result;
            }
        }

        private static void AddJournal(GpuOptimizationJournalRecord record)
        {
            IList<GpuOptimizationJournalRecord> records = ReadJournal();
            records.Add(record);
            WriteJournal(records);
        }

        private static void RemoveJournal(IEnumerable<GpuOptimizationJournalRecord> removed)
        {
            var set = new HashSet<GpuOptimizationJournalRecord>(removed ?? Enumerable.Empty<GpuOptimizationJournalRecord>());
            WriteJournal(ReadJournal().Where(record => !set.Contains(record) &&
                !set.Any(value => value.OperationId == record.OperationId && value.Key == record.Key && value.Value == record.Value)).ToList());
        }

        private static void WriteJournal(IList<GpuOptimizationJournalRecord> records)
        {
            lock (JournalSync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(JournalPath));
                string temporary = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllLines(temporary, records.Select(record => string.Join("\t", record.CreatedAt.ToString("o"),
                        record.OperationId, Encode(record.Key), Encode(record.Value))).ToArray(), new UTF8Encoding(false));
                    FileSystemTools.ReplaceFile(temporary, JournalPath);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporary) && File.Exists(JournalPath)) File.Delete(temporary);
                        else if (File.Exists(temporary))
                            OperationLogger.Error("显卡优化", "日志替换失败，恢复副本已保留：" + temporary);
                    }
                    catch { }
                }
            }
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        private static string ResultMessage(ProcessResult result)
        {
            return string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        }

        private static string VendorName(GpuVendor vendor)
        {
            switch (vendor)
            {
                case GpuVendor.Nvidia: return "NVIDIA";
                case GpuVendor.Amd: return "AMD";
                case GpuVendor.Intel: return "Intel";
                default: return "未知厂商";
            }
        }

        private static string OperationName(string operationId)
        {
            if (operationId == "power-high-performance") return "高性能电源计划";
            if (operationId.StartsWith("shader-cache-", StringComparison.Ordinal)) return "显卡着色器缓存清理";
            return operationId;
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

        private static bool ProbeNvApi(string libraryPath)
        {
            if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath)) return false;
            IntPtr module = IntPtr.Zero;
            try
            {
                module = LoadLibrary(libraryPath);
                if (module == IntPtr.Zero) return false;
                IntPtr queryAddress = GetProcAddress(module, "nvapi_QueryInterface");
                if (queryAddress == IntPtr.Zero) return false;
                var query = Marshal.GetDelegateForFunctionPointer<NvApiQueryInterfaceDelegate>(queryAddress);
                IntPtr initializeAddress = query(0x0150e828);
                if (initializeAddress == IntPtr.Zero) return false;
                var initialize = Marshal.GetDelegateForFunctionPointer<NvApiFunctionDelegate>(initializeAddress);
                if (initialize() != 0) return false;

                IntPtr unloadAddress = query(0xd22bdd7e);
                if (unloadAddress != IntPtr.Zero)
                {
                    var unload = Marshal.GetDelegateForFunctionPointer<NvApiFunctionDelegate>(unloadAddress);
                    unload();
                }
                return true;
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

        private static bool ProbeAdlx(string libraryPath, out string versionText)
        {
            versionText = string.Empty;
            if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath)) return false;
            IntPtr module = IntPtr.Zero;
            bool initialized = false;
            AdlxTerminateDelegate terminate = null;
            try
            {
                module = LoadLibrary(libraryPath);
                if (module == IntPtr.Zero) return false;
                IntPtr queryAddress = GetProcAddress(module, "ADLXQueryFullVersion");
                IntPtr initializeAddress = GetProcAddress(module, "ADLXInitialize");
                IntPtr terminateAddress = GetProcAddress(module, "ADLXTerminate");
                if (queryAddress == IntPtr.Zero || initializeAddress == IntPtr.Zero || terminateAddress == IntPtr.Zero) return false;

                var query = Marshal.GetDelegateForFunctionPointer<AdlxQueryFullVersionDelegate>(queryAddress);
                var initialize = Marshal.GetDelegateForFunctionPointer<AdlxInitializeDelegate>(initializeAddress);
                terminate = Marshal.GetDelegateForFunctionPointer<AdlxTerminateDelegate>(terminateAddress);
                ulong fullVersion;
                if (!AdlxSucceeded(query(out fullVersion)) || fullVersion == 0) return false;
                IntPtr systemServices;
                int initializationResult = initialize(fullVersion, out systemServices);
                initialized = AdlxSucceeded(initializationResult);
                if (!initialized || systemServices == IntPtr.Zero) return false;
                versionText = FormatAdlxVersion(fullVersion);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (initialized && terminate != null)
                {
                    try { terminate(); } catch { }
                }
                if (module != IntPtr.Zero) FreeLibrary(module);
            }
        }

        private static bool AdlxSucceeded(int result)
        {
            return result == 0 || result == 1 || result == 2;
        }

        private static string FormatAdlxVersion(ulong version)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}.{3}",
                (version >> 48) & 0xffff,
                (version >> 32) & 0xffff,
                (version >> 16) & 0xffff,
                version & 0xffff);
        }

        private static void AddSupportedOperation(GpuInfo gpu, string operation)
        {
            if (!gpu.SupportedOperations.Contains(operation, StringComparer.Ordinal)) gpu.SupportedOperations.Add(operation);
        }
    }
}
