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
        public long AggregatedDirectories { get; set; }
    }

    public sealed class DiskAnalysisService
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);

        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint errorCode);

        private readonly int maximumRetainedFileNodes;
        private readonly int maximumRetainedDirectoryNodes;

        public DiskAnalysisService()
            : this(50000, 50000)
        {
        }

        internal DiskAnalysisService(int maximumRetainedFileNodes)
            : this(maximumRetainedFileNodes, 50000)
        {
        }

        internal DiskAnalysisService(int maximumRetainedFileNodes, int maximumRetainedDirectoryNodes)
        {
            this.maximumRetainedFileNodes = Math.Max(0, maximumRetainedFileNodes);
            this.maximumRetainedDirectoryNodes = Math.Max(1, maximumRetainedDirectoryNodes);
        }

        private sealed class ScanState
        {
            public readonly Dictionary<string, ExtensionUsage> Extensions =
                new Dictionary<string, ExtensionUsage>(StringComparer.OrdinalIgnoreCase);
            public readonly List<DiskNode> LargestFiles = new List<DiskNode>();
            public readonly int MaximumRetainedFileNodes;
            public readonly int MaximumRetainedDirectoryNodes;
            public int RetainedFileNodes;
            public int RetainedDirectoryNodes = 1;
            public long VisitedFiles;
            public long AggregatedFiles;
            public long AggregatedDirectories;
            public int SkippedPaths;

            public ScanState(int maximumRetainedFileNodes, int maximumRetainedDirectoryNodes)
            {
                MaximumRetainedFileNodes = maximumRetainedFileNodes;
                MaximumRetainedDirectoryNodes = maximumRetainedDirectoryNodes;
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

                var state = new ScanState(maximumRetainedFileNodes, maximumRetainedDirectoryNodes);
                DiskNode root = ScanDirectory(new DirectoryInfo(rootPath), state, progress, cancellationToken, null, true);
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
                    AggregatedFiles = state.AggregatedFiles,
                    AggregatedDirectories = state.AggregatedDirectories
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
                var state = new ScanState(maximumRetainedFileNodes, maximumRetainedDirectoryNodes);
                return ScanDirectory(new DirectoryInfo(rootPath), state, progress, cancellationToken, extension, true);
            }, cancellationToken);
        }

        private static DiskNode ScanDirectory(
            DirectoryInfo directory,
            ScanState state,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            string extensionFilter,
            bool retainDetails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = new DiskNode
            {
                Name = string.IsNullOrWhiteSpace(directory.Name) ? directory.FullName : directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                LastWriteTime = SafeLastWrite(directory)
            };

            DiskNode aggregate = null;
            foreach (FileInfo file in EnumerateFiles(directory, state))
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
                if (retainDetails && state.RetainedFileNodes < state.MaximumRetainedFileNodes)
                {
                    node.Children.Add(fileNode);
                    state.RetainedFileNodes++;
                }
                else
                {
                    if (retainDetails && aggregate == null)
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
                    if (aggregate != null)
                    {
                        aggregate.Size += fileNode.Size;
                        aggregate.PhysicalSize += fileNode.PhysicalSize;
                        aggregate.FileCount++;
                        if (fileNode.LastWriteTime > aggregate.LastWriteTime) aggregate.LastWriteTime = fileNode.LastWriteTime;
                    }
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

            DiskNode directoryAggregate = null;
            foreach (DirectoryInfo child in EnumerateDirectories(directory, state))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    bool retainChild = retainDetails && state.RetainedDirectoryNodes < state.MaximumRetainedDirectoryNodes;
                    if (retainChild) state.RetainedDirectoryNodes++;
                    else state.AggregatedDirectories++;
                    DiskNode childNode = ScanDirectory(child, state, progress, cancellationToken, extensionFilter, retainChild);
                    if (!string.IsNullOrWhiteSpace(extensionFilter) && childNode.FileCount == 0) continue;
                    node.Size += childNode.Size;
                    node.PhysicalSize += childNode.PhysicalSize;
                    node.FileCount += childNode.FileCount;
                    if (retainChild)
                    {
                        node.Children.Add(childNode);
                    }
                    else if (retainDetails && childNode.FileCount > 0)
                    {
                        if (directoryAggregate == null)
                        {
                            directoryAggregate = new DiskNode
                            {
                                Name = "其他目录",
                                FullPath = directory.FullName,
                                IsDirectory = true,
                                IsAggregate = true,
                                LastWriteTime = DateTime.MinValue
                            };
                        }
                        directoryAggregate.Size += childNode.Size;
                        directoryAggregate.PhysicalSize += childNode.PhysicalSize;
                        directoryAggregate.FileCount += childNode.FileCount;
                        if (childNode.LastWriteTime > directoryAggregate.LastWriteTime)
                            directoryAggregate.LastWriteTime = childNode.LastWriteTime;
                    }
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

            if (directoryAggregate != null)
            {
                directoryAggregate.Name = string.Format("其他目录（{0:N0} 个文件）", directoryAggregate.FileCount);
                node.Children.Add(directoryAggregate);
            }

            node.Children.Sort(delegate(DiskNode left, DiskNode right) { return right.Size.CompareTo(left.Size); });
            return node;
        }

        private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo directory, ScanState state)
        {
            IEnumerator<FileInfo> enumerator;
            try { enumerator = directory.EnumerateFiles().GetEnumerator(); }
            catch
            {
                state.SkippedPaths++;
                yield break;
            }
            using (enumerator)
            {
                while (true)
                {
                    FileInfo current;
                    try
                    {
                        if (!enumerator.MoveNext()) yield break;
                        current = enumerator.Current;
                    }
                    catch
                    {
                        state.SkippedPaths++;
                        yield break;
                    }
                    yield return current;
                }
            }
        }

        private static IEnumerable<DirectoryInfo> EnumerateDirectories(DirectoryInfo directory, ScanState state)
        {
            IEnumerator<DirectoryInfo> enumerator;
            try { enumerator = directory.EnumerateDirectories().GetEnumerator(); }
            catch
            {
                state.SkippedPaths++;
                yield break;
            }
            using (enumerator)
            {
                while (true)
                {
                    DirectoryInfo current;
                    try
                    {
                        if (!enumerator.MoveNext()) yield break;
                        current = enumerator.Current;
                    }
                    catch
                    {
                        state.SkippedPaths++;
                        yield break;
                    }
                    yield return current;
                }
            }
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
