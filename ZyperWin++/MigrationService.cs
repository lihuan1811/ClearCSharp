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
    }

    internal sealed class MigrationRecord
    {
        public string Key { get; set; }
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
                foreach (MigrationRecord record in ReadRecords().ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileOperationSummary result = Restore(record.Key, cancellationToken);
                    foreach (string path in result.AffectedPaths) combined.AffectedPaths.Add(path);
                    foreach (string error in result.Errors) combined.Errors.Add(error);
                }
                return combined;
            }, cancellationToken);
        }

        public static IList<MigrationFolder> Catalog()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.Combine(home, "AppData", "Local");
            return new List<MigrationFolder>
            {
                Folder("desktop", "桌面", "Desktop", Path.Combine(home, "Desktop")),
                Folder("documents", "我的文档", "Documents", Path.Combine(home, "Documents")),
                Folder("downloads", "下载", "Downloads", Path.Combine(home, "Downloads")),
                Folder("pictures", "我的图片", "Pictures", Path.Combine(home, "Pictures")),
                Folder("videos", "我的视频", "Videos", Path.Combine(home, "Videos")),
                Folder("appdata_cache", "AppData 本地软件缓存（微信/QQ）", "AppData-Local-Tencent", Path.Combine(local, "Tencent")),
                Folder("temp", "当前用户 Temp 临时文件夹", "User-Temp", Path.Combine(local, "Temp"))
            };
        }

        private static IList<MigrationFolder> Scan(CancellationToken cancellationToken)
        {
            IDictionary<string, MigrationRecord> records = ReadRecords().ToDictionary(record => record.Key, StringComparer.OrdinalIgnoreCase);
            IList<MigrationFolder> folders = Catalog();
            foreach (MigrationFolder folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MigrationRecord record;
                records.TryGetValue(folder.Key, out record);
                folder.Exists = Directory.Exists(folder.SourcePath);
                folder.Migrated = record != null && IsReparsePoint(folder.SourcePath) && Directory.Exists(record.TargetPath);
                folder.TargetPath = folder.Migrated ? record.TargetPath : string.Empty;
                string measuredPath = folder.Migrated ? folder.TargetPath : folder.SourcePath;
                folder.Size = Directory.Exists(measuredPath) ? DirectorySize(measuredPath, cancellationToken) : 0;
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
            if (ReadRecords().Any(record => string.Equals(record.Key, key, StringComparison.OrdinalIgnoreCase)) || IsReparsePoint(folder.SourcePath))
            {
                result.Errors.Add("该目录已经迁移：" + folder.Name);
                return result;
            }

            string validationError;
            string targetRootPath;
            if (!ValidateTarget(folder, targetRoot, out targetRootPath, out validationError))
            {
                result.Errors.Add(validationError);
                return result;
            }
            string target = Path.Combine(targetRootPath, folder.TargetName);
            bool sourceExisted = Directory.Exists(folder.SourcePath);
            bool junctionCreated = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(target);
                if (sourceExisted)
                {
                    MoveDirectoryContents(folder.SourcePath, target, cancellationToken);
                    Directory.Delete(folder.SourcePath, false);
                }
                else Directory.CreateDirectory(Path.GetDirectoryName(folder.SourcePath));

                FileOperationSummary junction = CreateJunction(folder.SourcePath, target, cancellationToken);
                if (!junction.Success) throw new IOException(string.Join(Environment.NewLine, junction.Errors));
                junctionCreated = true;
                if (!UpdateRedirect(folder, target, out validationError)) throw new IOException(validationError);

                AddRecord(new MigrationRecord
                {
                    Key = folder.Key,
                    SourcePath = folder.SourcePath,
                    TargetPath = target,
                    CreatedAt = DateTime.UtcNow
                });
                result.AffectedPaths.Add(folder.SourcePath);
                result.AffectedPaths.Add(target);
                OperationLogger.Info("系统目录迁移", folder.Name + " -> " + target);
            }
            catch (Exception ex)
            {
                if (junctionCreated) RemoveJunction(folder.SourcePath, CancellationToken.None);
                try
                {
                    Directory.CreateDirectory(folder.SourcePath);
                    if (Directory.Exists(target)) MoveDirectoryContents(target, folder.SourcePath, CancellationToken.None);
                    if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target, false);
                    UpdateRedirect(folder, folder.SourcePath, out validationError);
                }
                catch (Exception rollbackError)
                {
                    result.Errors.Add("迁移失败且回滚未完整完成：" + rollbackError.Message);
                }
                result.Errors.Add("迁移失败：" + ex.Message);
                OperationLogger.Error("系统目录迁移", folder.Name + "：" + ex.Message);
            }
            return result;
        }

        private static FileOperationSummary Restore(string key, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            MigrationRecord record = ReadRecords().FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            MigrationFolder folder = Catalog().FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (record == null || folder == null)
            {
                result.Errors.Add("该目录没有可还原的迁移记录。");
                return result;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsReparsePoint(record.SourcePath)) throw new IOException("原路径已不是本程序创建的目录连接，已停止还原。");
                FileOperationSummary removed = RemoveJunction(record.SourcePath, cancellationToken);
                if (!removed.Success) throw new IOException(string.Join(Environment.NewLine, removed.Errors));
                Directory.CreateDirectory(record.SourcePath);
                if (Directory.Exists(record.TargetPath)) MoveDirectoryContents(record.TargetPath, record.SourcePath, cancellationToken);
                string updateError;
                if (!UpdateRedirect(folder, record.SourcePath, out updateError)) throw new IOException(updateError);
                if (Directory.Exists(record.TargetPath) && !Directory.EnumerateFileSystemEntries(record.TargetPath).Any()) Directory.Delete(record.TargetPath, false);
                RemoveRecord(record.Key);
                result.AffectedPaths.Add(record.SourcePath);
                OperationLogger.Info("还原迁移目录", folder.Name + " -> " + record.SourcePath);
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(record.SourcePath) && !IsReparsePoint(record.SourcePath))
                    {
                        Directory.CreateDirectory(record.TargetPath);
                        MoveDirectoryContents(record.SourcePath, record.TargetPath, CancellationToken.None);
                        if (!Directory.EnumerateFileSystemEntries(record.SourcePath).Any()) Directory.Delete(record.SourcePath, false);
                        CreateJunction(record.SourcePath, record.TargetPath, CancellationToken.None);
                        string ignored;
                        UpdateRedirect(folder, record.TargetPath, out ignored);
                    }
                }
                catch
                {
                }
                result.Errors.Add("还原失败，已尝试恢复迁移状态：" + ex.Message);
                OperationLogger.Error("还原迁移目录", folder.Name + "：" + ex.Message);
            }
            return result;
        }

        private static bool ValidateTarget(MigrationFolder folder, string targetRoot, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(targetRoot)) throw new IOException("请选择迁移目标目录。");
                normalized = Path.GetFullPath(targetRoot).TrimEnd(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(normalized);
                var drive = new DriveInfo(Path.GetPathRoot(normalized));
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) throw new IOException("迁移目标必须是可用的本地固定磁盘。");
                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("目标磁盘为 " + drive.DriveFormat + " 格式，不支持连接点。请选择 NTFS 磁盘。");
                if (string.Equals(Path.GetPathRoot(normalized), Path.GetPathRoot(folder.SourcePath), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("迁移目标必须位于其它磁盘。");
                string target = Path.Combine(normalized, folder.TargetName);
                if (IsSameOrChild(target, folder.SourcePath)) throw new IOException("迁移目标不能位于原目录内部。");
                if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                    throw new IOException("迁移目标已存在内容，为避免混入旧文件和破坏回滚，未执行迁移：" + target);
                if (File.Exists(target) || IsReparsePoint(target)) throw new IOException("迁移目标不是可用的普通目录：" + target);
                string executable = Path.GetFullPath(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (IsSameOrChild(executable, folder.SourcePath)) throw new IOException("本程序位于待迁移目录内，请先把程序移动到其它位置。");
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
                UIntPtr ignored;
                SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, UIntPtr.Zero, "Environment", SmtoAbortIfHung, 3000, out ignored);
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
            return new MigrationFolder { Key = key, Name = name, TargetName = targetName, SourcePath = sourcePath };
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
                    if (fields.Length != 4) continue;
                    try
                    {
                        records.Add(new MigrationRecord
                        {
                            Key = fields[0],
                            SourcePath = Decode(fields[1]),
                            TargetPath = Decode(fields[2]),
                            CreatedAt = DateTime.Parse(fields[3]).ToUniversalTime()
                        });
                    }
                    catch
                    {
                    }
                }
                return records;
            }
        }

        private static void AddRecord(MigrationRecord record)
        {
            IList<MigrationRecord> records = ReadRecords();
            records = records.Where(item => !string.Equals(item.Key, record.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            records.Add(record);
            WriteRecords(records);
        }

        private static void RemoveRecord(string key)
        {
            WriteRecords(ReadRecords().Where(item => !string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        private static void WriteRecords(IList<MigrationRecord> records)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
                string[] lines = records.Select(record => string.Join("\t", record.Key, Encode(record.SourcePath), Encode(record.TargetPath), record.CreatedAt.ToString("o"))).ToArray();
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
