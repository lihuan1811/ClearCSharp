using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    public sealed class DiskNode
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public long FileCount { get; set; }
        public DateTime LastWriteTime { get; set; }
        public List<DiskNode> Children { get; set; }

        public DiskNode()
        {
            Children = new List<DiskNode>();
        }

        public string Extension
        {
            get
            {
                if (IsDirectory) return "文件夹";
                string extension = Path.GetExtension(Name);
                return string.IsNullOrWhiteSpace(extension) ? "无扩展名" : extension.ToLowerInvariant();
            }
        }
    }

    public sealed class ExtensionUsage
    {
        public string Extension { get; set; }
        public long Bytes { get; set; }
        public long FileCount { get; set; }
    }

    public sealed class DiskAnalysisResult
    {
        public DiskNode Root { get; set; }
        public IList<ExtensionUsage> Extensions { get; set; }
        public IList<DiskNode> LargestFiles { get; set; }
        public int SkippedPaths { get; set; }
    }

    public sealed class DiskAnalysisService
    {
        private sealed class ScanState
        {
            public readonly Dictionary<string, ExtensionUsage> Extensions =
                new Dictionary<string, ExtensionUsage>(StringComparer.OrdinalIgnoreCase);
            public readonly List<DiskNode> LargestFiles = new List<DiskNode>();
            public long VisitedFiles;
            public int SkippedPaths;
        }

        public Task<DiskAnalysisResult> ScanAsync(
            string rootPath,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                    throw new DirectoryNotFoundException("目录不存在：" + rootPath);

                var state = new ScanState();
                DiskNode root = ScanDirectory(new DirectoryInfo(rootPath), state, progress, cancellationToken);
                state.LargestFiles.Sort(delegate(DiskNode left, DiskNode right) { return right.Size.CompareTo(left.Size); });
                if (state.LargestFiles.Count > 500) state.LargestFiles.RemoveRange(500, state.LargestFiles.Count - 500);

                return new DiskAnalysisResult
                {
                    Root = root,
                    Extensions = state.Extensions.Values
                        .OrderByDescending(value => value.Bytes)
                        .ToList(),
                    LargestFiles = state.LargestFiles,
                    SkippedPaths = state.SkippedPaths
                };
            }, cancellationToken);
        }

        private static DiskNode ScanDirectory(
            DirectoryInfo directory,
            ScanState state,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = new DiskNode
            {
                Name = string.IsNullOrWhiteSpace(directory.Name) ? directory.FullName : directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                LastWriteTime = SafeLastWrite(directory)
            };

            FileInfo[] files;
            try { files = directory.GetFiles(); }
            catch
            {
                state.SkippedPaths++;
                files = new FileInfo[0];
            }

            foreach (FileInfo file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long length;
                try { length = file.Length; }
                catch
                {
                    state.SkippedPaths++;
                    continue;
                }

                var fileNode = new DiskNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false,
                    Size = length,
                    FileCount = 1,
                    LastWriteTime = SafeLastWrite(file)
                };
                node.Children.Add(fileNode);
                node.Size += length;
                node.FileCount++;
                AddExtension(state, fileNode);
                AddLargestFile(state, fileNode);

                state.VisitedFiles++;
                if (progress != null && state.VisitedFiles % 250 == 0)
                    progress.Report(string.Format("已扫描 {0:N0} 个文件：{1}", state.VisitedFiles, directory.FullName));
            }

            DirectoryInfo[] directories;
            try { directories = directory.GetDirectories(); }
            catch
            {
                state.SkippedPaths++;
                directories = new DirectoryInfo[0];
            }

            foreach (DirectoryInfo child in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    DiskNode childNode = ScanDirectory(child, state, progress, cancellationToken);
                    node.Children.Add(childNode);
                    node.Size += childNode.Size;
                    node.FileCount += childNode.FileCount;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    state.SkippedPaths++;
                }
            }

            node.Children.Sort(delegate(DiskNode left, DiskNode right) { return right.Size.CompareTo(left.Size); });
            return node;
        }

        private static void AddExtension(ScanState state, DiskNode file)
        {
            string extension = file.Extension;
            ExtensionUsage usage;
            if (!state.Extensions.TryGetValue(extension, out usage))
            {
                usage = new ExtensionUsage { Extension = extension };
                state.Extensions[extension] = usage;
            }
            usage.Bytes += file.Size;
            usage.FileCount++;
        }

        private static void AddLargestFile(ScanState state, DiskNode file)
        {
            if (state.LargestFiles.Count < 500)
            {
                state.LargestFiles.Add(file);
                return;
            }

            int smallestIndex = 0;
            for (int index = 1; index < state.LargestFiles.Count; index++)
            {
                if (state.LargestFiles[index].Size < state.LargestFiles[smallestIndex].Size) smallestIndex = index;
            }
            if (file.Size > state.LargestFiles[smallestIndex].Size) state.LargestFiles[smallestIndex] = file;
        }

        private static DateTime SafeLastWrite(FileSystemInfo info)
        {
            try { return info.LastWriteTime; }
            catch { return DateTime.MinValue; }
        }
    }
}
