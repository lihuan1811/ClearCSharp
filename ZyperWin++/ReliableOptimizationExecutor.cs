using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace ZyperWin__
{
    internal sealed class OptimizationExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int AffectedCount { get; set; }
    }

    internal sealed class OptimizationBackupRecord
    {
        public string Id { get; set; }
        public string ItemTag { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public bool Applied { get; set; }
        public List<OptimizationSnapshot> Snapshots { get; set; } = new List<OptimizationSnapshot>();
    }

    internal sealed class OptimizationSnapshot
    {
        public string Kind { get; set; }
        public string Key { get; set; }
        public string ValueName { get; set; }
        public bool Exists { get; set; }
        public bool KeyExisted { get; set; }
        public string ValueKind { get; set; }
        public string Data { get; set; }
    }

    internal sealed class RegistryTreeSnapshot
    {
        public List<RegistryTreeValueSnapshot> Values { get; set; } = new List<RegistryTreeValueSnapshot>();
        public Dictionary<string, RegistryTreeSnapshot> SubKeys { get; set; } = new Dictionary<string, RegistryTreeSnapshot>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class RegistryTreeValueSnapshot
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Data { get; set; }
    }

    internal static class OptimizationBackupStore
    {
        private static readonly object Sync = new object();
        private static readonly string JournalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDiskGlow",
            "optimization_backups_v2.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public static IList<OptimizationBackupRecord> ReadAll()
        {
            lock (Sync)
            {
                return ReadUnsafe().OrderByDescending(value => value.CreatedAtUtc).ToList();
            }
        }

        public static void Add(OptimizationBackupRecord record)
        {
            lock (Sync)
            {
                List<OptimizationBackupRecord> records = ReadUnsafe();
                records.Add(record);
                WriteUnsafe(records);
            }
        }

        public static void Update(OptimizationBackupRecord record)
        {
            lock (Sync)
            {
                List<OptimizationBackupRecord> records = ReadUnsafe();
                int index = records.FindIndex(value => string.Equals(value.Id, record.Id, StringComparison.Ordinal));
                if (index < 0) records.Add(record); else records[index] = record;
                WriteUnsafe(records);
            }
        }

        public static void Remove(string id)
        {
            lock (Sync)
            {
                List<OptimizationBackupRecord> records = ReadUnsafe();
                records.RemoveAll(value => string.Equals(value.Id, id, StringComparison.Ordinal));
                WriteUnsafe(records);
            }
        }

        private static List<OptimizationBackupRecord> ReadUnsafe()
        {
            try
            {
                if (!File.Exists(JournalPath)) return new List<OptimizationBackupRecord>();
                return JsonSerializer.Deserialize<List<OptimizationBackupRecord>>(File.ReadAllText(JournalPath, Encoding.UTF8), JsonOptions)
                    ?? new List<OptimizationBackupRecord>();
            }
            catch (Exception ex)
            {
                OperationLogger.Error("系统优化快照", "快照索引损坏，已停止操作：" + ex.Message);
                throw new InvalidDataException("系统优化快照损坏，为保护原设置已停止操作。快照位置：" + JournalPath, ex);
            }
        }

        private static void WriteUnsafe(IList<OptimizationBackupRecord> records)
        {
            string directory = Path.GetDirectoryName(JournalPath);
            Directory.CreateDirectory(directory);
            string temporary = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions), new UTF8Encoding(false));
                FileSystemTools.ReplaceFile(temporary, JournalPath);
            }
            finally
            {
                if (File.Exists(temporary) && File.Exists(JournalPath)) File.Delete(temporary);
                else if (File.Exists(temporary))
                    OperationLogger.Error("系统优化快照", "快照替换失败，恢复副本已保留：" + temporary);
            }
        }
    }

    internal static class ReliableOptimizationExecutor
    {
        private const string HypervisorItem = "10、关闭虚拟化安全性";
        private const string ReservedStorageItem = "31、禁用保留的存储";
        private static readonly RegistryView NativeRegistryView = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;

        public static int JournalCount
        {
            get { return OptimizationBackupStore.ReadAll().Count; }
        }

        public static HashSet<string> JournaledTags()
        {
            return new HashSet<string>(OptimizationBackupStore.ReadAll().Select(value => value.ItemTag), StringComparer.Ordinal);
        }

        public static IDictionary<string, DateTime> LatestSnapshotTimes()
        {
            return OptimizationBackupStore.ReadAll()
                .GroupBy(value => value.ItemTag, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Max(value => value.CreatedAtUtc), StringComparer.Ordinal);
        }

        public static bool IsSupported(XElement item, string itemTag, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (item == null) throw new InvalidDataException("优化规则不存在。");
                var snapshots = new List<OptimizationSnapshot>();
                CaptureSpecialState(itemTag, snapshots);
                XElement optimize = item.Element("Optimize") ?? throw new InvalidDataException("缺少 Optimize 配置。");
                foreach (XElement command in optimize.Elements())
                {
                    EnsureCommandTargetIsAvailable(command);
                    CaptureCommand(command, snapshots);
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        public static OptimizationExecutionResult RestoreAllRecorded()
        {
            IList<OptimizationBackupRecord> records = OptimizationBackupStore.ReadAll()
                .OrderByDescending(value => value.CreatedAtUtc)
                .ToList();
            var errors = new List<string>();
            int restored = 0;
            foreach (OptimizationBackupRecord record in records)
            {
                OptimizationExecutionResult result = RestoreRecord(record, true);
                if (result.Success)
                {
                    OptimizationBackupStore.Remove(record.Id);
                    restored++;
                    OperationLogger.Info("系统优化还原", record.ItemTag + " 已恢复到修改前状态");
                }
                else
                {
                    errors.Add(record.ItemTag + "：" + result.Message);
                    OperationLogger.Error("系统优化还原", record.ItemTag + "：" + result.Message);
                    break;
                }
            }

            return new OptimizationExecutionResult
            {
                Success = errors.Count == 0,
                AffectedCount = restored,
                Message = errors.Count == 0
                    ? "已按修改时间倒序恢复 " + restored + " 条真实快照。"
                    : "已恢复 " + restored + " 条快照，以下快照失败：" + Environment.NewLine + string.Join(Environment.NewLine, errors)
            };
        }

        public static OptimizationExecutionResult Apply(XElement item, string itemTag)
        {
            if (item == null) return Failure("优化规则不存在：" + itemTag);
            var record = new OptimizationBackupRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                ItemTag = itemTag,
                CreatedAtUtc = DateTime.UtcNow
            };

            try
            {
                CaptureSpecialState(itemTag, record.Snapshots);
                XElement optimize = item.Element("Optimize") ?? throw new InvalidOperationException("缺少 Optimize 配置。");
                foreach (XElement command in optimize.Elements()) CaptureCommand(command, record.Snapshots);
                OptimizationBackupStore.Add(record);
            }
            catch (Exception ex)
            {
                return Failure("无法在修改前创建完整快照：" + ex.Message);
            }

            try
            {
                ExecuteSpecial(itemTag, false);
                foreach (XElement command in item.Element("Optimize").Elements()) ExecuteCommand(command);
                string verificationError;
                if (!VerifyItem(item, itemTag, out verificationError))
                    throw new InvalidOperationException("执行后验证失败：" + verificationError);

                record.Applied = true;
                OptimizationBackupStore.Update(record);
                OperationLogger.Info("系统优化", itemTag + " 已执行并验证，已保存原状态快照");
                return Success(itemTag + " 已执行并验证。");
            }
            catch (Exception ex)
            {
                OptimizationExecutionResult rollback = RestoreRecord(record, true);
                if (rollback.Success) OptimizationBackupStore.Remove(record.Id);
                string suffix = rollback.Success ? "已按快照回滚。" : "自动回滚未完整完成：" + rollback.Message;
                OperationLogger.Error("系统优化", itemTag + "：" + ex.Message + "；" + suffix);
                return Failure(ex.Message + "；" + suffix);
            }
        }

        public static OptimizationExecutionResult RestoreLatest(string itemTag)
        {
            OptimizationBackupRecord record = OptimizationBackupStore.ReadAll()
                .Where(value => string.Equals(value.ItemTag, itemTag, StringComparison.Ordinal))
                .OrderByDescending(value => value.CreatedAtUtc)
                .FirstOrDefault();
            if (record == null) return Failure("没有该项目修改前的真实快照，未执行预设值覆盖：" + itemTag);

            OptimizationExecutionResult result = RestoreRecord(record, true);
            if (result.Success)
            {
                OptimizationBackupStore.Remove(record.Id);
                OperationLogger.Info("系统优化还原", itemTag + " 已恢复到修改前状态");
            }
            else OperationLogger.Error("系统优化还原", itemTag + "：" + result.Message);
            return result;
        }

        public static bool VerifyItem(XElement item, string itemTag, out string error)
        {
            error = string.Empty;
            try
            {
                if (item == null) throw new InvalidOperationException("优化规则不存在。");
                XElement optimize = item.Element("Optimize") ?? throw new InvalidOperationException("缺少 Optimize 配置。");
                foreach (XElement command in optimize.Elements()) VerifyCommand(command);
                VerifySpecial(itemTag, false);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static OptimizationExecutionResult RestoreRecord(OptimizationBackupRecord record, bool verify)
        {
            var errors = new List<string>();
            foreach (OptimizationSnapshot snapshot in record.Snapshots.AsEnumerable().Reverse())
            {
                try { RestoreSnapshot(snapshot); }
                catch (Exception ex) { errors.Add(snapshot.Kind + " " + snapshot.Key + "：" + ex.Message); }
            }

            if (verify)
            {
                foreach (OptimizationSnapshot snapshot in record.Snapshots)
                {
                    try { VerifySnapshot(snapshot); }
                    catch (Exception ex) { errors.Add("还原验证失败 " + snapshot.Kind + " " + snapshot.Key + "：" + ex.Message); }
                }
            }
            return errors.Count == 0 ? Success("已恢复到修改前状态。") : Failure(string.Join(Environment.NewLine, errors));
        }

        private static void CaptureCommand(XElement command, IList<OptimizationSnapshot> snapshots)
        {
            switch (command.Name.LocalName)
            {
                case "RegWrite":
                    snapshots.Add(CaptureRegistryValue(Required(command, "Key"), command.Attribute("Value")?.Value ?? string.Empty));
                    break;
                case "RegDelete":
                    string valueName = command.Attribute("Value")?.Value;
                    snapshots.Add(string.IsNullOrEmpty(valueName)
                        ? CaptureRegistryTree(Required(command, "Key"))
                        : CaptureRegistryValue(Required(command, "Key"), valueName));
                    break;
                case "SetServiceStart":
                    string serviceName = Required(command, "Name");
                    snapshots.Add(CaptureServiceState(serviceName));
                    snapshots.Add(CaptureRegistryValue(ServiceRegistryPath(serviceName), "Start", true));
                    break;
                case "ExplorerNotify":
                    CaptureExplorerCommand(command, snapshots);
                    break;
                case "PowerCfg":
                    snapshots.Add(CapturePowerPlan());
                    break;
                default:
                    throw new NotSupportedException("不支持的优化命令：" + command.Name.LocalName);
            }
        }

        private static void EnsureCommandTargetIsAvailable(XElement command)
        {
            string scheme = null;
            if (command.Name.LocalName == "PowerCfg")
                scheme = Required(command, "Scheme");
            else if (command.Name.LocalName == "ExplorerNotify" &&
                     string.Equals(command.Attribute("Type")?.Value, "Cmd", StringComparison.OrdinalIgnoreCase))
            {
                Match match = Regex.Match(Required(command, "Cmd"),
                    @"^powercfg(?:\.exe)?\s+-setactive\s+([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase);
                if (match.Success) scheme = match.Groups[1].Value;
            }
            if (string.IsNullOrWhiteSpace(scheme)) return;
            ProcessResult plans = ProcessRunner.Run("powercfg.exe", "/list", 15000, CancellationToken.None);
            if (!plans.Success || plans.Output.IndexOf(scheme, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("当前系统没有电源计划：" + scheme);
        }

        private static void ExecuteCommand(XElement command)
        {
            switch (command.Name.LocalName)
            {
                case "RegWrite": ExecuteRegWrite(command); break;
                case "RegDelete": ExecuteRegDelete(command); break;
                case "SetServiceStart": ExecuteServiceStart(command); break;
                case "ExplorerNotify": ExecuteExplorerCommand(command); break;
                case "PowerCfg": ExecutePowerCfg(command); break;
                default: throw new NotSupportedException("不支持的优化命令：" + command.Name.LocalName);
            }
            VerifyCommand(command);
        }

        private static void VerifyCommand(XElement command)
        {
            switch (command.Name.LocalName)
            {
                case "RegWrite": VerifyRegWrite(command); break;
                case "RegDelete": VerifyRegDelete(command); break;
                case "SetServiceStart": VerifyServiceStart(command); break;
                case "ExplorerNotify": VerifyExplorerCommand(command); break;
                case "PowerCfg": VerifyPowerCfg(command); break;
                default: throw new NotSupportedException("不支持的优化命令：" + command.Name.LocalName);
            }
        }

        private static void ExecuteRegWrite(XElement command)
        {
            string keyPath = Required(command, "Key");
            string valueName = command.Attribute("Value")?.Value ?? string.Empty;
            RegistryValueKind kind = ParseValueKind(command.Attribute("Type")?.Value);
            object value = ParseConfiguredValue(command.Attribute("Data")?.Value, kind);
            using RegistryKey key = OpenKey(keyPath, true, true) ?? throw new IOException("无法创建注册表键：" + keyPath);
            key.SetValue(valueName, value, kind);
        }

        private static void ExecuteRegDelete(XElement command)
        {
            string keyPath = Required(command, "Key");
            string valueName = command.Attribute("Value")?.Value;
            if (string.IsNullOrEmpty(valueName))
            {
                DeleteRegistryTree(keyPath);
                return;
            }
            using RegistryKey key = OpenKey(keyPath, true, false);
            if (key != null) key.DeleteValue(valueName, false);
        }

        private static void ExecuteServiceStart(XElement command)
        {
            string name = Required(command, "Name");
            int startType = ParseInteger(Required(command, "Type"));
            using RegistryKey key = OpenKey(ServiceRegistryPath(name), true, false)
                ?? throw new InvalidOperationException("当前系统不存在服务：" + name);
            key.SetValue("Start", startType, RegistryValueKind.DWord);

            // Changing Start alone does not affect a service which is already
            // running. Disable operations must take effect immediately.
            if (startType == 4 && QueryServiceState(name) == 4)
                EnsureServiceRunning(name, false);
        }

        private static void ExecuteExplorerCommand(XElement command)
        {
            string type = command.Attribute("Type")?.Value ?? string.Empty;
            if (!string.Equals(type, "Cmd", StringComparison.OrdinalIgnoreCase))
            {
                NotifyShell();
                return;
            }
            string value = Required(command, "Cmd");
            ProcessResult result = RunCmd(value, 60000);
            if (!result.Success && !CommandReachedRequestedState(value))
                throw new IOException("命令执行失败：" + ResultMessage(result));
        }

        private static void ExecutePowerCfg(XElement command)
        {
            if (!string.Equals(command.Attribute("Type")?.Value, "setactive", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("不支持的 PowerCfg 类型。");
            ProcessResult result = ProcessRunner.Run("powercfg.exe", "/setactive " + Required(command, "Scheme"), 30000, CancellationToken.None);
            if (!result.Success) throw new IOException(ResultMessage(result));
        }

        private static void VerifyRegWrite(XElement command)
        {
            string keyPath = Required(command, "Key");
            string valueName = command.Attribute("Value")?.Value ?? string.Empty;
            RegistryValueKind expectedKind = ParseValueKind(command.Attribute("Type")?.Value);
            object expected = ParseConfiguredValue(command.Attribute("Data")?.Value, expectedKind);
            using RegistryKey key = OpenKey(keyPath, false, false) ?? throw new IOException("注册表键不存在：" + keyPath);
            if (!key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
                throw new IOException("注册表值不存在：" + keyPath + "\\" + valueName);
            object actual = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (!RegistryValuesEqual(actual, expected, expectedKind))
                throw new IOException("注册表值不符合预期：" + keyPath + "\\" + valueName);
        }

        private static void VerifyRegDelete(XElement command)
        {
            string keyPath = Required(command, "Key");
            string valueName = command.Attribute("Value")?.Value;
            if (string.IsNullOrEmpty(valueName))
            {
                using RegistryKey key = OpenKey(keyPath, false, false);
                if (key != null) throw new IOException("注册表键仍然存在：" + keyPath);
                return;
            }
            using RegistryKey valueKey = OpenKey(keyPath, false, false);
            if (valueKey != null && valueKey.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
                throw new IOException("注册表值仍然存在：" + keyPath + "\\" + valueName);
        }

        private static void VerifyServiceStart(XElement command)
        {
            string name = Required(command, "Name");
            int expected = ParseInteger(Required(command, "Type"));
            using RegistryKey key = OpenKey(ServiceRegistryPath(name), false, false)
                ?? throw new IOException("服务不存在：" + name);
            int actual = Convert.ToInt32(key.GetValue("Start", -1), CultureInfo.InvariantCulture);
            if (actual != expected) throw new IOException("服务启动类型验证失败：" + name);
        }

        private static void VerifyExplorerCommand(XElement command)
        {
            if (!string.Equals(command.Attribute("Type")?.Value, "Cmd", StringComparison.OrdinalIgnoreCase)) return;
            string value = Required(command, "Cmd");
            if (!CommandReachedRequestedState(value)) throw new IOException("命令状态验证失败：" + value);
        }

        private static void VerifyPowerCfg(XElement command)
        {
            string expected = Required(command, "Scheme").ToLowerInvariant();
            if (!string.Equals(ReadActivePowerPlan(), expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("活动电源计划未切换到 " + expected);
        }

        private static void CaptureExplorerCommand(XElement command, IList<OptimizationSnapshot> snapshots)
        {
            if (!string.Equals(command.Attribute("Type")?.Value, "Cmd", StringComparison.OrdinalIgnoreCase)) return;
            string value = Required(command, "Cmd").Trim();
            if (Regex.IsMatch(value, @"^powercfg(?:\.exe)?\s+-setactive\s+", RegexOptions.IgnoreCase)) snapshots.Add(CapturePowerPlan());
            else if (Regex.IsMatch(value, @"^powercfg(?:\.exe)?\s+-hibernate\s+(?:off|on)$", RegexOptions.IgnoreCase)) snapshots.Add(CaptureHibernate());
            else if (Regex.IsMatch(value, @"^net\s+stop\s+", RegexOptions.IgnoreCase))
                snapshots.Add(CaptureServiceState(Regex.Replace(value, @"^net\s+stop\s+", string.Empty, RegexOptions.IgnoreCase).Trim(' ', '"')));
            else if (Regex.IsMatch(value, @"^netsh\s+wfp\s+set\s+options\s+netevents=", RegexOptions.IgnoreCase)) snapshots.Add(CaptureWfpNetEvents());
            else throw new NotSupportedException("该命令没有可靠的状态快照实现：" + value);
        }

        private static OptimizationSnapshot CaptureRegistryValue(string keyPath, string valueName, bool requireKey = false)
        {
            using RegistryKey key = OpenKey(keyPath, false, false);
            if (key == null)
            {
                if (requireKey) throw new InvalidOperationException("当前系统不存在注册表键：" + keyPath);
                return new OptimizationSnapshot { Kind = "RegistryValue", Key = keyPath, ValueName = valueName, KeyExisted = false, Exists = false };
            }
            bool exists = key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase);
            if (!exists) return new OptimizationSnapshot { Kind = "RegistryValue", Key = keyPath, ValueName = valueName, KeyExisted = true, Exists = false };
            RegistryValueKind kind = key.GetValueKind(valueName);
            object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return new OptimizationSnapshot
            {
                Kind = "RegistryValue",
                Key = keyPath,
                ValueName = valueName,
                KeyExisted = true,
                Exists = true,
                ValueKind = kind.ToString(),
                Data = EncodeRegistryValue(value, kind)
            };
        }

        private static OptimizationSnapshot CaptureRegistryTree(string keyPath)
        {
            using RegistryKey key = OpenKey(keyPath, false, false);
            if (key == null) return new OptimizationSnapshot { Kind = "RegistryTree", Key = keyPath, Exists = false };
            RegistryTreeSnapshot tree = CaptureTree(key);
            return new OptimizationSnapshot { Kind = "RegistryTree", Key = keyPath, Exists = true, Data = JsonSerializer.Serialize(tree) };
        }

        private static OptimizationSnapshot CaptureServiceState(string serviceName)
        {
            int state = QueryServiceState(serviceName);
            if (state < 0) throw new InvalidOperationException("当前系统不存在服务：" + serviceName);
            return new OptimizationSnapshot { Kind = "ServiceState", Key = serviceName, Exists = true, Data = state == 4 ? "running" : "stopped" };
        }

        private static OptimizationSnapshot CapturePowerPlan()
        {
            string value = ReadActivePowerPlan();
            if (string.IsNullOrWhiteSpace(value)) throw new IOException("无法读取当前电源计划。");
            return new OptimizationSnapshot { Kind = "PowerPlan", Key = "active", Exists = true, Data = value };
        }

        private static OptimizationSnapshot CaptureHibernate()
        {
            using RegistryKey key = OpenKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power", false, false)
                ?? throw new IOException("无法读取休眠状态。");
            object value = key.GetValue("HibernateEnabled");
            if (value == null) throw new IOException("无法读取 HibernateEnabled。");
            return new OptimizationSnapshot { Kind = "Hibernate", Key = "HibernateEnabled", Exists = true, Data = Convert.ToInt32(value) == 0 ? "off" : "on" };
        }

        private static OptimizationSnapshot CaptureWfpNetEvents()
        {
            string value = ReadWfpNetEvents();
            return new OptimizationSnapshot { Kind = "WfpNetEvents", Key = "NETEVENTS", Exists = true, Data = value };
        }

        private static void CaptureSpecialState(string itemTag, IList<OptimizationSnapshot> snapshots)
        {
            if (string.Equals(itemTag, HypervisorItem, StringComparison.Ordinal)) snapshots.Add(CaptureHypervisorLaunchType());
            else if (string.Equals(itemTag, ReservedStorageItem, StringComparison.Ordinal)) snapshots.Add(CaptureReservedStorage());
        }

        private static OptimizationSnapshot CaptureHypervisorLaunchType()
        {
            ProcessResult result = ProcessRunner.Run("bcdedit.exe", "/enum {current}", 30000, CancellationToken.None);
            if (!result.Success) throw new IOException("无法读取 BCD：" + ResultMessage(result));
            Match match = Regex.Match(result.Output, @"(?im)^\s*hypervisorlaunchtype\s+(\S+)\s*$");
            return new OptimizationSnapshot { Kind = "BcdHypervisor", Key = "hypervisorlaunchtype", Exists = match.Success, Data = match.Success ? match.Groups[1].Value : string.Empty };
        }

        private static OptimizationSnapshot CaptureReservedStorage()
        {
            bool enabled = ReadReservedStorage();
            return new OptimizationSnapshot { Kind = "ReservedStorage", Key = "ReservedStorage", Exists = true, Data = enabled ? "enabled" : "disabled" };
        }

        private static void ExecuteSpecial(string itemTag, bool restore)
        {
            string fileName = null;
            string arguments = null;
            if (string.Equals(itemTag, HypervisorItem, StringComparison.Ordinal))
            {
                fileName = "bcdedit.exe";
                arguments = "/set {current} hypervisorlaunchtype " + (restore ? "auto" : "off");
            }
            else if (string.Equals(itemTag, ReservedStorageItem, StringComparison.Ordinal))
            {
                fileName = "dism.exe";
                arguments = "/Online /Set-ReservedStorageState /State:" + (restore ? "Enabled" : "Disabled");
            }
            if (fileName == null) return;
            ProcessResult result = ProcessRunner.Run(fileName, arguments, 180000, CancellationToken.None);
            if (!result.Success) throw new IOException(ResultMessage(result));
        }

        private static void VerifySpecial(string itemTag, bool restore)
        {
            if (string.Equals(itemTag, HypervisorItem, StringComparison.Ordinal))
            {
                OptimizationSnapshot value = CaptureHypervisorLaunchType();
                string expected = restore ? "auto" : "off";
                if (!value.Exists || !string.Equals(value.Data, expected, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("hypervisorlaunchtype 状态不符合预期。");
            }
            else if (string.Equals(itemTag, ReservedStorageItem, StringComparison.Ordinal) && ReadReservedStorage() == !restore)
                throw new IOException("保留存储状态不符合预期。");
        }

        private static void RestoreSnapshot(OptimizationSnapshot snapshot)
        {
            switch (snapshot.Kind)
            {
                case "RegistryValue": RestoreRegistryValue(snapshot); break;
                case "RegistryTree": RestoreRegistryTree(snapshot); break;
                case "ServiceState": EnsureServiceRunning(snapshot.Key, snapshot.Data == "running"); break;
                case "PowerPlan": RunRequired("powercfg.exe", "/setactive " + snapshot.Data, 30000); break;
                case "Hibernate": RunRequired("powercfg.exe", "-hibernate " + snapshot.Data, 30000); break;
                case "WfpNetEvents": RunRequired("netsh.exe", "wfp set options netevents=" + snapshot.Data, 30000); break;
                case "BcdHypervisor":
                    RunRequired("bcdedit.exe", snapshot.Exists
                        ? "/set {current} hypervisorlaunchtype " + snapshot.Data
                        : "/deletevalue {current} hypervisorlaunchtype", 30000);
                    break;
                case "ReservedStorage":
                    RunRequired("dism.exe", "/Online /Set-ReservedStorageState /State:" + (snapshot.Data == "enabled" ? "Enabled" : "Disabled"), 180000);
                    break;
                default: throw new NotSupportedException("未知快照类型：" + snapshot.Kind);
            }
        }

        private static void VerifySnapshot(OptimizationSnapshot snapshot)
        {
            switch (snapshot.Kind)
            {
                case "RegistryValue": VerifyRegistrySnapshot(snapshot); break;
                case "RegistryTree":
                    using (RegistryKey key = OpenKey(snapshot.Key, false, false))
                    {
                        if ((key != null) != snapshot.Exists) throw new IOException("注册表键存在状态不一致。");
                        if (key != null)
                        {
                            RegistryTreeSnapshot expectedTree = JsonSerializer.Deserialize<RegistryTreeSnapshot>(snapshot.Data)
                                ?? throw new InvalidDataException("注册表树快照无效。");
                            VerifyTree(key, expectedTree);
                        }
                    }
                    break;
                case "ServiceState":
                    if ((QueryServiceState(snapshot.Key) == 4) != (snapshot.Data == "running")) throw new IOException("服务运行状态不一致。");
                    break;
                case "PowerPlan":
                    if (!string.Equals(ReadActivePowerPlan(), snapshot.Data, StringComparison.OrdinalIgnoreCase)) throw new IOException("电源计划不一致。");
                    break;
                case "Hibernate":
                    if (CaptureHibernate().Data != snapshot.Data) throw new IOException("休眠状态不一致。");
                    break;
                case "WfpNetEvents":
                    if (!string.Equals(ReadWfpNetEvents(), snapshot.Data, StringComparison.OrdinalIgnoreCase)) throw new IOException("WFP NETEVENTS 状态不一致。");
                    break;
                case "BcdHypervisor":
                    OptimizationSnapshot bcd = CaptureHypervisorLaunchType();
                    if (bcd.Exists != snapshot.Exists || (snapshot.Exists && !string.Equals(bcd.Data, snapshot.Data, StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("BCD 状态不一致。");
                    break;
                case "ReservedStorage":
                    if ((ReadReservedStorage() ? "enabled" : "disabled") != snapshot.Data) throw new IOException("保留存储状态不一致。");
                    break;
            }
        }

        private static void RestoreRegistryValue(OptimizationSnapshot snapshot)
        {
            if (snapshot.Exists)
            {
                RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), snapshot.ValueKind);
                using RegistryKey key = OpenKey(snapshot.Key, true, true) ?? throw new IOException("无法还原注册表键：" + snapshot.Key);
                key.SetValue(snapshot.ValueName ?? string.Empty, DecodeRegistryValue(snapshot.Data, kind), kind);
                return;
            }

            using (RegistryKey key = OpenKey(snapshot.Key, true, false))
            {
                if (key != null) key.DeleteValue(snapshot.ValueName ?? string.Empty, false);
            }
            if (!snapshot.KeyExisted) DeleteRegistryKeyIfEmpty(snapshot.Key);
        }

        private static void RestoreRegistryTree(OptimizationSnapshot snapshot)
        {
            DeleteRegistryTree(snapshot.Key);
            if (!snapshot.Exists) return;
            RegistryTreeSnapshot tree = JsonSerializer.Deserialize<RegistryTreeSnapshot>(snapshot.Data)
                ?? throw new InvalidDataException("注册表树快照无效。");
            using RegistryKey key = OpenKey(snapshot.Key, true, true) ?? throw new IOException("无法创建注册表键：" + snapshot.Key);
            WriteTree(key, tree);
        }

        private static void VerifyRegistrySnapshot(OptimizationSnapshot snapshot)
        {
            using RegistryKey key = OpenKey(snapshot.Key, false, false);
            if (key == null)
            {
                if (snapshot.KeyExisted || snapshot.Exists) throw new IOException("注册表键不存在。");
                return;
            }
            bool exists = key.GetValueNames().Contains(snapshot.ValueName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            if (exists != snapshot.Exists) throw new IOException("注册表值存在状态不一致。");
            if (!snapshot.Exists) return;
            RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), snapshot.ValueKind);
            object expected = DecodeRegistryValue(snapshot.Data, kind);
            object actual = key.GetValue(snapshot.ValueName ?? string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (!RegistryValuesEqual(actual, expected, kind)) throw new IOException("注册表值与快照不一致。");
        }

        private static RegistryTreeSnapshot CaptureTree(RegistryKey key)
        {
            var tree = new RegistryTreeSnapshot();
            foreach (string name in key.GetValueNames())
            {
                RegistryValueKind kind = key.GetValueKind(name);
                tree.Values.Add(new RegistryTreeValueSnapshot
                {
                    Name = name,
                    Kind = kind.ToString(),
                    Data = EncodeRegistryValue(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), kind)
                });
            }
            foreach (string name in key.GetSubKeyNames())
            {
                using RegistryKey child = key.OpenSubKey(name, false);
                if (child != null) tree.SubKeys[name] = CaptureTree(child);
            }
            return tree;
        }

        private static void WriteTree(RegistryKey key, RegistryTreeSnapshot tree)
        {
            foreach (RegistryTreeValueSnapshot value in tree.Values)
            {
                RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), value.Kind);
                key.SetValue(value.Name ?? string.Empty, DecodeRegistryValue(value.Data, kind), kind);
            }
            foreach (KeyValuePair<string, RegistryTreeSnapshot> pair in tree.SubKeys)
            {
                using RegistryKey child = key.CreateSubKey(pair.Key, true);
                WriteTree(child, pair.Value);
            }
        }

        private static void VerifyTree(RegistryKey key, RegistryTreeSnapshot expected)
        {
            var expectedValues = expected.Values.ToDictionary(value => value.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            string[] actualValueNames = key.GetValueNames();
            if (actualValueNames.Length != expectedValues.Count || actualValueNames.Any(name => !expectedValues.ContainsKey(name)))
                throw new IOException("注册表树的值集合不一致。");
            foreach (string valueName in actualValueNames)
            {
                RegistryTreeValueSnapshot expectedValue = expectedValues[valueName];
                RegistryValueKind expectedKind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), expectedValue.Kind);
                if (key.GetValueKind(valueName) != expectedKind) throw new IOException("注册表值类型不一致：" + valueName);
                object actual = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                object expectedData = DecodeRegistryValue(expectedValue.Data, expectedKind);
                if (!RegistryValuesEqual(actual, expectedData, expectedKind)) throw new IOException("注册表值数据不一致：" + valueName);
            }

            string[] actualSubKeys = key.GetSubKeyNames();
            if (actualSubKeys.Length != expected.SubKeys.Count || actualSubKeys.Any(name => !expected.SubKeys.ContainsKey(name)))
                throw new IOException("注册表树的子键集合不一致。");
            foreach (string subKeyName in actualSubKeys)
            {
                using RegistryKey child = key.OpenSubKey(subKeyName, false) ?? throw new IOException("无法读取注册表子键：" + subKeyName);
                VerifyTree(child, expected.SubKeys[subKeyName]);
            }
        }

        private static RegistryKey OpenKey(string fullPath, bool writable, bool create)
        {
            RegistryPath path = ParseRegistryPath(fullPath);
            using RegistryKey root = RegistryKey.OpenBaseKey(path.Hive, NativeRegistryView);
            if (string.IsNullOrEmpty(path.SubKey)) return RegistryKey.OpenBaseKey(path.Hive, NativeRegistryView);
            return create ? root.CreateSubKey(path.SubKey, writable) : root.OpenSubKey(path.SubKey, writable);
        }

        private static void DeleteRegistryTree(string fullPath)
        {
            RegistryPath path = ParseRegistryPath(fullPath);
            int separator = path.SubKey.LastIndexOf('\\');
            string parentPath = separator < 0 ? string.Empty : path.SubKey.Substring(0, separator);
            string childName = separator < 0 ? path.SubKey : path.SubKey.Substring(separator + 1);
            using RegistryKey root = RegistryKey.OpenBaseKey(path.Hive, NativeRegistryView);
            using RegistryKey parent = string.IsNullOrEmpty(parentPath) ? RegistryKey.OpenBaseKey(path.Hive, NativeRegistryView) : root.OpenSubKey(parentPath, true);
            if (parent != null && !string.IsNullOrEmpty(childName)) parent.DeleteSubKeyTree(childName, false);
        }

        private static void DeleteRegistryKeyIfEmpty(string fullPath)
        {
            using (RegistryKey key = OpenKey(fullPath, false, false))
            {
                if (key == null || key.ValueCount > 0 || key.SubKeyCount > 0) return;
            }
            DeleteRegistryTree(fullPath);
        }

        private static RegistryPath ParseRegistryPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) throw new ArgumentException("注册表路径为空。");
            int separator = fullPath.IndexOf('\\');
            string rootName = separator < 0 ? fullPath : fullPath.Substring(0, separator);
            string subKey = separator < 0 ? string.Empty : fullPath.Substring(separator + 1);
            RegistryHive hive;
            switch (rootName.ToUpperInvariant())
            {
                case "HKEY_CURRENT_USER": case "HKCU": hive = RegistryHive.CurrentUser; break;
                case "HKEY_LOCAL_MACHINE": case "HKLM": hive = RegistryHive.LocalMachine; break;
                case "HKEY_CLASSES_ROOT": case "HKCR": hive = RegistryHive.ClassesRoot; break;
                case "HKEY_USERS": case "HKU": hive = RegistryHive.Users; break;
                case "HKEY_CURRENT_CONFIG": case "HKCC": hive = RegistryHive.CurrentConfig; break;
                default: throw new NotSupportedException("不支持的注册表根键：" + rootName);
            }
            if (hive == RegistryHive.Users && subKey.StartsWith("DEFAULT", StringComparison.OrdinalIgnoreCase))
                subKey = ".DEFAULT" + subKey.Substring("DEFAULT".Length);
            return new RegistryPath { Hive = hive, SubKey = subKey };
        }

        private static RegistryValueKind ParseValueKind(string value)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "REG_DWORD": case "DWORD": return RegistryValueKind.DWord;
                case "REG_QWORD": case "QWORD": return RegistryValueKind.QWord;
                case "REG_BINARY": case "BINARY": return RegistryValueKind.Binary;
                case "REG_MULTI_SZ": case "MULTISTRING": return RegistryValueKind.MultiString;
                case "REG_EXPAND_SZ": case "EXPANDSTRING": return RegistryValueKind.ExpandString;
                case "REG_SZ": case "STRING": case "": return RegistryValueKind.String;
                default: throw new NotSupportedException("不支持的注册表值类型：" + value);
            }
        }

        private static object ParseConfiguredValue(string data, RegistryValueKind kind)
        {
            string value = data ?? string.Empty;
            switch (kind)
            {
                case RegistryValueKind.DWord: return ParseInteger(value);
                case RegistryValueKind.QWord: return ParseLong(value);
                case RegistryValueKind.Binary:
                    string hex = Regex.Replace(value, "[^0-9a-fA-F]", string.Empty);
                    if (hex.Length % 2 != 0) throw new FormatException("二进制注册表数据长度无效。");
                    byte[] bytes = new byte[hex.Length / 2];
                    for (int index = 0; index < bytes.Length; index++) bytes[index] = byte.Parse(hex.Substring(index * 2, 2), NumberStyles.HexNumber);
                    return bytes;
                case RegistryValueKind.MultiString: return value.Split(new[] { "\\0" }, StringSplitOptions.None);
                default: return value;
            }
        }

        private static string EncodeRegistryValue(object value, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.Binary: return Convert.ToBase64String((byte[])value);
                case RegistryValueKind.MultiString: return JsonSerializer.Serialize((string[])value);
                case RegistryValueKind.DWord: return Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        private static object DecodeRegistryValue(string data, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.Binary: return Convert.FromBase64String(data ?? string.Empty);
                case RegistryValueKind.MultiString: return JsonSerializer.Deserialize<string[]>(data ?? "[]") ?? Array.Empty<string>();
                case RegistryValueKind.DWord: return int.Parse(data, CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return long.Parse(data, CultureInfo.InvariantCulture);
                default: return data ?? string.Empty;
            }
        }

        private static bool RegistryValuesEqual(object actual, object expected, RegistryValueKind kind)
        {
            if (kind == RegistryValueKind.Binary) return actual is byte[] leftBytes && expected is byte[] rightBytes && leftBytes.SequenceEqual(rightBytes);
            if (kind == RegistryValueKind.MultiString) return actual is string[] leftStrings && expected is string[] rightStrings && leftStrings.SequenceEqual(rightStrings);
            if (kind == RegistryValueKind.DWord) return Convert.ToInt32(actual, CultureInfo.InvariantCulture) == Convert.ToInt32(expected, CultureInfo.InvariantCulture);
            if (kind == RegistryValueKind.QWord) return Convert.ToInt64(actual, CultureInfo.InvariantCulture) == Convert.ToInt64(expected, CultureInfo.InvariantCulture);
            return string.Equals(Convert.ToString(actual, CultureInfo.InvariantCulture), Convert.ToString(expected, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        private static bool CommandReachedRequestedState(string command)
        {
            Match power = Regex.Match(command, @"^powercfg(?:\.exe)?\s+-setactive\s+([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase);
            if (power.Success) return string.Equals(ReadActivePowerPlan(), power.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            Match hibernate = Regex.Match(command, @"^powercfg(?:\.exe)?\s+-hibernate\s+(off|on)$", RegexOptions.IgnoreCase);
            if (hibernate.Success) return string.Equals(CaptureHibernate().Data, hibernate.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            Match stop = Regex.Match(command, @"^net\s+stop\s+(.+)$", RegexOptions.IgnoreCase);
            if (stop.Success) return QueryServiceState(stop.Groups[1].Value.Trim(' ', '"')) != 4;
            Match wfp = Regex.Match(command, @"^netsh\s+wfp\s+set\s+options\s+netevents=(on|off)$", RegexOptions.IgnoreCase);
            if (wfp.Success) return string.Equals(ReadWfpNetEvents(), wfp.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static string ReadActivePowerPlan()
        {
            ProcessResult result = ProcessRunner.Run("powercfg.exe", "/getactivescheme", 15000, CancellationToken.None);
            if (!result.Success) throw new IOException(ResultMessage(result));
            Match match = Regex.Match(result.Output, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            if (!match.Success) throw new InvalidDataException("无法解析当前电源计划。");
            return match.Value.ToLowerInvariant();
        }

        private static int QueryServiceState(string name)
        {
            ProcessResult result = ProcessRunner.Run("sc.exe", "query \"" + name + "\"", 15000, CancellationToken.None);
            if (!result.Success) return -1;
            Match match = Regex.Match(result.Output, @"(?im)(?:STATE|状态)\s*:\s*(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        private static void EnsureServiceRunning(string name, bool running)
        {
            int current = QueryServiceState(name);
            if (current < 0) throw new IOException("服务不存在：" + name);
            if ((current == 4) == running) return;
            ProcessResult result = ProcessRunner.Run("sc.exe", (running ? "start " : "stop ") + "\"" + name + "\"", 30000, CancellationToken.None);
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                int state = QueryServiceState(name);
                if ((state == 4) == running) return;
                Thread.Sleep(250);
            }
            throw new IOException("服务状态切换失败：" + name + "；" + ResultMessage(result));
        }

        private static string ReadWfpNetEvents()
        {
            ProcessResult result = ProcessRunner.Run("netsh.exe", "wfp show options optionsfor=NETEVENTS", 30000, CancellationToken.None);
            if (!result.Success) throw new IOException(ResultMessage(result));
            Match match = Regex.Match(result.Output, @"(?i)\b(ON|OFF)\b");
            if (!match.Success) throw new InvalidDataException("无法解析 WFP NETEVENTS 状态。");
            return match.Groups[1].Value.ToLowerInvariant();
        }

        private static bool ReadReservedStorage()
        {
            ProcessResult result = ProcessRunner.Run("dism.exe", "/Online /Get-ReservedStorageState", 120000, CancellationToken.None);
            if (!result.Success) throw new IOException(ResultMessage(result));
            if (Regex.IsMatch(result.Output, "(?i)disabled|已禁用|禁用")) return false;
            if (Regex.IsMatch(result.Output, "(?i)enabled|已启用|启用")) return true;
            throw new InvalidDataException("无法解析保留存储状态。");
        }

        private static void NotifyShell()
        {
            IntPtr ignored;
            Optimize.NativeMethods.SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "Environment", 0, 1000, out ignored);
        }

        private static ProcessResult RunCmd(string command, int timeout)
        {
            return ProcessRunner.Run("cmd.exe", "/D /C \"" + command + "\"", timeout, CancellationToken.None);
        }

        private static void RunRequired(string fileName, string arguments, int timeout)
        {
            ProcessResult result = ProcessRunner.Run(fileName, arguments, timeout, CancellationToken.None);
            if (!result.Success) throw new IOException(ResultMessage(result));
        }

        private static string Required(XElement command, string attribute)
        {
            string value = command.Attribute(attribute)?.Value;
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException(command.Name.LocalName + " 缺少 " + attribute + " 参数。");
            return value;
        }

        private static int ParseInteger(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) return result;
            if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint unsigned)) return unchecked((int)unsigned);
            string hexadecimal = value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
            if (int.TryParse(hexadecimal, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)) return result;
            if (uint.TryParse(hexadecimal, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out unsigned)) return unchecked((int)unsigned);
            throw new FormatException("无法解析整数：" + value);
        }

        private static long ParseLong(string value)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)) return result;
            if (long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)) return result;
            throw new FormatException("无法解析长整数：" + value);
        }

        private static string ServiceRegistryPath(string name)
        {
            return @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + name;
        }

        private static string ResultMessage(ProcessResult result)
        {
            if (result == null) return "没有进程结果。";
            if (result.TimedOut) return "命令执行超时。";
            return string.IsNullOrWhiteSpace(result.Error) ? (string.IsNullOrWhiteSpace(result.Output) ? "退出码 " + result.ExitCode : result.Output) : result.Error;
        }

        private static OptimizationExecutionResult Success(string message)
        {
            return new OptimizationExecutionResult { Success = true, Message = message };
        }

        private static OptimizationExecutionResult Failure(string message)
        {
            return new OptimizationExecutionResult { Success = false, Message = message };
        }

        private sealed class RegistryPath
        {
            public RegistryHive Hive { get; set; }
            public string SubKey { get; set; }
        }
    }
}
