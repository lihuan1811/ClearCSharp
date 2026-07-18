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
        DriveC
    }

    public sealed class CleanupRule
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string Risk { get; set; }
        public List<string> SearchPatterns { get; set; }
        public bool Recursive { get; set; }
        public bool Recommended { get; set; }
        public bool ScanOnly { get; set; }
        public int MinimumAgeDays { get; set; }
        public long MinimumBytes { get; set; }
        public List<string> PathTemplates { get; set; }
    }

    public sealed class CleanupScanResult
    {
        public CleanupRule Rule { get; set; }
        public long Bytes { get; set; }
        public int FileCount { get; set; }
        public List<string> Files { get; set; }
        public HashSet<string> SelectedFiles { get; set; }
        public List<string> Roots { get; set; }
    }

    public sealed class CleanupResult
    {
        public long Bytes { get; set; }
        public int DeletedFiles { get; set; }
        public int FailedFiles { get; set; }
        public int SkippedFiles { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public static class CleanupCatalog
    {
        public static IList<CleanupRule> GetRules(CleanupKind kind)
        {
            return DriveRules();
        }

        private static IList<CleanupRule> DriveRules()
        {
            return new List<CleanupRule>
            {
                Rule("过期文件", "winsxs-backup", "WinSxS 备份组件（仅分析）", "组件存储必须由 DISM/CBS 维护，本工具不会直接删除。", "高风险", false, true, 0, null,
                    "%WINDIR%\\WinSxS\\Backup"),
                Rule("过期文件", "old-windows", "旧 Windows 安装文件（仅分析）", "用于版本回退的系统升级残留，只统计空间。", "高风险", false, true, 0, null,
                    "%SystemDrive%\\Windows.old", "%SystemDrive%\\$Windows.~BT", "%SystemDrive%\\$Windows.~WS"),
                Rule("过期文件", "service-pack", "旧服务包卸载备份", "旧版 Windows 服务包留下的卸载备份。", "谨慎", false, false, 0, null,
                    "%WINDIR%\\$NtServicePackUninstall$", "%WINDIR%\\$hf_mig$"),
                Rule("过期文件", "windows-update", "Windows 更新下载缓存", "已下载的更新包与临时文件，正在安装更新时不应清理。", "谨慎", false, false, 0, null,
                    "%WINDIR%\\SoftwareDistribution\\Download", "%WINDIR%\\SoftwareDistribution\\Temp"),
                Rule("过期文件", "delivery-optimization", "Windows 传递优化缓存", "Windows 更新分发时保存的可再生下载缓存。", "安全", true, false, 0, null,
                    "%WINDIR%\\ServiceProfiles\\NetworkService\\AppData\\Local\\Microsoft\\Windows\\DeliveryOptimization\\Cache",
                    "%WINDIR%\\SoftwareDistribution\\DeliveryOptimization\\Cache"),
                Rule("过期文件", "backup-temp", "30 天前的备份临时文件", "Windows Backup 产生的旧日志和临时文件。", "安全", true, false, 30, null,
                    "%WINDIR%\\Temp\\WindowsBackup", "%WINDIR%\\Logs\\WindowsBackup", "%LOCALAPPDATA%\\Microsoft\\Windows\\WindowsBackup"),
                Rule("过期文件", "installer-cache", "30 天前的安装程序缓存", "仅匹配安装缓存中的临时、日志和旧文件，不删除有效 MSI/MSP。", "谨慎", false, false, 30,
                    new[] { "*.tmp", "*.temp", "*.log", "*.old", "*.msi.cache", "*.msp.cache", "*.exe.cache" },
                    "%WINDIR%\\Installer\\Temp", "%ProgramData%\\Package Cache\\Temp", "%LOCALAPPDATA%\\Package Cache"),

                Rule("系统相关", "error-reports", "Windows 错误报告", "系统和应用崩溃后生成的 WER 报告。", "安全", true, false, 0, null,
                    "%ProgramData%\\Microsoft\\Windows\\WER", "%LOCALAPPDATA%\\Microsoft\\Windows\\WER"),
                Rule("系统相关", "event-logs", "Windows 事件日志（仅分析）", "事件日志用于系统审计和故障排查，只统计不删除。", "高风险", false, true, 0, new[] { "*.evtx" },
                    "%WINDIR%\\System32\\winevt\\Logs"),
                Rule("系统相关", "setup-logs", "Windows 安装与设备日志", "系统安装、升级和设备安装留下的日志。", "安全", true, false, 0, new[] { "*.log", "*.etl", "*.tmp" },
                    "%WINDIR%\\Panther", "%WINDIR%\\INF", "%WINDIR%\\System32\\LogFiles\\setupapi"),
                Rule("系统相关", "system-logs", "Windows 系统日志", "Windows Logs 与 debug 目录中的旧日志和跟踪文件。", "安全", true, false, 0, new[] { "*.log", "*.etl", "*.tmp", "*.dmp" },
                    "%WINDIR%\\Logs", "%WINDIR%\\debug"),
                Rule("系统相关", "memory-dumps", "系统内存转储文件", "蓝屏和系统故障生成的内存转储。", "安全", true, false, 0, new[] { "*.dmp", "MEMORY.DMP" },
                    "%WINDIR%\\Minidump", "%WINDIR%\\MEMORY.DMP"),
                Rule("系统相关", "app-crash", "应用程序崩溃转储", "应用崩溃后写入当前用户 CrashDumps 的转储文件。", "安全", true, false, 0, new[] { "*.dmp" },
                    "%LOCALAPPDATA%\\CrashDumps"),
                Rule("系统相关", "winsxs-temp", "WinSxS 临时文件（仅分析）", "组件存储临时目录由 TrustedInstaller/CBS 管理，只统计。", "高风险", false, true, 0, null,
                    "%WINDIR%\\WinSxS\\Temp"),
                Rule("系统相关", "prefetch", "Windows 预读取文件", "系统用于加速程序启动的预读取记录，默认不勾选。", "谨慎", false, false, 0, new[] { "*.pf" },
                    "%WINDIR%\\Prefetch"),

                Rule("缓存文件", "edge-cache", "Microsoft Edge 缓存", "网页缓存与代码缓存，不删除收藏、密码和 Cookie。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Cache", "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Code Cache"),
                Rule("缓存文件", "chrome-cache", "Google Chrome 缓存", "网页缓存与代码缓存，不删除用户配置。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache", "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Code Cache"),
                Rule("缓存文件", "firefox-cache", "Mozilla Firefox 缓存", "Firefox 配置目录中的 cache2 内容。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2"),
                Rule("缓存文件", "qq-browser-cache", "QQ 浏览器缓存", "QQ 浏览器的网页缓存，不处理账号与密码数据。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\Tencent\\QQBrowser\\User Data\\*\\Cache"),
                Rule("缓存文件", "store-cache", "Microsoft Store 缓存", "商城应用产生的本地缓存目录。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\Packages\\Microsoft.WindowsStore_*\\LocalCache"),
                Rule("缓存文件", "terminal-cache", "Terminal Server Client 缓存", "远程桌面客户端生成的位图缓存。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\Microsoft\\Terminal Server Client\\Cache"),
                Rule("缓存文件", "wininet-cache", "WinINet 网页缓存", "Windows 旧式网页组件生成的可再生缓存。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache"),
                Rule("缓存文件", "wininet-cookies", "WinINet Cookies", "可能包含网站登录状态，默认不勾选。", "谨慎", false, false, 0, null,
                    "%LOCALAPPDATA%\\Microsoft\\Windows\\INetCookies"),
                Rule("缓存文件", "onedrive-logs", "OneDrive 日志缓存", "OneDrive 诊断日志与可再生缓存。", "安全", true, false, 0, new[] { "*.log", "*.etl", "*.tmp" },
                    "%LOCALAPPDATA%\\Microsoft\\OneDrive\\logs"),
                Rule("缓存文件", "thumbnails", "缩略图缓存", "资源管理器生成的缩略图数据库。", "安全", true, false, 0, new[] { "thumbcache_*.db" },
                    "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer"),
                Rule("缓存文件", "shader-cache", "DirectX 着色器缓存", "图形驱动和 DirectX 产生的可再生着色器缓存。", "安全", true, false, 0, null,
                    "%LOCALAPPDATA%\\D3DSCache", "%LOCALAPPDATA%\\NVIDIA\\DXCache", "%LOCALAPPDATA%\\AMD\\DxCache"),

                Rule("应用程序", "office-cache", "Microsoft Office 文档缓存", "Office 上传中心和文档缓存中的临时内容。", "谨慎", false, false, 7, new[] { "*.tmp", "*.log", "*.cache" },
                    "%LOCALAPPDATA%\\Microsoft\\Office\\*\\OfficeFileCache"),
                Rule("应用程序", "development-cache", "开发工具缓存", "NuGet、npm 与 pip 下载缓存，可由工具重新生成。", "安全", false, false, 0, null,
                    "%USERPROFILE%\\.nuget\\packages", "%APPDATA%\\npm-cache", "%LOCALAPPDATA%\\pip\\Cache"),
                Rule("应用程序", "adobe-cache", "Adobe 应用缓存", "Adobe 媒体缓存和临时数据。", "安全", false, false, 7, null,
                    "%APPDATA%\\Adobe\\Common\\Media Cache", "%APPDATA%\\Adobe\\Common\\Media Cache Files"),

                Rule("微信缓存专清", "wechat-cache", "微信缓存与小程序临时文件", "微信可再生成的 Cache、Temp 和 Applet 内容。", "安全", true, false, 0, null,
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "FileStorage", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "FileStorage", "Temp"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "FileStorage", "Applet"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "FileStorage", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "FileStorage", "Temp"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "FileStorage", "Applet"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "cache"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "cache"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "FileStorage", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "FileStorage", "Temp"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "FileStorage", "Applet"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "FileStorage", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "FileStorage", "Temp"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "FileStorage", "Applet"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "*", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "FileStorage", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "FileStorage", "Temp"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "FileStorage", "Applet"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "*", "FileStorage", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "*", "FileStorage", "Temp"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "*", "FileStorage", "Applet"),
                    "%LOCALAPPDATA%\\Tencent\\WeChat\\Cache", "%LOCALAPPDATA%\\Tencent\\WeChat\\*\\Cache",
                    "%LOCALAPPDATA%\\Tencent\\WeChat\\FileStorage\\Cache", "%LOCALAPPDATA%\\Tencent\\WeChat\\*\\FileStorage\\Cache",
                    "%LOCALAPPDATA%\\Tencent\\WeChat\\FileStorage\\Temp", "%LOCALAPPDATA%\\Tencent\\WeChat\\*\\FileStorage\\Temp",
                    "%LOCALAPPDATA%\\Tencent\\WeChat\\FileStorage\\Applet", "%LOCALAPPDATA%\\Tencent\\WeChat\\*\\FileStorage\\Applet",
                    "%APPDATA%\\Tencent\\WeChat\\Cache", "%APPDATA%\\Tencent\\WeChat\\*\\Cache"),
                Rule("微信缓存专清", "wechat-media", "微信图片、视频与接收文件", "删除后本地接收的媒体和文件可能无法离线查看，默认不勾选。", "谨慎", false, false, 0, null,
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "FileStorage", "Image"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "FileStorage", "Video"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "FileStorage", "File"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "FileStorage", "MsgAttach"),
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "Msg", "Audio"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "FileStorage", "Image"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "FileStorage", "Video"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "FileStorage", "File"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "*", "FileStorage", "Image"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "*", "FileStorage", "Video"),
                    Path.Combine(KnownFolderPaths.Documents, "WXWork", "*", "FileStorage", "File")),
                Rule("微信缓存专清", "wechat-records", "微信本地聊天记录（仅分析）", "本地聊天数据库和备份不可直接清理，只统计空间。", "高风险", false, true, 0, null,
                    Path.Combine(KnownFolderPaths.Documents, "WeChat Files", "*", "Msg"),
                    Path.Combine(KnownFolderPaths.Documents, "xwechat_files", "*", "msg")),

                Rule("QQ缓存专清", "qq-cache", "QQ/QQNT 可再生缓存", "QQ/QQNT 的 Cache 临时内容，不包含图片、视频、接收文件和聊天记录。", "安全", true, false, 0, null,
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "Cache"),
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "*", "Cache"),
                    "%LOCALAPPDATA%\\Tencent\\QQNT\\Cache", "%LOCALAPPDATA%\\Tencent\\QQNT\\*\\Cache",
                    "%LOCALAPPDATA%\\Tencent\\QQNT\\Temp", "%LOCALAPPDATA%\\Tencent\\QQNT\\*\\Temp",
                    "%LOCALAPPDATA%\\Tencent\\QQ\\Cache", "%LOCALAPPDATA%\\Tencent\\QQ\\*\\Cache",
                    "%LOCALAPPDATA%\\Tencent\\QQ\\Temp", "%LOCALAPPDATA%\\Tencent\\QQ\\*\\Temp",
                    "%APPDATA%\\Tencent\\QQ\\Cache", "%APPDATA%\\Tencent\\QQ\\*\\Cache",
                    "%APPDATA%\\Tencent\\QQ\\Temp", "%APPDATA%\\Tencent\\QQ\\*\\Temp"),
                Rule("QQ缓存专清", "qq-media", "QQ 图片、视频与短视频", "删除后本地媒体可能无法离线查看，默认不勾选。", "谨慎", false, false, 0, null,
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "Image"),
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "Video"),
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "ShortVideo"),
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "*", "Image"),
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "*", "Video"),
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "*", "ShortVideo"),
                    "%LOCALAPPDATA%\\Tencent\\QQ\\Image", "%LOCALAPPDATA%\\Tencent\\QQ\\*\\Image",
                    "%LOCALAPPDATA%\\Tencent\\QQ\\Video", "%LOCALAPPDATA%\\Tencent\\QQ\\*\\Video",
                    "%LOCALAPPDATA%\\Tencent\\QQNT\\Image", "%LOCALAPPDATA%\\Tencent\\QQNT\\*\\Image",
                    "%LOCALAPPDATA%\\Tencent\\QQNT\\Video", "%LOCALAPPDATA%\\Tencent\\QQNT\\*\\Video",
                    "%APPDATA%\\Tencent\\QQ\\Image", "%APPDATA%\\Tencent\\QQ\\*\\Image",
                    "%APPDATA%\\Tencent\\QQ\\Video", "%APPDATA%\\Tencent\\QQ\\*\\Video"),
                Rule("QQ缓存专清", "qq-received", "QQ 接收文件与安装包", "删除后本地接收文件会丢失，默认不勾选。", "谨慎", false, false, 0, null,
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "*", "FileRecv"),
                    "%LOCALAPPDATA%\\Tencent\\QQ\\FileRecv", "%LOCALAPPDATA%\\Tencent\\QQ\\*\\FileRecv",
                    "%LOCALAPPDATA%\\Tencent\\QQNT\\FileRecv", "%LOCALAPPDATA%\\Tencent\\QQNT\\*\\FileRecv",
                    "%APPDATA%\\Tencent\\QQ\\FileRecv", "%APPDATA%\\Tencent\\QQ\\*\\FileRecv"),
                Rule("QQ缓存专清", "qq-records", "QQ 本地聊天记录（仅分析）", "QQ 本地消息数据库不可直接清理，只统计空间。", "高风险", false, true, 0, null,
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "*", "Msg2.0"),
                    Path.Combine(KnownFolderPaths.Documents, "Tencent Files", "*", "Msg3.0"),
                    "%LOCALAPPDATA%\\Tencent\\QQNT\\*\\nt_db"),

                MinimumSize(
                    Rule("应用程序", "large-packages", "C盘大型安装包、压缩包与镜像", "扫描常用个人目录中超过 50 MB 的安装包、压缩包和镜像，默认不勾选。", "谨慎", false, false, 0,
                        new[] { "*.exe", "*.msi", "*.msix", "*.appx", "*.zip", "*.rar", "*.7z", "*.iso", "*.img" },
                        KnownFolderPaths.Downloads, KnownFolderPaths.Desktop, KnownFolderPaths.Documents),
                    50L * 1024L * 1024L),

                Rule("临时文件", "user-temp", "用户临时文件", "当前用户 TEMP 目录中的临时文件。", "安全", true, false, 0, null,
                    "%TEMP%"),
                Rule("临时文件", "windows-temp", "Windows 临时文件", "Windows Temp 中未被系统占用的临时文件。", "安全", true, false, 0, null,
                    "%WINDIR%\\Temp"),
                Rule("临时文件", "download-remnants", "下载未完成残留", "下载目录中超过 7 天的临时下载片段。", "安全", true, false, 7,
                    new[] { "*.crdownload", "*.part", "*.download", "*.tmp" }, KnownFolderPaths.Downloads),
                Rule("临时文件", "recycle-bin", "回收站（仅分析）", "回收站内容可能仍需恢复，本工具只统计并交由系统界面清理。", "谨慎", false, true, 0, null,
                    "%SystemDrive%\\$Recycle.Bin")
            };
        }

        private static CleanupRule Rule(
            string category,
            string id,
            string name,
            string description,
            string risk,
            bool recommended,
            bool scanOnly,
            int minimumAgeDays,
            string[] patterns,
            params string[] paths)
        {
            return new CleanupRule
            {
                Id = id,
                Name = name,
                Category = category,
                Description = description,
                Risk = risk,
                SearchPatterns = (patterns == null || patterns.Length == 0 ? new[] { "*" } : patterns).ToList(),
                Recursive = true,
                Recommended = recommended,
                ScanOnly = scanOnly,
                MinimumAgeDays = minimumAgeDays,
                PathTemplates = paths.ToList()
            };
        }

        private static CleanupRule MinimumSize(CleanupRule rule, long minimumBytes)
        {
            rule.MinimumBytes = minimumBytes;
            return rule;
        }
    }

    public sealed class CleanupService
    {
        internal static CleanupScanResult CreateSelection(CleanupScanResult source)
        {
            var files = source.Files.Where(source.SelectedFiles.Contains).ToList();
            long bytes = 0;
            foreach (string path in files)
            {
                try { bytes += new FileInfo(path).Length; } catch { }
            }
            return new CleanupScanResult
            {
                Rule = source.Rule,
                Files = files,
                SelectedFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase),
                Roots = source.Roots.ToList(),
                FileCount = files.Count,
                Bytes = bytes
            };
        }

        public Task<IList<CleanupScanResult>> ScanAsync(
            CleanupKind kind,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run<IList<CleanupScanResult>>(() =>
            {
                var results = new List<CleanupScanResult>();
                IList<string> whitelist = CleanupWhitelist.ReadAll();
                foreach (var rule in CleanupCatalog.GetRules(kind))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (progress != null) progress.Report("正在扫描：" + rule.Name);

                    var result = new CleanupScanResult
                    {
                        Rule = rule,
                        Files = new List<string>(),
                        SelectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        Roots = new List<string>()
                    };

                    foreach (string template in rule.PathTemplates)
                    {
                        foreach (string root in ResolveRoots(template))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (CleanupWhitelist.Contains(root, whitelist)) continue;
                            if (!IsSafeCleanupRoot(root)) continue;
                            if (File.Exists(root))
                            {
                                AddFile(root, rule, result, whitelist);
                                continue;
                            }
                            if (!Directory.Exists(root)) continue;
                            result.Roots.Add(root);
                            ScanRoot(root, rule, result, whitelist, cancellationToken);
                        }
                    }

                    if (rule.Recommended && !rule.ScanOnly)
                        result.SelectedFiles.UnionWith(result.Files);

                    results.Add(result);
                }
                return results;
            }, cancellationToken);
        }

        public Task<CleanupResult> CleanAsync(
            IEnumerable<CleanupScanResult> selected,
            string backupRoot,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var result = new CleanupResult();
                bool createBackup = !string.IsNullOrWhiteSpace(backupRoot);
                if (createBackup) BackupStore.Prune(5000, 1024L * 1024L * 1024L, backupRoot);
                foreach (CleanupScanResult item in selected)
                {
                    int deletedBefore = result.DeletedFiles;
                    int failedBefore = result.FailedFiles;
                    long bytesBefore = result.Bytes;
                    if (item.Rule.ScanOnly)
                    {
                        result.SkippedFiles += item.FileCount;
                        OperationLogger.Info("清理", item.Rule.Name + " 为仅分析项，未执行删除");
                        continue;
                    }
                    foreach (string file in item.Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (!File.Exists(file)) continue;
                            long length = 0;
                            try { length = new FileInfo(file).Length; } catch { }
                            string backupError;
                            BackupRecord backup;
                            if (createBackup && !BackupStore.TryBackup(file, backupRoot, out backup, out backupError))
                            {
                                result.FailedFiles++;
                                result.Errors.Add(file + "：" + backupError);
                                OperationLogger.Error("清理备份", backupError);
                                continue;
                            }
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                            result.DeletedFiles++;
                            result.Bytes += length;
                        }
                        catch (Exception ex)
                        {
                            result.FailedFiles++;
                            string message = file + "：" + ex.Message;
                            result.Errors.Add(message);
                            OperationLogger.Error("清理文件", message);
                        }
                    }

                    foreach (string root in item.Roots)
                    {
                        RemoveEmptyDirectories(root, cancellationToken);
                    }

                    if (progress != null) progress.Report("已完成：" + item.Rule.Name);
                    OperationLogger.Info(
                        "清理",
                        string.Format(
                            "{0}，请求 {1} 个文件，实际删除 {2} 个，释放 {3}，失败 {4} 个",
                            item.Rule.Name,
                            item.Files.Count,
                            result.DeletedFiles - deletedBefore,
                            DisplayFormat.Bytes(result.Bytes - bytesBefore),
                            result.FailedFiles - failedBefore));
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

            string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrWhiteSpace(systemRoot) ||
                !string.Equals(Path.GetPathRoot(full), systemRoot, StringComparison.OrdinalIgnoreCase)) return false;

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

            return full.Split(Path.DirectorySeparatorChar).Length >= 2;
        }

        internal static IEnumerable<string> ResolveRoots(string template)
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
            CleanupRule rule,
            CleanupScanResult result,
            IList<string> whitelist,
            CancellationToken cancellationToken)
        {
            var directories = new Stack<string>();
            directories.Push(root);

            while (directories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = directories.Pop();

                var uniqueFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string pattern in rule.SearchPatterns)
                {
                    string[] files;
                    try { files = Directory.GetFiles(current, pattern, SearchOption.TopDirectoryOnly); }
                    catch { files = new string[0]; }
                    foreach (string file in files) uniqueFiles.Add(file);
                }

                foreach (string file in uniqueFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddFile(file, rule, result, whitelist);
                }

                if (!rule.Recursive) continue;
                string[] children;
                try { children = Directory.GetDirectories(current); }
                catch { children = new string[0]; }
                foreach (string child in children)
                {
                    try
                    {
                        if (CleanupWhitelist.Contains(child, whitelist)) continue;
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) directories.Push(child);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void AddFile(string file, CleanupRule rule, CleanupScanResult result, IList<string> whitelist)
        {
            try
            {
                if (CleanupWhitelist.Contains(file, whitelist)) return;
                var info = new FileInfo(file);
                if (!info.Exists) return;
                if (rule.MinimumAgeDays > 0 && info.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-rule.MinimumAgeDays)) return;
                if (rule.MinimumBytes > 0 && info.Length < rule.MinimumBytes) return;
                result.Files.Add(file);
                result.FileCount++;
                result.Bytes += info.Length;
            }
            catch
            {
            }
        }

        private static void RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            var directories = new List<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                try
                {
                    foreach (string child in Directory.GetDirectories(current))
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                        directories.Add(child);
                        pending.Push(child);
                    }
                }
                catch
                {
                }
            }

            foreach (string directory in directories.OrderByDescending(value => value.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false); }
                catch { }
            }
        }
    }
}
