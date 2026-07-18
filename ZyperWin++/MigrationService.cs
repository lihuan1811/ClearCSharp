using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    public sealed class MigrationLocation
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string TargetName { get; set; }
        public string SourcePath { get; set; }
    }

    public sealed class MigrationFolder
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string TargetName { get; set; }
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public long Size { get; set; }
        public bool Exists { get; set; }
        public bool Migrated { get; set; }
        public bool Partial { get; set; }
        public List<MigrationLocation> Locations { get; set; }
    }

    internal sealed class MigrationRecord
    {
        public string Key { get; set; }
        public string LocationKey { get; set; }
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class MigrationService
    {
        private const int HwndBroadcast = 0xffff;
        private const int WmSettingChange = 0x001a;
        private const int SmtoAbortIfHung = 0x0002;
        private static readonly object Sync = new object();
        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDiskGlow",
            "migration_records.tsv");

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint message,
            UIntPtr wParam,
            string lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);

        public Task<IList<MigrationFolder>> ScanAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IList<MigrationFolder>>(() => Scan(cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> MigrateAsync(string key, string targetRoot, CancellationToken cancellationToken)
        {
            return Task.Run(() => Migrate(key, targetRoot, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RestoreAsync(string key, CancellationToken cancellationToken)
        {
            return Task.Run(() => Restore(key, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RestoreAllAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var combined = new FileOperationSummary();
                foreach (string key in ReadRecords().Select(record => record.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileOperationSummary result = Restore(key, cancellationToken);
                    foreach (string path in result.AffectedPaths) combined.AffectedPaths.Add(path);
                    foreach (string error in result.Errors) combined.Errors.Add(error);
                }
                return combined;
            }, cancellationToken);
        }

        public static int MigratedRecordCount()
        {
            return ReadRecords().Select(record => record.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }

        public static IList<MigrationFolder> Catalog()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.Combine(home, "AppData", "Local");
            string roaming = Environment.GetEnvironmentVariable("APPDATA") ?? Path.Combine(home, "AppData", "Roaming");
            return new List<MigrationFolder>
            {
                Folder("desktop", "桌面", "Desktop", KnownFolderPaths.Desktop),
                Folder("documents", "我的文档", "Documents", KnownFolderPaths.Documents),
                Folder("downloads", "下载", "Downloads", KnownFolderPaths.Downloads),
                Folder("pictures", "我的图片", "Pictures", KnownFolderPaths.Pictures),
                Folder("videos", "我的视频", "Videos", KnownFolderPaths.Videos),
                CompositeFolder("appdata_cache", "应用数据与聊天文件（微信/QQ）", "AppData-WeChat-QQ",
                    Location("local-tencent", "Local Tencent", "Local-Tencent", Path.Combine(local, "Tencent")),
                    Location("roaming-tencent", "Roaming Tencent", "Roaming-Tencent", Path.Combine(roaming, "Tencent")),
                    Location("wechat-files", "WeChat Files", "Documents-WeChat-Files", Path.Combine(KnownFolderPaths.Documents, "WeChat Files")),
                    Location("xwechat-files", "xwechat_files", "Documents-xwechat-files", Path.Combine(KnownFolderPaths.Documents, "xwechat_files")),
                    Location("tencent-files", "Tencent Files", "Documents-Tencent-Files", Path.Combine(KnownFolderPaths.Documents, "Tencent Files"))),
                Folder("temp", "当前用户 Temp 临时文件夹", "User-Temp", Path.Combine(local, "Temp"))
            };
        }

        private static IList<MigrationFolder> Scan(CancellationToken cancellationToken)
        {
            IList<MigrationRecord> records = ReadRecords();
            IList<MigrationFolder> folders = Catalog();
            foreach (MigrationFolder folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IList<MigrationRecord> folderRecords = records.Where(record => string.Equals(record.Key, folder.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                IList<MigrationLocation> locations = EligibleLocations(folder, true).ToList();
                folder.Exists = locations.Any(location => Directory.Exists(location.SourcePath));
                int applicable = 0;
                int migrated = 0;
                long size = 0;
                var targets = new List<string>();
                foreach (MigrationLocation location in locations)
                {
                    MigrationRecord record = FindRecord(folderRecords, location);
                    bool exists = Directory.Exists(location.SourcePath);
                    if (!exists && record == null) continue;
                    applicable++;
                    bool isMigrated = record != null && IsReparsePoint(record.SourcePath) && Directory.Exists(record.TargetPath);
                    if (isMigrated)
                    {
                        migrated++;
                        targets.Add(record.TargetPath);
                    }
                    string measuredPath = isMigrated ? record.TargetPath : location.SourcePath;
                    if (Directory.Exists(measuredPath)) size += DirectorySize(measuredPath, cancellationToken);
                }
                folder.Migrated = applicable > 0 && migrated == applicable;
                folder.Partial = migrated > 0 && migrated < applicable;
                folder.TargetPath = string.Join("；", targets);
                folder.Size = size;
            }
            return folders;
        }

        private static FileOperationSummary Migrate(string key, string targetRoot, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            MigrationFolder folder = Catalog().FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (folder == null)
            {
                result.Errors.Add("未知的系统目录：" + key);
                return result;
            }
            if (ReadRecords().Any(record => string.Equals(record.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                result.Errors.Add("该目录已经迁移：" + folder.Name);
                return result;
            }

            string validationError;
            string targetRootPath;
            IList<MigrationLocation> locations = EligibleLocations(folder, folder.Locations.Count == 1)
                .Where(location => folder.Locations.Count == 1 || Directory.Exists(location.SourcePath))
                .ToList();
            if (locations.Count == 0)
            {
                result.Errors.Add("没有找到位于 C 盘且可迁移的目录：" + folder.Name);
                return result;
            }
            if (!ValidateTargetRoot(targetRoot, out targetRootPath, out validationError))
            {
                result.Errors.Add(validationError);
                return result;
            }
            var pending = new List<MigrationRecord>();
            foreach (MigrationLocation location in locations)
            {
                string target = TargetPath(folder, location, targetRootPath);
                if (!ValidateLocationTarget(location, target, out validationError))
                {
                    result.Errors.Add(validationError);
                    return result;
                }
                pending.Add(new MigrationRecord
                {
                    Key = folder.Key,
                    LocationKey = location.Key,
                    SourcePath = location.SourcePath,
                    TargetPath = target,
                    CreatedAt = DateTime.UtcNow
                });
            }
            EnsureCombinedFreeSpace(pending.Select(record => record.SourcePath), targetRootPath, "迁移目标磁盘", cancellationToken);

            try
            {
                foreach (MigrationRecord record in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(record.TargetPath);
                    if (Directory.Exists(record.SourcePath))
                    {
                        if (IsReparsePoint(record.SourcePath)) throw new IOException("原路径已经是目录连接：" + record.SourcePath);
                        MoveDirectoryContents(record.SourcePath, record.TargetPath, cancellationToken);
                        Directory.Delete(record.SourcePath, false);
                    }
                    else Directory.CreateDirectory(Path.GetDirectoryName(record.SourcePath));

                    FileOperationSummary junction = CreateJunction(record.SourcePath, record.TargetPath, cancellationToken);
                    if (!junction.Success) throw new IOException(string.Join(Environment.NewLine, junction.Errors));
                }

                if (folder.Locations.Count == 1 && !UpdateRedirect(folder, pending[0].TargetPath, out validationError))
                    throw new IOException(validationError);

                IList<MigrationRecord> records = ReadRecords();
                foreach (MigrationRecord record in pending)
                {
                    records.Add(record);
                    result.AffectedPaths.Add(record.SourcePath);
                    result.AffectedPaths.Add(record.TargetPath);
                    OperationLogger.Info("系统目录迁移", folder.Name + " / " + record.LocationKey + " -> " + record.TargetPath);
                }
                WriteRecords(records);
            }
            catch (Exception ex)
            {
                foreach (MigrationRecord record in pending.AsEnumerable().Reverse())
                {
                    try { RollbackMigration(record); }
                    catch (Exception rollbackError) { result.Errors.Add("迁移失败且回滚未完整完成：" + rollbackError.Message); }
                }
                if (folder.Locations.Count == 1) UpdateRedirect(folder, pending[0].SourcePath, out validationError);
                result.Errors.Add("迁移失败：" + ex.Message);
                OperationLogger.Error("系统目录迁移", folder.Name + "：" + ex.Message);
            }
            return result;
        }

        private static FileOperationSummary Restore(string key, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            IList<MigrationRecord> allRecords = ReadRecords();
            IList<MigrationRecord> records = allRecords.Where(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)).ToList();
            MigrationFolder folder = Catalog().FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (records.Count == 0 || folder == null)
            {
                result.Errors.Add("该目录没有可还原的迁移记录。");
                return result;
            }

            try
            {
                EnsureCombinedFreeSpace(records.Select(record => record.TargetPath), records[0].SourcePath, "C盘原目录", cancellationToken);
            }
            catch (Exception ex)
            {
                result.Errors.Add("还原前检查失败：" + ex.Message);
                return result;
            }

            var restored = new List<MigrationRecord>();
            foreach (MigrationRecord record in records.AsEnumerable().Reverse())
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsReparsePoint(record.SourcePath)) throw new IOException("原路径已不是本程序创建的目录连接：" + record.SourcePath);
                    FileOperationSummary removed = RemoveJunction(record.SourcePath, cancellationToken);
                    if (!removed.Success) throw new IOException(string.Join(Environment.NewLine, removed.Errors));
                    Directory.CreateDirectory(record.SourcePath);
                    if (Directory.Exists(record.TargetPath)) MoveDirectoryContents(record.TargetPath, record.SourcePath, cancellationToken);
                    if (folder.Locations.Count == 1)
                    {
                        string updateError;
                        if (!UpdateRedirect(folder, record.SourcePath, out updateError)) throw new IOException(updateError);
                    }
                    if (Directory.Exists(record.TargetPath) && !Directory.EnumerateFileSystemEntries(record.TargetPath).Any())
                        Directory.Delete(record.TargetPath, false);
                    restored.Add(record);
                    result.AffectedPaths.Add(record.SourcePath);
                    OperationLogger.Info("还原迁移目录", folder.Name + " / " + record.LocationKey + " -> " + record.SourcePath);
                }
                catch (Exception ex)
                {
                    try
                    {
                        RestoreMigratedState(record);
                        if (folder.Locations.Count == 1)
                        {
                            string ignored;
                            UpdateRedirect(folder, record.TargetPath, out ignored);
                        }
                    }
                    catch { }
                    result.Errors.Add("还原失败，已尝试恢复迁移状态：" + ex.Message);
                    OperationLogger.Error("还原迁移目录", folder.Name + "：" + ex.Message);
                }
            }

            WriteRecords(allRecords.Where(record => !restored.Contains(record)).ToList());
            return result;
        }

        private static bool ValidateTargetRoot(string targetRoot, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(targetRoot)) throw new IOException("请选择迁移目标目录。");
                normalized = Path.GetFullPath(targetRoot);
                string targetRootOnly = Path.GetPathRoot(normalized);
                if (!string.Equals(normalized, targetRootOnly, StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.TrimEnd(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(normalized);
                var drive = new DriveInfo(Path.GetPathRoot(normalized));
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) throw new IOException("迁移目标必须是可用的本地固定磁盘。");
                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("目标磁盘为 " + drive.DriveFormat + " 格式，不支持连接点。请选择 NTFS 磁盘。");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool ValidateLocationTarget(MigrationLocation location, string target, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.Equals(Path.GetPathRoot(target), Path.GetPathRoot(location.SourcePath), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("迁移目标必须位于其它磁盘：" + location.Name);
                if (IsSameOrChild(target, location.SourcePath)) throw new IOException("迁移目标不能位于原目录内部。");
                if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                    throw new IOException("迁移目标已存在内容，为避免混入旧文件和破坏回滚，未执行迁移：" + target);
                if (File.Exists(target) || IsReparsePoint(target)) throw new IOException("迁移目标不是可用的普通目录：" + target);
                string executable = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executable) && IsSameOrChild(executable, location.SourcePath))
                    throw new IOException("本程序位于待迁移目录内，请先把程序移动到其它位置。");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static FileOperationSummary CreateJunction(string source, string target, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            ProcessResult command = ProcessRunner.Run("cmd.exe", "/D /C mklink /J " + Quote(source) + " " + Quote(target), 60000, cancellationToken);
            if (command.Success && IsReparsePoint(source)) result.AffectedPaths.Add(source);
            else result.Errors.Add("创建目录连接失败：" + (string.IsNullOrWhiteSpace(command.Error) ? command.Output : command.Error));
            return result;
        }

        private static FileOperationSummary RemoveJunction(string source, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            ProcessResult command = ProcessRunner.Run("cmd.exe", "/D /C rmdir " + Quote(source), 60000, cancellationToken);
            if (command.Success && !Directory.Exists(source)) result.AffectedPaths.Add(source);
            else result.Errors.Add("删除目录连接失败：" + (string.IsNullOrWhiteSpace(command.Error) ? command.Output : command.Error));
            return result;
        }

        private static bool UpdateRedirect(MigrationFolder folder, string path, out string error)
        {
            error = string.Empty;
            try
            {
                if (folder.Key == "temp")
                {
                    using (RegistryKey environment = Registry.CurrentUser.CreateSubKey("Environment"))
                    {
                        environment.SetValue("TEMP", path, RegistryValueKind.ExpandString);
                        environment.SetValue("TMP", path, RegistryValueKind.ExpandString);
                    }
                }
                else
                {
                    string valueName = PersonalFolderRegistryValue(folder.Key);
                    if (!string.IsNullOrWhiteSpace(valueName))
                    {
                        using (RegistryKey userShell = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                        using (RegistryKey shell = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"))
                        {
                            userShell.SetValue(valueName, path, RegistryValueKind.ExpandString);
                            shell.SetValue(valueName, path, RegistryValueKind.String);
                        }
                    }
                }
                string settingArea = folder.Key == "temp"
                    ? "Environment"
                    : @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
                UIntPtr ignored;
                SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, UIntPtr.Zero, settingArea, SmtoAbortIfHung, 3000, out ignored);
                return true;
            }
            catch (Exception ex)
            {
                error = "更新系统目录注册表或环境变量失败：" + ex.Message;
                return false;
            }
        }

        private static string PersonalFolderRegistryValue(string key)
        {
            switch (key)
            {
                case "desktop": return "Desktop";
                case "documents": return "Personal";
                case "downloads": return "{374DE290-123F-4565-9164-39C4925E467B}";
                case "pictures": return "My Pictures";
                case "videos": return "My Video";
                default: return string.Empty;
            }
        }

        private static void MoveDirectoryContents(string source, string target, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(target);
            foreach (string directory in Directory.GetDirectories(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(directory)) throw new IOException("目录中包含连接点，无法安全迁移：" + directory);
                string destination = Path.Combine(target, Path.GetFileName(directory));
                MoveDirectoryContents(directory, destination, cancellationToken);
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
            }
            foreach (string file in Directory.GetFiles(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(target, Path.GetFileName(file));
                if (File.Exists(destination)) throw new IOException("目标文件已存在：" + destination);
                MoveFileAcrossVolumes(file, destination);
            }
        }

        private static void MoveFileAcrossVolumes(string source, string destination)
        {
            try { File.Move(source, destination); }
            catch (IOException)
            {
                File.Copy(source, destination, false);
                try { File.Delete(source); }
                catch
                {
                    File.Delete(destination);
                    throw;
                }
            }
        }

        private static void EnsureCombinedFreeSpace(IEnumerable<string> sources, string destination, string destinationLabel, CancellationToken cancellationToken)
        {
            long required = 0;
            foreach (string source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(source)) required += DirectorySize(source, cancellationToken);
            }
            string root = Path.GetPathRoot(Path.GetFullPath(destination));
            var drive = new DriveInfo(root);
            if (!drive.IsReady) throw new IOException(destinationLabel + "不可用。");
            if (required > drive.AvailableFreeSpace)
            {
                throw new IOException(string.Format("{0}空间不足：需要 {1}，当前可用 {2}。",
                    destinationLabel, DisplayFormat.Bytes(required), DisplayFormat.Bytes(drive.AvailableFreeSpace)));
            }
        }

        internal static void RunJunctionRoundTripSmokeTest(CancellationToken cancellationToken)
        {
            string token = Guid.NewGuid().ToString("N");
            string source = Path.Combine(Path.GetTempPath(), "CDiskGlowMigrationSource_" + token);
            string target = Path.Combine(Path.GetTempPath(), "CDiskGlowMigrationTarget_" + token);
            try
            {
                Directory.CreateDirectory(Path.Combine(source, "nested"));
                File.WriteAllText(Path.Combine(source, "nested", "probe.txt"), "migration-round-trip", Encoding.UTF8);
                MoveDirectoryContents(source, target, cancellationToken);
                Directory.Delete(source, false);

                FileOperationSummary created = CreateJunction(source, target, cancellationToken);
                if (!created.Success || !File.Exists(Path.Combine(source, "nested", "probe.txt")))
                    throw new IOException("迁移连接点创建后无法读取目标文件。");

                FileOperationSummary removed = RemoveJunction(source, cancellationToken);
                if (!removed.Success) throw new IOException(string.Join(Environment.NewLine, removed.Errors));
                Directory.CreateDirectory(source);
                MoveDirectoryContents(target, source, cancellationToken);
                if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target, false);
                if (File.ReadAllText(Path.Combine(source, "nested", "probe.txt"), Encoding.UTF8) != "migration-round-trip")
                    throw new IOException("迁移还原后的文件内容不一致。");
            }
            finally
            {
                if (IsReparsePoint(source)) RemoveJunction(source, CancellationToken.None);
                try { if (Directory.Exists(source)) Directory.Delete(source, true); } catch { }
                try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { }
            }
        }

        private static long DirectorySize(string path, CancellationToken cancellationToken)
        {
            long total = 0;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                try
                {
                    foreach (string directory in Directory.EnumerateDirectories(current)) if (!IsReparsePoint(directory)) pending.Push(directory);
                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        try { total += new FileInfo(file).Length; }
                        catch { }
                    }
                }
                catch
                {
                }
            }
            return total;
        }

        private static bool IsReparsePoint(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return false; }
        }

        private static bool IsSameOrChild(string path, string root)
        {
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static MigrationFolder Folder(string key, string name, string targetName, string sourcePath)
        {
            MigrationLocation location = Location("primary", name, targetName, sourcePath);
            return new MigrationFolder
            {
                Key = key,
                Name = name,
                TargetName = targetName,
                SourcePath = sourcePath,
                Locations = new List<MigrationLocation> { location }
            };
        }

        private static MigrationFolder CompositeFolder(string key, string name, string targetName, params MigrationLocation[] locations)
        {
            return new MigrationFolder
            {
                Key = key,
                Name = name,
                TargetName = targetName,
                SourcePath = string.Join("；", locations.Select(location => location.SourcePath)),
                Locations = locations.ToList()
            };
        }

        private static MigrationLocation Location(string key, string name, string targetName, string sourcePath)
        {
            return new MigrationLocation { Key = key, Name = name, TargetName = targetName, SourcePath = sourcePath };
        }

        private static IEnumerable<MigrationLocation> EligibleLocations(MigrationFolder folder, bool includeMissingSingle)
        {
            string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            foreach (MigrationLocation location in folder.Locations ?? new List<MigrationLocation>())
            {
                string sourceRoot;
                try { sourceRoot = Path.GetPathRoot(Path.GetFullPath(location.SourcePath)); }
                catch { continue; }
                if (!string.Equals(sourceRoot, systemRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (includeMissingSingle || Directory.Exists(location.SourcePath) || IsReparsePoint(location.SourcePath)) yield return location;
            }
        }

        private static string TargetPath(MigrationFolder folder, MigrationLocation location, string targetRoot)
        {
            return folder.Locations.Count == 1
                ? Path.Combine(targetRoot, folder.TargetName)
                : Path.Combine(targetRoot, folder.TargetName, location.TargetName);
        }

        private static MigrationRecord FindRecord(IEnumerable<MigrationRecord> records, MigrationLocation location)
        {
            return records.FirstOrDefault(record =>
                string.Equals(record.LocationKey, location.Key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.SourcePath, location.SourcePath, StringComparison.OrdinalIgnoreCase));
        }

        private static void RollbackMigration(MigrationRecord record)
        {
            if (IsReparsePoint(record.SourcePath))
            {
                FileOperationSummary removed = RemoveJunction(record.SourcePath, CancellationToken.None);
                if (!removed.Success) throw new IOException(string.Join(Environment.NewLine, removed.Errors));
            }
            Directory.CreateDirectory(record.SourcePath);
            if (Directory.Exists(record.TargetPath)) MoveDirectoryContents(record.TargetPath, record.SourcePath, CancellationToken.None);
            if (Directory.Exists(record.TargetPath) && !Directory.EnumerateFileSystemEntries(record.TargetPath).Any())
                Directory.Delete(record.TargetPath, false);
        }

        private static void RestoreMigratedState(MigrationRecord record)
        {
            if (IsReparsePoint(record.SourcePath)) return;
            Directory.CreateDirectory(record.TargetPath);
            if (Directory.Exists(record.SourcePath))
            {
                MoveDirectoryContents(record.SourcePath, record.TargetPath, CancellationToken.None);
                if (!Directory.EnumerateFileSystemEntries(record.SourcePath).Any()) Directory.Delete(record.SourcePath, false);
            }
            FileOperationSummary created = CreateJunction(record.SourcePath, record.TargetPath, CancellationToken.None);
            if (!created.Success) throw new IOException(string.Join(Environment.NewLine, created.Errors));
        }

        private static IList<MigrationRecord> ReadRecords()
        {
            lock (Sync)
            {
                var records = new List<MigrationRecord>();
                if (!File.Exists(StatePath)) return records;
                foreach (string line in File.ReadAllLines(StatePath, Encoding.UTF8))
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length != 4 && fields.Length != 5) continue;
                    try
                    {
                        records.Add(new MigrationRecord
                        {
                            Key = fields[0],
                            LocationKey = fields.Length == 5 ? fields[1] : "primary",
                            SourcePath = Decode(fields.Length == 5 ? fields[2] : fields[1]),
                            TargetPath = Decode(fields.Length == 5 ? fields[3] : fields[2]),
                            CreatedAt = DateTime.Parse(fields.Length == 5 ? fields[4] : fields[3]).ToUniversalTime()
                        });
                    }
                    catch
                    {
                    }
                }
                return records;
            }
        }

        private static void WriteRecords(IList<MigrationRecord> records)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
                string[] lines = records.Select(record => string.Join("\t", record.Key, record.LocationKey ?? "primary",
                    Encode(record.SourcePath), Encode(record.TargetPath), record.CreatedAt.ToString("o"))).ToArray();
                File.WriteAllLines(StatePath, lines, new UTF8Encoding(false));
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

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}
