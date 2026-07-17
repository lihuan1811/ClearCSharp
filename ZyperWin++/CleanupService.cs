using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    public enum CleanupKind
    {
        DriveC,
        QQ,
        WeChat
    }

    public sealed class CleanupRule
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string SearchPattern { get; set; }
        public bool Recursive { get; set; }
        public bool Recommended { get; set; }
        public List<string> PathTemplates { get; set; }
    }

    public sealed class CleanupScanResult
    {
        public CleanupRule Rule { get; set; }
        public long Bytes { get; set; }
        public int FileCount { get; set; }
        public List<string> Files { get; set; }
        public List<string> Roots { get; set; }
    }

    public sealed class CleanupResult
    {
        public long Bytes { get; set; }
        public int DeletedFiles { get; set; }
        public int FailedFiles { get; set; }
    }

    public static class CleanupCatalog
    {
        public static IList<CleanupRule> GetRules(CleanupKind kind)
        {
            switch (kind)
            {
                case CleanupKind.QQ:
                    return QQRules();
                case CleanupKind.WeChat:
                    return WeChatRules();
                default:
                    return DriveRules();
            }
        }

        private static IList<CleanupRule> DriveRules()
        {
            return new List<CleanupRule>
            {
                Rule("user-temp", "用户临时文件", "当前用户 TEMP 目录中的临时文件", true,
                    "%TEMP%"),
                Rule("windows-temp", "Windows 临时文件", "Windows Temp 目录中可安全释放的文件", true,
                    "%WINDIR%\\Temp"),
                Rule("wer", "Windows 错误报告", "已归档和待上报的错误报告", true,
                    "%ProgramData%\\Microsoft\\Windows\\WER\\ReportArchive",
                    "%ProgramData%\\Microsoft\\Windows\\WER\\ReportQueue"),
                PatternRule("crash-dumps", "应用崩溃转储", "应用生成的 .dmp 崩溃文件", true, "*.dmp",
                    "%LOCALAPPDATA%\\CrashDumps"),
                Rule("browser-cache", "浏览器缓存", "Edge、Chrome 的网页缓存，不删除收藏与登录数据", true,
                    "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Cache",
                    "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Code Cache",
                    "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache",
                    "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Code Cache"),
                PatternRule("thumb-cache", "缩略图缓存", "资源管理器生成的缩略图数据库", true, "thumbcache_*.db",
                    "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer"),
                Rule("delivery-cache", "传递优化缓存", "Windows 更新传递优化下载缓存", false,
                    "%ProgramData%\\Microsoft\\Windows\\DeliveryOptimization\\Cache")
            };
        }

        private static IList<CleanupRule> QQRules()
        {
            return new List<CleanupRule>
            {
                Rule("qq-cache", "QQ 缓存", "QQ 与 NTQQ 生成的可再生缓存", true,
                    "%LOCALAPPDATA%\\Tencent\\QQ\\Cache",
                    "%APPDATA%\\Tencent\\QQ\\Cache",
                    "%USERPROFILE%\\Documents\\Tencent Files\\nt_qq\\global\\nt_data\\Cache"),
                Rule("qq-temp", "QQ 临时文件", "QQ 运行产生的临时目录", true,
                    "%APPDATA%\\Tencent\\QQ\\Temp",
                    "%LOCALAPPDATA%\\Tencent\\QQ\\Temp",
                    "%USERPROFILE%\\Documents\\Tencent Files\\All Users\\QQ\\Temp"),
                PatternRule("qq-logs", "QQ 日志", "QQ 客户端诊断日志", true, "*.log",
                    "%LOCALAPPDATA%\\Tencent\\QQ\\Logs",
                    "%APPDATA%\\Tencent\\QQ\\Logs")
            };
        }

        private static IList<CleanupRule> WeChatRules()
        {
            return new List<CleanupRule>
            {
                Rule("wechat-cache", "微信缓存", "微信账号目录中的 FileStorage Cache", true,
                    "%USERPROFILE%\\Documents\\WeChat Files\\*\\FileStorage\\Cache",
                    "%LOCALAPPDATA%\\Tencent\\WeChat\\Cache",
                    "%APPDATA%\\Tencent\\WeChat\\Cache"),
                Rule("wechat-temp", "微信临时文件", "微信与新版 xwechat 产生的临时数据", true,
                    "%USERPROFILE%\\Documents\\WeChat Files\\*\\FileStorage\\Temp",
                    "%USERPROFILE%\\Documents\\xwechat_files\\*\\temp",
                    "%LOCALAPPDATA%\\Tencent\\WeChat\\Temp"),
                PatternRule("wechat-logs", "微信日志", "微信客户端诊断日志", true, "*.log",
                    "%APPDATA%\\Tencent\\WeChat\\Log",
                    "%LOCALAPPDATA%\\Tencent\\WeChat\\Log")
            };
        }

        private static CleanupRule Rule(
            string id,
            string name,
            string description,
            bool recommended,
            params string[] paths)
        {
            return PatternRule(id, name, description, recommended, "*", paths);
        }

        private static CleanupRule PatternRule(
            string id,
            string name,
            string description,
            bool recommended,
            string searchPattern,
            params string[] paths)
        {
            return new CleanupRule
            {
                Id = id,
                Name = name,
                Description = description,
                SearchPattern = searchPattern,
                Recursive = true,
                Recommended = recommended,
                PathTemplates = paths.ToList()
            };
        }
    }

    public sealed class CleanupService
    {
        public Task<IList<CleanupScanResult>> ScanAsync(
            CleanupKind kind,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run<IList<CleanupScanResult>>(() =>
            {
                var results = new List<CleanupScanResult>();
                foreach (var rule in CleanupCatalog.GetRules(kind))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (progress != null) progress.Report("正在扫描：" + rule.Name);

                    var result = new CleanupScanResult
                    {
                        Rule = rule,
                        Files = new List<string>(),
                        Roots = new List<string>()
                    };

                    foreach (string template in rule.PathTemplates)
                    {
                        foreach (string root in ResolveRoots(template))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!IsSafeCleanupRoot(root) || !Directory.Exists(root)) continue;
                            result.Roots.Add(root);
                            ScanRoot(root, rule.SearchPattern, rule.Recursive, result, cancellationToken);
                        }
                    }

                    results.Add(result);
                }
                return results;
            }, cancellationToken);
        }

        public Task<CleanupResult> CleanAsync(
            IEnumerable<CleanupScanResult> selected,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var result = new CleanupResult();
                foreach (CleanupScanResult item in selected)
                {
                    foreach (string file in item.Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (!File.Exists(file)) continue;
                            long length = 0;
                            try { length = new FileInfo(file).Length; } catch { }
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                            result.DeletedFiles++;
                            result.Bytes += length;
                        }
                        catch
                        {
                            result.FailedFiles++;
                        }
                    }

                    foreach (string root in item.Roots)
                    {
                        RemoveEmptyDirectories(root, cancellationToken);
                    }

                    if (progress != null) progress.Report("已完成：" + item.Rule.Name);
                    OperationLogger.Info(
                        "清理",
                        string.Format("{0}，扫描 {1} 个文件，释放 {2}", item.Rule.Name, item.FileCount, DisplayFormat.Bytes(item.Bytes)));
                }
                return result;
            }, cancellationToken);
        }

        public static bool IsSafeCleanupRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string full;
            try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
            catch { return false; }

            string[] protectedPaths =
            {
                Environment.GetEnvironmentVariable("SystemDrive") + Path.DirectorySeparatorChar,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            foreach (string protectedPath in protectedPaths)
            {
                if (string.IsNullOrWhiteSpace(protectedPath)) continue;
                string normalized = Path.GetFullPath(protectedPath).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(full, normalized, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return full.Split(Path.DirectorySeparatorChar).Length >= 3;
        }

        private static IEnumerable<string> ResolveRoots(string template)
        {
            string expanded = Environment.ExpandEnvironmentVariables(template);
            if (expanded.IndexOf('*') < 0 && expanded.IndexOf('?') < 0)
            {
                yield return expanded;
                yield break;
            }

            int wildcard = expanded.IndexOfAny(new[] { '*', '?' });
            int separator = expanded.LastIndexOf(Path.DirectorySeparatorChar, wildcard);
            if (separator <= 2) yield break;

            string parent = expanded.Substring(0, separator);
            string remainder = expanded.Substring(separator + 1);
            int nextSeparator = remainder.IndexOf(Path.DirectorySeparatorChar);
            string pattern = nextSeparator < 0 ? remainder : remainder.Substring(0, nextSeparator);
            string tail = nextSeparator < 0 ? string.Empty : remainder.Substring(nextSeparator + 1);

            string[] matches;
            try { matches = Directory.GetDirectories(parent, pattern, SearchOption.TopDirectoryOnly); }
            catch { yield break; }

            foreach (string match in matches)
            {
                string candidate = string.IsNullOrEmpty(tail) ? match : Path.Combine(match, tail);
                foreach (string resolved in ResolveRoots(candidate)) yield return resolved;
            }
        }

        private static void ScanRoot(
            string root,
            string searchPattern,
            bool recursive,
            CleanupScanResult result,
            CancellationToken cancellationToken)
        {
            var directories = new Stack<string>();
            directories.Push(root);

            while (directories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = directories.Pop();

                string[] files;
                try { files = Directory.GetFiles(current, searchPattern, SearchOption.TopDirectoryOnly); }
                catch { files = new string[0]; }

                foreach (string file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        result.Files.Add(file);
                        result.FileCount++;
                        result.Bytes += info.Length;
                    }
                    catch
                    {
                    }
                }

                if (!recursive) continue;
                string[] children;
                try { children = Directory.GetDirectories(current); }
                catch { children = new string[0]; }
                foreach (string child in children)
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) directories.Push(child);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
        {
            string[] directories;
            try { directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
            catch { return; }

            foreach (string directory in directories.OrderByDescending(value => value.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
                }
                catch
                {
                }
            }
        }
    }
}
