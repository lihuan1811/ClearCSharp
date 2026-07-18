using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        public long PhysicalSize { get; set; }
        public long FileCount { get; set; }
        public DateTime LastWriteTime { get; set; }
        public List<DiskNode> Children { get; set; }
        public bool IsAggregate { get; set; }

        public DiskNode()
        {
            Children = new List<DiskNode>();
        }

        public string Extension
        {
            get
            {
                if (IsDirectory) return "文件夹";
                if (IsAggregate) return "其他文件";
                string extension = Path.GetExtension(Name);
                return string.IsNullOrWhiteSpace(extension) ? "无扩展名" : extension.ToLowerInvariant();
            }
        }
    }

    public sealed class ExtensionUsage
    {
        public string Extension { get; set; }
        public long Bytes { get; set; }
        public long PhysicalBytes { get; set; }
        public long FileCount { get; set; }
    }

    public sealed class DiskAnalysisResult
    {
        public DiskNode Root { get; set; }
        public IList<ExtensionUsage> Extensions { get; set; }
        public IList<DiskNode> LargestFiles { get; set; }
        public int SkippedPaths { get; set; }
        public long AggregatedFiles { get; set; }
    }

    public sealed class DiskAnalysisService
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);

        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint errorCode);

        private readonly int maximumRetainedFileNodes;

        public DiskAnalysisService()
            : this(50000)
        {
        }

        internal DiskAnalysisService(int maximumRetainedFileNodes)
        {
            this.maximumRetainedFileNodes = Math.Max(0, maximumRetainedFileNodes);
        }

        private sealed class ScanState
        {
            public readonly Dictionary<string, ExtensionUsage> Extensions =
                new Dictionary<string, ExtensionUsage>(StringComparer.OrdinalIgnoreCase);
            public readonly List<DiskNode> LargestFiles = new List<DiskNode>();
            public readonly int MaximumRetainedFileNodes;
            public int RetainedFileNodes;
            public long VisitedFiles;
            public long AggregatedFiles;
            public int SkippedPaths;

            public ScanState(int maximumRetainedFileNodes)
            {
                MaximumRetainedFileNodes = maximumRetainedFileNodes;
            }
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

                var state = new ScanState(maximumRetainedFileNodes);
                DiskNode root = ScanDirectory(new DirectoryInfo(rootPath), state, progress, cancellationToken, null);
                state.LargestFiles.Sort(delegate(DiskNode left, DiskNode right) { return right.Size.CompareTo(left.Size); });
                if (state.LargestFiles.Count > 500) state.LargestFiles.RemoveRange(500, state.LargestFiles.Count - 500);

                return new DiskAnalysisResult
                {
                    Root = root,
                    Extensions = state.Extensions.Values
                        .OrderByDescending(value => value.Bytes)
                        .ToList(),
                    LargestFiles = state.LargestFiles,
                    SkippedPaths = state.SkippedPaths,
                    AggregatedFiles = state.AggregatedFiles
                };
            }, cancellationToken);
        }

        public Task<DiskNode> ScanExtensionAsync(
            string rootPath,
            string extension,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                    throw new DirectoryNotFoundException("目录不存在：" + rootPath);
                if (string.IsNullOrWhiteSpace(extension))
                    throw new ArgumentException("文件类型不能为空。", nameof(extension));
                var state = new ScanState(maximumRetainedFileNodes);
                return ScanDirectory(new DirectoryInfo(rootPath), state, progress, cancellationToken, extension);
            }, cancellationToken);
        }

        private static DiskNode ScanDirectory(
            DirectoryInfo directory,
            ScanState state,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            string extensionFilter)
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

            DiskNode aggregate = null;
            foreach (FileInfo file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesExtension(file.Name, extensionFilter)) continue;
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
                    PhysicalSize = ReadPhysicalSize(file.FullName, length),
                    FileCount = 1,
                    LastWriteTime = SafeLastWrite(file)
                };
                if (state.RetainedFileNodes < state.MaximumRetainedFileNodes)
                {
                    node.Children.Add(fileNode);
                    state.RetainedFileNodes++;
                }
                else
                {
                    if (aggregate == null)
                    {
                        aggregate = new DiskNode
                        {
                            Name = "其他文件",
                            FullPath = directory.FullName,
                            IsDirectory = false,
                            IsAggregate = true,
                            LastWriteTime = DateTime.MinValue
                        };
                    }
                    aggregate.Size += fileNode.Size;
                    aggregate.PhysicalSize += fileNode.PhysicalSize;
                    aggregate.FileCount++;
                    if (fileNode.LastWriteTime > aggregate.LastWriteTime) aggregate.LastWriteTime = fileNode.LastWriteTime;
                    state.AggregatedFiles++;
                }
                node.Size += length;
                node.PhysicalSize += fileNode.PhysicalSize;
                node.FileCount++;
                AddExtension(state, fileNode);
                AddLargestFile(state, fileNode);

                state.VisitedFiles++;
                if (progress != null && state.VisitedFiles % 250 == 0)
                    progress.Report(string.Format("已扫描 {0:N0} 个文件：{1}", state.VisitedFiles, directory.FullName));
            }

            if (aggregate != null)
            {
                aggregate.Name = string.Format("其他文件（{0:N0} 个）", aggregate.FileCount);
                node.Children.Add(aggregate);
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
                    DiskNode childNode = ScanDirectory(child, state, progress, cancellationToken, extensionFilter);
                    if (!string.IsNullOrWhiteSpace(extensionFilter) && childNode.FileCount == 0) continue;
                    node.Children.Add(childNode);
                    node.Size += childNode.Size;
                    node.PhysicalSize += childNode.PhysicalSize;
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

        private static bool MatchesExtension(string fileName, string extensionFilter)
        {
            if (string.IsNullOrWhiteSpace(extensionFilter)) return true;
            string extension = Path.GetExtension(fileName);
            string normalized = string.IsNullOrWhiteSpace(extension) ? "无扩展名" : extension.ToLowerInvariant();
            return string.Equals(normalized, extensionFilter, StringComparison.OrdinalIgnoreCase);
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
            usage.PhysicalBytes += file.PhysicalSize;
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

        internal static long ReadPhysicalSize(string path, long fallbackLength)
        {
            uint high;
            SetLastError(0);
            uint low = GetCompressedFileSize(path, out high);
            int error = Marshal.GetLastWin32Error();
            if (low == uint.MaxValue && error != 0) return fallbackLength;
            ulong value = ((ulong)high << 32) | low;
            return value > long.MaxValue ? fallbackLength : (long)value;
        }
    }
}
