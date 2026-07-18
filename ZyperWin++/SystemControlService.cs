using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    public sealed class SystemControlOperation
    {
        public string Id { get; set; }
        public string Group { get; set; }
        public string Name { get; set; }
        public string Risk { get; set; }
        public string CurrentState { get; set; }
        public string Description { get; set; }
        public string TargetState { get; set; }
        public bool CanApply { get; set; }
        public bool CanRestore { get; set; }
    }

    internal sealed class SystemControlSnapshot
    {
        public string Kind { get; set; }
        public string Key { get; set; }
        public string ValueName { get; set; }
        public bool Exists { get; set; }
        public bool KeyExisted { get; set; }
        public string ValueKind { get; set; }
        public string Data { get; set; }
    }

    internal sealed class SystemControlJournalRecord
    {
        public string Id { get; set; }
        public string OperationId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public List<SystemControlSnapshot> Snapshots { get; set; } = new List<SystemControlSnapshot>();
    }

    public sealed class SystemControlService
    {
        private const int InternetOptionRefresh = 37;
        private const int InternetOptionSettingsChanged = 39;
        private const string UpdatePolicy = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        private const string EdgePolicy = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge";
        private const string InternetSettings = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private static readonly object Sync = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly RegistryView NativeRegistryView = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
        private static readonly string JournalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDiskGlow",
            "advanced_control_backups.json");

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int bufferLength);

        public static int JournalCount
        {
            get { return ReadJournal().Count; }
        }

        public Task<IList<SystemControlOperation>> DetectAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IList<SystemControlOperation>>(() => Detect(cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> ApplyAsync(
            IEnumerable<SystemControlOperation> operations,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => Apply(operations, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RestoreAsync(
            IEnumerable<SystemControlOperation> operations,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => Restore(operations, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RestoreAllAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => RestoreRecords(ReadJournal().OrderByDescending(value => value.CreatedAtUtc).ToList(), cancellationToken), cancellationToken);
        }

        private static IList<SystemControlOperation> Detect(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IList<SystemControlJournalRecord> journal = ReadJournal();
            var operations = new List<SystemControlOperation>();

            int noAutoUpdate = ReadDword(UpdatePolicy, "NoAutoUpdate", 0);
            int auOptions = ReadDword(UpdatePolicy, "AUOptions", 0);
            Add(operations, journal, new SystemControlOperation
            {
                Id = "windows-update-notify",
                Group = "Windows Update",
                Name = "更新改为通知下载",
                Risk = "安全",
                CurrentState = noAutoUpdate == 0 && auOptions == 2 ? "已启用" : "自动更新策略未设置为通知",
                Description = "通过 Windows Update 官方组策略值改为下载前通知；还原时恢复原策略值。",
                CanApply = noAutoUpdate != 0 || auOptions != 2
            });

            cancellationToken.ThrowIfCancellationRequested();
            bool defenderDisabled;
            if (DefenderAvailable() && TryReadDefenderDisabled(out defenderDisabled))
            {
                Add(operations, journal, new SystemControlOperation
                {
                    Id = "defender-realtime",
                    Group = "Defender",
                    Name = defenderDisabled ? "启用 Defender 实时保护" : "临时关闭 Defender 实时保护",
                    Risk = defenderDisabled ? "安全" : "高风险",
                    CurrentState = defenderDisabled ? "实时保护已关闭" : "实时保护已开启",
                    Description = defenderDisabled
                        ? "调用 Set-MpPreference 启用实时保护；还原时恢复检测到的原状态。"
                        : "调用 Set-MpPreference 临时关闭实时保护；Tamper Protection 可能拒绝。建议仅排障时使用，可还原。",
                    CanApply = true,
                    TargetState = defenderDisabled ? "enabled" : "disabled"
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (EdgeInstalled())
            {
                bool edgeDisabled = ReadDword(EdgePolicy, "BackgroundModeEnabled", -1) == 0 &&
                    ReadDword(EdgePolicy, "StartupBoostEnabled", -1) == 0;
                Add(operations, journal, new SystemControlOperation
                {
                    Id = "edge-background-disable",
                    Group = "Microsoft Edge",
                    Name = "禁止 Edge 后台运行与启动增强",
                    Risk = "安全",
                    CurrentState = edgeDisabled ? "已禁止" : "允许后台运行",
                    Description = "写入 Microsoft Edge 官方策略 BackgroundModeEnabled 与 StartupBoostEnabled；可恢复原值。",
                    CanApply = !edgeDisabled
                });
            }

            bool proxyEnabled = ReadDword(InternetSettings, "ProxyEnable", 0) != 0;
            string proxyServer = ReadString(InternetSettings, "ProxyServer");
            string autoConfig = ReadString(InternetSettings, "AutoConfigURL");
            bool proxyConfigured = proxyEnabled || !string.IsNullOrWhiteSpace(proxyServer) || !string.IsNullOrWhiteSpace(autoConfig);
            Add(operations, journal, new SystemControlOperation
            {
                Id = "browser-proxy-reset",
                Group = "浏览器劫持修复",
                Name = "恢复系统代理为直连",
                Risk = "谨慎",
                CurrentState = proxyConfigured ? "检测到代理或 PAC" : "当前为直连",
                Description = "清除当前用户 WinINet 代理和 PAC 地址并广播设置变更；企业代理用户不要执行，可按快照还原。",
                CanApply = proxyConfigured
            });

            return operations;
        }

        private static void Add(
            ICollection<SystemControlOperation> operations,
            IEnumerable<SystemControlJournalRecord> journal,
            SystemControlOperation operation)
        {
            operation.CanRestore = journal.Any(value => string.Equals(value.OperationId, operation.Id, StringComparison.Ordinal));
            if (operation.CanRestore) operation.CanApply = false;
            operations.Add(operation);
        }

        private static FileOperationSummary Apply(IEnumerable<SystemControlOperation> operations, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            foreach (SystemControlOperation operation in (operations ?? Enumerable.Empty<SystemControlOperation>()).Where(value => value.CanApply))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ApplyOne(operation, result, cancellationToken);
            }
            return result;
        }

        private static void ApplyOne(SystemControlOperation operation, FileOperationSummary result, CancellationToken cancellationToken)
        {
            var record = new SystemControlJournalRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                OperationId = operation.Id,
                CreatedAtUtc = DateTime.UtcNow
            };
            try
            {
                Capture(operation.Id, record.Snapshots);
                AddJournal(record);
                Execute(operation, cancellationToken);
                Verify(operation);
                result.AffectedPaths.Add(operation.Name);
                OperationLogger.Info("高级系统管控", operation.Name + " 已执行并验证");
            }
            catch (OperationCanceledException)
            {
                try
                {
                    RestoreRecord(record, CancellationToken.None);
                    RemoveJournal(record.Id);
                    OperationLogger.Info("高级系统管控", operation.Name + " 已取消并恢复原状态");
                }
                catch (Exception rollbackException)
                {
                    OperationLogger.Error("高级系统管控", operation.Name + " 取消后的自动回滚失败，快照已保留：" + rollbackException.Message);
                    throw new IOException(operation.Name + " 已取消，但自动回滚失败；请使用全局一键还原：" + rollbackException.Message, rollbackException);
                }
                throw;
            }
            catch (Exception ex)
            {
                string rollbackError = null;
                try
                {
                    RestoreRecord(record, CancellationToken.None);
                    RemoveJournal(record.Id);
                }
                catch (Exception restoreException) { rollbackError = restoreException.Message; }
                result.Errors.Add(operation.Name + "：" + ex.Message +
                    (rollbackError == null ? "；已回滚。" : "；回滚失败：" + rollbackError));
                OperationLogger.Error("高级系统管控", result.Errors.Last());
            }
        }

        private static FileOperationSummary Restore(
            IEnumerable<SystemControlOperation> operations,
            CancellationToken cancellationToken)
        {
            IList<SystemControlJournalRecord> journal = ReadJournal();
            var records = new List<SystemControlJournalRecord>();
            foreach (SystemControlOperation operation in (operations ?? Enumerable.Empty<SystemControlOperation>()).Where(value => value.CanRestore))
            {
                SystemControlJournalRecord record = journal
                    .Where(value => string.Equals(value.OperationId, operation.Id, StringComparison.Ordinal))
                    .OrderByDescending(value => value.CreatedAtUtc)
                    .FirstOrDefault();
                if (record != null) records.Add(record);
            }
            return RestoreRecords(records.OrderByDescending(value => value.CreatedAtUtc).ToList(), cancellationToken);
        }

        private static FileOperationSummary RestoreRecords(
            IEnumerable<SystemControlJournalRecord> records,
            CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            foreach (SystemControlJournalRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    RestoreRecord(record, cancellationToken);
                    RemoveJournal(record.Id);
                    result.AffectedPaths.Add(record.OperationId);
                    OperationLogger.Info("高级系统管控还原", record.OperationId + " 已恢复原状态");
                }
                catch (Exception ex)
                {
                    result.Errors.Add(record.OperationId + "：" + ex.Message);
                    OperationLogger.Error("高级系统管控还原", result.Errors.Last());
                }
            }
            return result;
        }

        private static void Capture(string operationId, IList<SystemControlSnapshot> snapshots)
        {
            switch (operationId)
            {
                case "windows-update-notify":
                    snapshots.Add(CaptureRegistry(UpdatePolicy, "NoAutoUpdate"));
                    snapshots.Add(CaptureRegistry(UpdatePolicy, "AUOptions"));
                    break;
                case "defender-realtime":
                    bool disabled;
                    if (!TryReadDefenderDisabled(out disabled)) throw new InvalidOperationException("无法读取 Defender 实时保护状态。");
                    snapshots.Add(new SystemControlSnapshot { Kind = "Defender", Key = "DisableRealtimeMonitoring", Exists = true, Data = disabled ? "true" : "false" });
                    break;
                case "edge-background-disable":
                    snapshots.Add(CaptureRegistry(EdgePolicy, "BackgroundModeEnabled"));
                    snapshots.Add(CaptureRegistry(EdgePolicy, "StartupBoostEnabled"));
                    break;
                case "browser-proxy-reset":
                    snapshots.Add(CaptureRegistry(InternetSettings, "ProxyEnable"));
                    snapshots.Add(CaptureRegistry(InternetSettings, "ProxyServer"));
                    snapshots.Add(CaptureRegistry(InternetSettings, "AutoConfigURL"));
                    break;
                default:
                    throw new NotSupportedException("未知高级系统操作：" + operationId);
            }
        }

        private static void Execute(SystemControlOperation operation, CancellationToken cancellationToken)
        {
            switch (operation.Id)
            {
                case "windows-update-notify":
                    WriteDword(UpdatePolicy, "NoAutoUpdate", 0);
                    WriteDword(UpdatePolicy, "AUOptions", 2);
                    break;
                case "defender-realtime":
                    SetDefenderDisabled(string.Equals(operation.TargetState, "disabled", StringComparison.Ordinal), cancellationToken);
                    break;
                case "edge-background-disable":
                    WriteDword(EdgePolicy, "BackgroundModeEnabled", 0);
                    WriteDword(EdgePolicy, "StartupBoostEnabled", 0);
                    break;
                case "browser-proxy-reset":
                    WriteDword(InternetSettings, "ProxyEnable", 0);
                    DeleteValue(InternetSettings, "ProxyServer");
                    DeleteValue(InternetSettings, "AutoConfigURL");
                    NotifyInternetSettings(cancellationToken);
                    break;
                default:
                    throw new NotSupportedException("未知高级系统操作：" + operation.Id);
            }
        }

        private static void Verify(SystemControlOperation operation)
        {
            switch (operation.Id)
            {
                case "windows-update-notify":
                    if (ReadDword(UpdatePolicy, "NoAutoUpdate", -1) != 0 || ReadDword(UpdatePolicy, "AUOptions", -1) != 2)
                        throw new IOException("Windows Update 策略验证失败。");
                    break;
                case "defender-realtime":
                    bool disabled;
                    bool expectedDisabled = string.Equals(operation.TargetState, "disabled", StringComparison.Ordinal);
                    if (!TryReadDefenderDisabled(out disabled) || disabled != expectedDisabled)
                        throw new IOException("Defender 实时保护状态验证失败；Tamper Protection 可能阻止了修改。");
                    break;
                case "edge-background-disable":
                    if (ReadDword(EdgePolicy, "BackgroundModeEnabled", -1) != 0 || ReadDword(EdgePolicy, "StartupBoostEnabled", -1) != 0)
                        throw new IOException("Edge 后台策略验证失败。");
                    break;
                case "browser-proxy-reset":
                    if (ReadDword(InternetSettings, "ProxyEnable", -1) != 0 ||
                        !string.IsNullOrWhiteSpace(ReadString(InternetSettings, "ProxyServer")) ||
                        !string.IsNullOrWhiteSpace(ReadString(InternetSettings, "AutoConfigURL")))
                        throw new IOException("系统代理状态验证失败。");
                    break;
            }
        }

        private static void RestoreRecord(SystemControlJournalRecord record, CancellationToken cancellationToken)
        {
            foreach (SystemControlSnapshot snapshot in record.Snapshots.AsEnumerable().Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Kind == "Registry") RestoreRegistry(snapshot);
                else if (snapshot.Kind == "Defender") SetDefenderDisabled(snapshot.Data == "true", cancellationToken);
                else throw new InvalidDataException("未知高级系统快照：" + snapshot.Kind);
            }
            if (record.OperationId == "browser-proxy-reset") NotifyInternetSettings(cancellationToken);
            foreach (SystemControlSnapshot snapshot in record.Snapshots) VerifySnapshot(snapshot);
        }

        private static void VerifySnapshot(SystemControlSnapshot snapshot)
        {
            if (snapshot.Kind == "Defender")
            {
                bool disabled;
                if (!TryReadDefenderDisabled(out disabled) || disabled != (snapshot.Data == "true"))
                    throw new IOException("Defender 原状态还原验证失败。");
                return;
            }
            if (snapshot.Kind != "Registry") throw new InvalidDataException("未知高级系统快照：" + snapshot.Kind);
            using (RegistryKey key = OpenKey(snapshot.Key, false, false))
            {
                bool exists = key != null && key.GetValueNames().Contains(snapshot.ValueName, StringComparer.OrdinalIgnoreCase);
                if (exists != snapshot.Exists) throw new IOException("注册表存在状态还原验证失败：" + snapshot.Key + "\\" + snapshot.ValueName);
                if (!exists) return;
                RegistryValueKind kind = key.GetValueKind(snapshot.ValueName);
                string value = Convert.ToString(key.GetValue(snapshot.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames), CultureInfo.InvariantCulture);
                if (!string.Equals(kind.ToString(), snapshot.ValueKind, StringComparison.Ordinal) ||
                    !string.Equals(value, snapshot.Data ?? string.Empty, StringComparison.Ordinal))
                    throw new IOException("注册表值还原验证失败：" + snapshot.Key + "\\" + snapshot.ValueName);
            }
        }

        private static SystemControlSnapshot CaptureRegistry(string keyPath, string valueName)
        {
            using (RegistryKey key = OpenKey(keyPath, false, false))
            {
                if (key == null || !key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase))
                    return new SystemControlSnapshot
                    {
                        Kind = "Registry",
                        Key = keyPath,
                        ValueName = valueName,
                        Exists = false,
                        KeyExisted = key != null
                    };
                RegistryValueKind kind = key.GetValueKind(valueName);
                object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return new SystemControlSnapshot
                {
                    Kind = "Registry",
                    Key = keyPath,
                    ValueName = valueName,
                    Exists = true,
                    KeyExisted = true,
                    ValueKind = kind.ToString(),
                    Data = Convert.ToString(value, CultureInfo.InvariantCulture)
                };
            }
        }

        private static void RestoreRegistry(SystemControlSnapshot snapshot)
        {
            if (!snapshot.Exists)
            {
                DeleteValue(snapshot.Key, snapshot.ValueName);
                if (!snapshot.KeyExisted) DeleteKeyIfEmpty(snapshot.Key);
                return;
            }
            RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), snapshot.ValueKind);
            object value = kind == RegistryValueKind.DWord
                ? (object)int.Parse(snapshot.Data, CultureInfo.InvariantCulture)
                : snapshot.Data ?? string.Empty;
            using (RegistryKey key = OpenKey(snapshot.Key, true, true) ?? throw new IOException("无法还原注册表键：" + snapshot.Key))
                key.SetValue(snapshot.ValueName, value, kind);
        }

        private static bool DefenderAvailable()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WinDefend", false))
                return key != null;
        }

        private static bool TryReadDefenderDisabled(out bool disabled)
        {
            disabled = false;
            try
            {
                ProcessResult result = ProcessRunner.RunPowerShellAsync(
                    "$v=(Get-MpPreference).DisableRealtimeMonitoring; if($v){'true'}else{'false'}",
                    30000,
                    CancellationToken.None).GetAwaiter().GetResult();
                if (!result.Success) return false;
                Match match = Regex.Match(result.Output ?? string.Empty, @"(?im)^\s*(true|false)\s*$");
                if (!match.Success) return false;
                disabled = string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
                return true;
            }
            catch { return false; }
        }

        private static void SetDefenderDisabled(bool disabled, CancellationToken cancellationToken)
        {
            ProcessResult result = ProcessRunner.RunPowerShellAsync(
                "Set-MpPreference -DisableRealtimeMonitoring $" + (disabled ? "true" : "false"),
                60000,
                cancellationToken).GetAwaiter().GetResult();
            if (!result.Success) throw new IOException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        }

        private static bool EdgeInstalled()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (string.IsNullOrWhiteSpace(programFiles)) programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Directory.Exists(Path.Combine(programFiles, "Microsoft", "Edge", "Application"));
        }

        private static void NotifyInternetSettings(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0) ||
                !InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0))
                throw new IOException("代理已写入，但无法通知 WinINet 刷新设置。");
        }

        private static int ReadDword(string keyPath, string valueName, int fallback)
        {
            using (RegistryKey key = OpenKey(keyPath, false, false))
            {
                if (key == null) return fallback;
                try { return Convert.ToInt32(key.GetValue(valueName, fallback), CultureInfo.InvariantCulture); }
                catch { return fallback; }
            }
        }

        private static string ReadString(string keyPath, string valueName)
        {
            using (RegistryKey key = OpenKey(keyPath, false, false))
                return key == null ? string.Empty : Convert.ToString(key.GetValue(valueName), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static void WriteDword(string keyPath, string valueName, int value)
        {
            using (RegistryKey key = OpenKey(keyPath, true, true) ?? throw new IOException("无法写入注册表键：" + keyPath))
                key.SetValue(valueName, value, RegistryValueKind.DWord);
        }

        private static void DeleteValue(string keyPath, string valueName)
        {
            using (RegistryKey key = OpenKey(keyPath, true, false))
                if (key != null) key.DeleteValue(valueName, false);
        }

        private static RegistryKey OpenKey(string fullPath, bool writable, bool create)
        {
            RegistryHive hive;
            string path;
            ParseRegistryPath(fullPath, out hive, out path);
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, NativeRegistryView))
                return create ? baseKey.CreateSubKey(path, true) : baseKey.OpenSubKey(path, writable);
        }

        private static void DeleteKeyIfEmpty(string fullPath)
        {
            RegistryHive hive;
            string path;
            ParseRegistryPath(fullPath, out hive, out path);
            int separator = path.LastIndexOf('\\');
            if (separator < 0) return;
            string parentPath = path.Substring(0, separator);
            string keyName = path.Substring(separator + 1);
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, NativeRegistryView))
            using (RegistryKey key = baseKey.OpenSubKey(path, false))
            {
                if (key == null || key.ValueCount != 0 || key.SubKeyCount != 0) return;
            }
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, NativeRegistryView))
            using (RegistryKey parent = baseKey.OpenSubKey(parentPath, true))
                if (parent != null) parent.DeleteSubKey(keyName, false);
        }

        private static void ParseRegistryPath(string fullPath, out RegistryHive hive, out string path)
        {
            if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.LocalMachine;
                path = fullPath.Substring("HKEY_LOCAL_MACHINE\\".Length);
            }
            else if (fullPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.CurrentUser;
                path = fullPath.Substring("HKEY_CURRENT_USER\\".Length);
            }
            else throw new ArgumentException("不支持的注册表路径：" + fullPath);
        }

        private static IList<SystemControlJournalRecord> ReadJournal()
        {
            lock (Sync)
            {
                if (!File.Exists(JournalPath)) return new List<SystemControlJournalRecord>();
                try
                {
                    return JsonSerializer.Deserialize<List<SystemControlJournalRecord>>(
                        File.ReadAllText(JournalPath, Encoding.UTF8), JsonOptions) ?? new List<SystemControlJournalRecord>();
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("高级系统管控还原记录已损坏，未执行修改：" + ex.Message, ex);
                }
            }
        }

        private static void AddJournal(SystemControlJournalRecord record)
        {
            IList<SystemControlJournalRecord> records = ReadJournal();
            records.Add(record);
            WriteJournal(records);
        }

        private static void RemoveJournal(string id)
        {
            WriteJournal(ReadJournal().Where(value => !string.Equals(value.Id, id, StringComparison.Ordinal)).ToList());
        }

        private static void WriteJournal(IList<SystemControlJournalRecord> records)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(JournalPath));
                string temporary = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions), new UTF8Encoding(false));
                    FileSystemTools.ReplaceFile(temporary, JournalPath);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporary) && File.Exists(JournalPath)) File.Delete(temporary);
                        else if (File.Exists(temporary))
                            OperationLogger.Error("高级系统管控", "日志替换失败，恢复副本已保留：" + temporary);
                    }
                    catch { }
                }
            }
        }
    }
}
