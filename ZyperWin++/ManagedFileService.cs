using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    public enum ManagedFileType
    {
        All,
        Video,
        Image,
        Installer,
        Archive,
        Document
    }

    public sealed class ManagedFileEntry
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime ModifiedAt { get; set; }
        public ManagedFileType Type { get; set; }
    }

    public sealed class FileOperationSummary
    {
        public IList<string> AffectedPaths { get; private set; }
        public IList<string> Errors { get; private set; }

        public bool Success { get { return Errors.Count == 0; } }

        public FileOperationSummary()
        {
            AffectedPaths = new List<string>();
            Errors = new List<string>();
        }

        public string Message
        {
            get
            {
                string summary = string.Format("完成 {0} 项，失败 {1} 项。", AffectedPaths.Count, Errors.Count);
                return Errors.Count == 0 ? summary : summary + Environment.NewLine + string.Join(Environment.NewLine, Errors.Take(12));
            }
        }
    }

    public sealed class ManagedFileService
    {
        private static readonly HashSet<string> VideoExtensions = Extensions(".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm");
        private static readonly HashSet<string> ImageExtensions = Extensions(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff");
        private static readonly HashSet<string> InstallerExtensions = Extensions(".exe", ".msi", ".msp", ".appx", ".msix");
        private static readonly HashSet<string> ArchiveExtensions = Extensions(".zip", ".rar", ".7z", ".iso", ".tar", ".gz", ".bz2");
        private static readonly HashSet<string> DocumentExtensions = Extensions(".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".rtf", ".csv");

        public Task<IList<ManagedFileEntry>> ScanAsync(
            string root,
            ManagedFileType type,
            int limit,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run<IList<ManagedFileEntry>>(() => Scan(root, type, limit, progress, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> CopyAsync(IEnumerable<string> paths, string targetDirectory, CancellationToken cancellationToken)
        {
            return Task.Run(() => CopyOrMove(paths, targetDirectory, false, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> MoveAsync(IEnumerable<string> paths, string targetDirectory, CancellationToken cancellationToken)
        {
            return Task.Run(() => CopyOrMove(paths, targetDirectory, true, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RenameAsync(IEnumerable<string> paths, string prefix, CancellationToken cancellationToken)
        {
            return Task.Run(() => Rename(paths, prefix, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> DeleteAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            return Task.Run(() => Delete(paths, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> ShredAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            return Task.Run(() => Shred(paths, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> MigrateWithShortcutsAsync(IEnumerable<string> paths, string targetDirectory, CancellationToken cancellationToken)
        {
            return Task.Run(() => MigrateWithShortcuts(paths, targetDirectory, cancellationToken), cancellationToken);
        }

        public Task<FileOperationSummary> RepairFolderPermissionAsync(string path, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var result = new FileOperationSummary();
                if (!Directory.Exists(path))
                {
                    result.Errors.Add("文件夹不存在：" + path);
                    return result;
                }
                ProcessResult command = ProcessRunner.Run("icacls.exe", Quote(path) + " /reset /T /C /Q", 300000, cancellationToken);
                if (command.Success) result.AffectedPaths.Add(path);
                else result.Errors.Add(string.IsNullOrWhiteSpace(command.Error) ? command.Output : command.Error);
                return result;
            }, cancellationToken);
        }

        public static string TypeLabel(ManagedFileType type)
        {
            switch (type)
            {
                case ManagedFileType.Video: return "视频";
                case ManagedFileType.Image: return "图片";
                case ManagedFileType.Installer: return "安装包";
                case ManagedFileType.Archive: return "压缩包";
                case ManagedFileType.Document: return "文档";
                default: return "全部";
            }
        }

        public static ManagedFileType DetectType(string path)
        {
            string extension = System.IO.Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            if (VideoExtensions.Contains(extension)) return ManagedFileType.Video;
            if (ImageExtensions.Contains(extension)) return ManagedFileType.Image;
            if (InstallerExtensions.Contains(extension)) return ManagedFileType.Installer;
            if (ArchiveExtensions.Contains(extension)) return ManagedFileType.Archive;
            if (DocumentExtensions.Contains(extension)) return ManagedFileType.Document;
            return ManagedFileType.All;
        }

        private static IList<ManagedFileEntry> Scan(string root, ManagedFileType type, int limit, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new DirectoryNotFoundException("目录不存在：" + root);
            var files = new List<ManagedFileEntry>();
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            long visited = 0;
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                try
                {
                    foreach (string child in Directory.EnumerateDirectories(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                        }
                        catch
                        {
                        }
                    }
                    foreach (string path in Directory.EnumerateFiles(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        visited++;
                        ManagedFileType detected = DetectType(path);
                        if (type != ManagedFileType.All && detected != type) continue;
                        try
                        {
                            var info = new FileInfo(path);
                            files.Add(new ManagedFileEntry
                            {
                                Path = info.FullName,
                                Name = info.Name,
                                Size = info.Length,
                                ModifiedAt = info.LastWriteTime,
                                Type = detected
                            });
                        }
                        catch
                        {
                        }
                        if (progress != null && visited % 250 == 0) progress.Report(string.Format("已检查 {0:N0} 个文件：{1}", visited, directory));
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
            IEnumerable<ManagedFileEntry> ordered = files.OrderByDescending(file => file.Size);
            return (limit > 0 ? ordered.Take(limit) : ordered).ToList();
        }

        private static FileOperationSummary CopyOrMove(IEnumerable<string> paths, string targetDirectory, bool move, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                result.Errors.Add("请选择目标目录。");
                return result;
            }
            Directory.CreateDirectory(targetDirectory);
            foreach (string source in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(source))
                    {
                        result.Errors.Add("文件不存在：" + source);
                        continue;
                    }
                    string destination = Path.Combine(targetDirectory, Path.GetFileName(source));
                    if (File.Exists(destination))
                    {
                        result.Errors.Add("目标文件已存在：" + destination);
                        continue;
                    }
                    if (move) MoveFileAcrossVolumes(source, destination);
                    else File.Copy(source, destination, false);
                    result.AffectedPaths.Add(destination);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(Path.GetFileName(source) + "：" + ex.Message);
                }
            }
            return result;
        }

        private static FileOperationSummary Rename(IEnumerable<string> paths, string prefix, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            string cleanPrefix = SanitizeFileName(prefix).Trim();
            List<string> sources = paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();
            if (sources.Count == 0 || cleanPrefix.Length == 0)
            {
                result.Errors.Add("请选择文件并输入有效的批量重命名前缀。");
                return result;
            }

            var temporaryPaths = new List<string>();
            var targetPaths = new List<string>();
            try
            {
                for (int index = 0; index < sources.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string directory = Path.GetDirectoryName(sources[index]);
                    string target = Path.Combine(directory, cleanPrefix + "_" + (index + 1).ToString("000") + Path.GetExtension(sources[index]));
                    if (File.Exists(target) && !string.Equals(target, sources[index], StringComparison.OrdinalIgnoreCase))
                        throw new IOException("目标文件已存在：" + target);
                    targetPaths.Add(target);
                    temporaryPaths.Add(Path.Combine(directory, ".c_diskglow_rename_" + Guid.NewGuid().ToString("N") + ".tmp"));
                }
                for (int index = 0; index < sources.Count; index++) File.Move(sources[index], temporaryPaths[index]);
                for (int index = 0; index < sources.Count; index++)
                {
                    File.Move(temporaryPaths[index], targetPaths[index]);
                    result.AffectedPaths.Add(targetPaths[index]);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                for (int index = 0; index < sources.Count; index++)
                {
                    try
                    {
                        if (File.Exists(targetPaths.ElementAtOrDefault(index)) && !File.Exists(sources[index])) File.Move(targetPaths[index], sources[index]);
                        else if (File.Exists(temporaryPaths.ElementAtOrDefault(index)) && !File.Exists(sources[index])) File.Move(temporaryPaths[index], sources[index]);
                    }
                    catch
                    {
                    }
                }
                result.AffectedPaths.Clear();
            }
            return result;
        }

        private static FileOperationSummary Delete(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            BackupStore.Prune(5000, 1024L * 1024L * 1024L);
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path)) continue;
                string backupError;
                if (!BackupStore.TryBackup(path, out backupError))
                {
                    result.Errors.Add(backupError);
                    continue;
                }
                try
                {
                    File.Delete(path);
                    result.AffectedPaths.Add(path);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(Path.GetFileName(path) + "：" + ex.Message);
                }
            }
            return result;
        }

        private static FileOperationSummary Shred(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists) continue;
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
                        {
                            var buffer = new byte[1024 * 1024];
                            for (int pass = 0; pass < 2; pass++)
                            {
                                stream.Position = 0;
                                long remaining = info.Length;
                                while (remaining > 0)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    int count = (int)Math.Min(buffer.Length, remaining);
                                    if (pass == 0) random.GetBytes(buffer); else Array.Clear(buffer, 0, buffer.Length);
                                    stream.Write(buffer, 0, count);
                                    remaining -= count;
                                }
                                stream.Flush(true);
                            }
                        }
                        File.Delete(path);
                        result.AffectedPaths.Add(path);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(Path.GetFileName(path) + "：" + ex.Message);
                    }
                }
            }
            return result;
        }

        private static FileOperationSummary MigrateWithShortcuts(IEnumerable<string> paths, string targetDirectory, CancellationToken cancellationToken)
        {
            var result = new FileOperationSummary();
            Directory.CreateDirectory(targetDirectory);
            foreach (string source in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(targetDirectory, Path.GetFileName(source));
                string shortcut = source + ".lnk";
                try
                {
                    if (!File.Exists(source)) throw new FileNotFoundException("源文件不存在。", source);
                    if (File.Exists(destination) || File.Exists(shortcut)) throw new IOException("目标文件或原位快捷方式已存在。");
                    MoveFileAcrossVolumes(source, destination);
                    string script = "$s=New-Object -ComObject WScript.Shell; $l=$s.CreateShortcut('" +
                        CommandLineTools.EscapePowerShellLiteral(shortcut) + "'); $l.TargetPath='" +
                        CommandLineTools.EscapePowerShellLiteral(destination) + "'; $l.Save()";
                    ProcessResult created = ProcessRunner.RunPowerShellAsync(script, 60000, cancellationToken).GetAwaiter().GetResult();
                    if (!created.Success || !File.Exists(shortcut))
                    {
                        MoveFileAcrossVolumes(destination, source);
                        throw new IOException("快捷方式创建失败：" + created.Error);
                    }
                    result.AffectedPaths.Add(destination);
                    result.AffectedPaths.Add(shortcut);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(Path.GetFileName(source) + "：" + ex.Message);
                }
            }
            return result;
        }

        private static void MoveFileAcrossVolumes(string source, string destination)
        {
            try
            {
                File.Move(source, destination);
            }
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

        private static HashSet<string> Extensions(params string[] values)
        {
            return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            return result;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}
