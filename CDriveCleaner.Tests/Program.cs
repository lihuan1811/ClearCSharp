using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using ZyperWin__;

namespace CDriveCleaner.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run("Byte formatting", TestByteFormatting);
            Run("Windows command output encoding", TestCommandOutputEncoding);
            Run("Uninstall command parsing", TestCommandParsing);
            Run("Cleanup catalog safety", TestCleanupCatalog);
            Run("Cleanup wildcard paths", TestCleanupWildcardPaths);
            Run("Cleanup per-file selection", TestCleanupFileSelection);
            Run("Cleanup execution truth", TestCleanupExecution);
            Run("Busy animation lifecycle", TestBusyAnimationLifecycle);
            Run("Final navigation contract", TestFinalNavigation);
            Run("Disk analysis", TestDiskAnalysis);
            Run("Disk analysis memory bound", TestDiskAnalysisMemoryBound);
            Run("Managed file classification", TestManagedFiles);
            Run("Overwrite delete disclosure", TestOverwriteDelete);
            Run("Backup roots and restore", TestBackupRootsAndRestore);
            Run("Uninstall residual identity safety", TestUninstallResidualIdentity);
            Run("Migration catalog contract", TestMigrationCatalog);
            Run("GPU safe operation parsing", TestGpuSafetyParsing);
            Run("Zyper optimization data", TestOptimizationData);
            Run("Optimization snapshot round trip", TestOptimizationSnapshotRoundTrip);

            Console.WriteLine(failures == 0
                ? "All C DiskGlow tests passed."
                : failures + " test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("FAIL  " + name + ": " + ex.Message);
            }
        }

        private static void TestByteFormatting()
        {
            Assert(DisplayFormat.Bytes(0) == "0 B", "0 bytes should stay in bytes");
            Assert(DisplayFormat.Bytes(1024) == "1.00 KB", "1024 bytes should be 1 KB");
            Assert(DisplayFormat.Bytes(1024L * 1024L * 5L) == "5.00 MB", "5 MiB should format correctly");
        }

        private static void TestCommandOutputEncoding()
        {
            Assert(ProcessRunner.OutputEncodingFor("powershell.exe").CodePage == 65001, "PowerShell output must stay UTF-8");
            Assert(ProcessRunner.OutputEncodingFor("dism.exe").CodePage ==
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage,
                "native Windows command output must use the local OEM code page");
        }

        private static void TestCommandParsing()
        {
            string executable;
            string arguments;
            CommandLineTools.SplitExecutable("\"C:\\Program Files\\Demo\\uninstall.exe\" /remove /quiet", out executable, out arguments);
            Assert(executable == "C:\\Program Files\\Demo\\uninstall.exe", "quoted executable parsing failed");
            Assert(arguments == "/remove /quiet", "quoted argument parsing failed");

            CommandLineTools.SplitExecutable("MsiExec.exe /I{12345678-1234-1234-1234-123456789012}", out executable, out arguments);
            Assert(executable == "MsiExec.exe", "MSI executable parsing failed");
            Assert(arguments.StartsWith("/I", StringComparison.Ordinal), "MSI arguments were lost");

            var app = new InstalledApp
            {
                UninstallCommand = "normal-uninstall.exe",
                QuietUninstallCommand = "quiet-uninstall.exe /silent"
            };
            Assert(CommandLineTools.PreferredUninstallCommand(app) == "quiet-uninstall.exe /silent",
                "quiet uninstall command must be preferred when available");
            app.QuietUninstallCommand = string.Empty;
            Assert(CommandLineTools.PreferredUninstallCommand(app) == "normal-uninstall.exe",
                "normal uninstall command must remain the fallback");
        }

        private static void TestCleanupCatalog()
        {
            foreach (CleanupKind kind in Enum.GetValues(typeof(CleanupKind)))
            {
                var rules = CleanupCatalog.GetRules(kind);
                Assert(rules.Count > 0, kind + " needs cleanup rules");
                Assert(rules.All(rule => rule.PathTemplates.Count > 0), kind + " contains an empty rule");
            }

            var driveRules = CleanupCatalog.GetRules(CleanupKind.DriveC);
            string[] expectedCategories = { "过期文件", "系统相关", "缓存文件", "应用程序", "临时文件", "微信缓存专清", "QQ缓存专清" };
            Assert(driveRules.Select(rule => rule.Category).Distinct().OrderBy(value => value)
                .SequenceEqual(expectedCategories.OrderBy(value => value)), "cleanup categories no longer match the final PRD");
            Assert(driveRules.Any(rule => rule.ScanOnly && rule.Name.Contains("WinSxS")), "high-risk WinSxS paths must remain scan-only");
            Assert(driveRules.Any(rule => rule.Id == "wechat-cache" && rule.Recommended), "WeChat safe cache rule is missing");
            CleanupRule wechatCache = driveRules.Single(rule => rule.Id == "wechat-cache");
            Assert(wechatCache.PathTemplates.Any(path => path.IndexOf("WXWork", StringComparison.OrdinalIgnoreCase) >= 0),
                "WeCom/WXWork cache roots are missing");
            Assert(wechatCache.PathTemplates.Any(path => path.IndexOf("xwechat_files", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.IndexOf("FileStorage", StringComparison.OrdinalIgnoreCase) >= 0),
                "xwechat FileStorage cache roots are missing");
            Assert(driveRules.Any(rule => rule.Id == "wechat-records" && rule.ScanOnly), "WeChat records must remain scan-only");
            Assert(driveRules.Any(rule => rule.Id == "qq-cache" && rule.Recommended), "QQ safe cache rule is missing");
            CleanupRule qqCache = driveRules.Single(rule => rule.Id == "qq-cache");
            Assert(qqCache.PathTemplates.Any(path => path.IndexOf("LOCALAPPDATA", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.IndexOf("Tencent\\QQ\\", StringComparison.OrdinalIgnoreCase) >= 0),
                "legacy LocalAppData QQ cache roots are missing");
            Assert(qqCache.PathTemplates.All(path =>
                path.IndexOf("Image", StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("Video", StringComparison.OrdinalIgnoreCase) < 0),
                "QQ recommended cache must not include local images or videos");
            Assert(driveRules.Any(rule => rule.Id == "qq-media" && !rule.Recommended), "QQ local media must be a separate opt-in rule");
            Assert(driveRules.Any(rule => rule.Id == "qq-records" && rule.ScanOnly), "QQ records must remain scan-only");
            Assert(driveRules.Any(rule => rule.Id == "large-packages" && rule.MinimumBytes == 50L * 1024L * 1024L),
                "large installer/archive/image scan rule is missing or has the wrong threshold");

            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert(!CleanupService.IsSafeCleanupRoot(user), "user profile root must be protected");
            Assert(!CleanupService.IsSafeCleanupRoot(Path.GetPathRoot(Environment.CurrentDirectory)), "drive root must be protected");

            string whitelistRoot = Path.Combine(Path.GetTempPath(), "CDiskGlowWhitelist_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(whitelistRoot, "child"));
            try
            {
                Assert(CleanupWhitelist.Contains(Path.Combine(whitelistRoot, "child", "file.tmp"), new[] { whitelistRoot }),
                    "cleanup whitelist must protect child paths");
            }
            finally
            {
                Directory.Delete(whitelistRoot, true);
            }
        }

        private static void TestCleanupWildcardPaths()
        {
            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowCleanupRoots_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "account-a", "Cache"));
            Directory.CreateDirectory(Path.Combine(root, "account-b", "Cache"));
            try
            {
                string[] resolved = CleanupService.ResolveRoots(Path.Combine(root, "*", "Cache")).OrderBy(value => value).ToArray();
                Assert(resolved.Length == 2, "account wildcard did not resolve all cache roots");
                Assert(resolved.All(Directory.Exists), "resolved cleanup root does not exist");

                string expectedDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Assert(KnownFolderPaths.ResolveValue(@"%USERPROFILE%\Downloads", string.Empty) == Path.GetFullPath(expectedDownloads),
                    "known-folder environment variables were not expanded");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void TestFinalNavigation()
        {
            string[] expected = { "C盘深度清理", "软件强力卸载", "系统智能优化", "磁盘文件管理器", "CMD 系统修复" };
            Assert(MainWindow.FinalModules.SequenceEqual(expected), "main navigation no longer matches the final five modules");
        }

        private static void TestCleanupFileSelection()
        {
            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowSelection_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string selectedPath = Path.Combine(root, "selected.tmp");
            string skippedPath = Path.Combine(root, "skipped.tmp");
            try
            {
                File.WriteAllBytes(selectedPath, new byte[32]);
                File.WriteAllBytes(skippedPath, new byte[64]);
                var scan = new CleanupScanResult
                {
                    Rule = new CleanupRule { Name = "test", ScanOnly = false },
                    Files = new List<string> { selectedPath, skippedPath },
                    SelectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selectedPath },
                    Roots = new List<string> { root },
                    FileCount = 2,
                    Bytes = 96
                };
                CleanupScanResult selection = CleanupService.CreateSelection(scan);
                Assert(selection.FileCount == 1 && selection.Files.Single() == selectedPath,
                    "cleanup must preserve the individual file selection");
                Assert(selection.Bytes == 32, "selected cleanup bytes must be recalculated from selected files");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestManagedFiles()
        {
            Assert(ManagedFileService.DetectType("movie.mp4") == ManagedFileType.Video, "video classification failed");
            Assert(ManagedFileService.DetectType("photo.png") == ManagedFileType.Image, "image classification failed");
            Assert(ManagedFileService.DetectType("setup.msi") == ManagedFileType.Installer, "installer classification failed");
            Assert(ManagedFileService.DetectType("archive.7z") == ManagedFileType.Archive, "archive classification failed");
            Assert(ManagedFileService.DetectType("report.pdf") == ManagedFileType.Document, "document classification failed");

            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowManagedFiles_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllBytes(Path.Combine(root, "large.mp4"), new byte[256]);
                File.WriteAllBytes(Path.Combine(root, "small.mp4"), new byte[64]);
                File.WriteAllBytes(Path.Combine(root, "ignore.txt"), new byte[512]);
                var service = new ManagedFileService();
                var files = service.ScanAsync(root, ManagedFileType.Video, 1, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(files.Count == 1 && files[0].Name == "large.mp4", "type filter or largest-file limit failed");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void TestCleanupExecution()
        {
            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowCleanupExecution_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "delete.tmp");
            try
            {
                File.WriteAllBytes(file, new byte[321]);
                var selection = new CleanupScanResult
                {
                    Rule = new CleanupRule { Name = "test", ScanOnly = false },
                    Files = new List<string> { file },
                    SelectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { file },
                    Roots = new List<string> { root },
                    FileCount = 1,
                    Bytes = 999999
                };
                CleanupResult result = new CleanupService().CleanAsync(new[] { selection }, null, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.DeletedFiles == 1 && result.FailedFiles == 0, "cleanup did not report the real deletion result");
                Assert(result.Bytes == 321, "cleanup reported scanned bytes instead of actually deleted bytes");
                Assert(!File.Exists(file), "cleanup returned success but the file still exists");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void TestOverwriteDelete()
        {
            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowOverwrite_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "secret.bin");
            try
            {
                File.WriteAllBytes(file, Enumerable.Repeat((byte)0x5a, 8192).ToArray());
                FileOperationSummary result = new ManagedFileService().ShredAsync(new[] { file }, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.Success && result.AffectedPaths.Contains(file), "overwrite delete did not complete");
                Assert(!File.Exists(file), "overwrite delete returned success but the file still exists");
                Assert(result.Notices.Any(value => value.Contains("不能保证物理不可恢复")), "overwrite delete must disclose storage limitations");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void TestBackupRootsAndRestore()
        {
            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowBackupRoot_" + Guid.NewGuid().ToString("N"));
            string sourceRoot = Path.Combine(Path.GetTempPath(), "CDiskGlowBackupSource_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(sourceRoot);
            string source = Path.Combine(sourceRoot, "restore.txt");
            try
            {
                File.WriteAllText(source, "original");
                BackupRecord record;
                string error;
                Assert(BackupStore.TryBackup(source, root, out record, out error), error ?? "backup failed");
                File.WriteAllText(source, "changed");
                BackupRecord discovered = BackupStore.ReadRecords().FirstOrDefault(value =>
                    string.Equals(value.BackupPath, record.BackupPath, StringComparison.OrdinalIgnoreCase));
                Assert(discovered != null, "backup stored outside the current preferred drive was not discovered");
                Assert(BackupStore.TryRestore(discovered, out error), error ?? "restore failed");
                Assert(File.ReadAllText(source) == "original", "restored file content is incorrect");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, true);
            }
        }

        private static void TestUninstallResidualIdentity()
        {
            var app = new InstalledApp
            {
                Name = "Demo Tool",
                InstallLocation = @"C:\Program Files\Demo Tool"
            };
            Assert(UninstallService.MatchesApplication(app, @"C:\Program Files\Demo Tool\demo.exe"),
                "install directory identity should match");
            Assert(UninstallService.MatchesApplication(app, "Demo Tool"), "exact app name should match");
            Assert(UninstallService.MatchesApplication(app, "Demo Tool 快捷方式"), "explicit shortcut name should match");
            Assert(!UninstallService.MatchesApplication(app, "Demo Toolkit Updater"),
                "generic name substring must not be treated as an uninstall residual");
            Assert(!UninstallService.MatchesApplication(new InstalledApp { Name = "ABCD" }, "prefix-ABCD-unrelated"),
                "a four-character substring must not authorize residual deletion");
        }

        private static void TestBusyAnimationLifecycle()
        {
            using (var overlay = new BusyAnimationOverlay())
            {
                Assert(!overlay.IsActive, "busy animation must start inactive");
                overlay.Start("正在扫描", "测试路径");
                Assert(overlay.IsActive, "busy animation did not start");
                overlay.UpdateMessage("正在读取文件");
                overlay.Stop();
                Assert(!overlay.IsActive && !overlay.Visible, "busy animation did not stop cleanly");
            }
        }

        private static void TestMigrationCatalog()
        {
            string[] expected = { "desktop", "documents", "downloads", "pictures", "videos", "appdata_cache", "temp" };
            var folders = MigrationService.Catalog();
            Assert(folders.Count == expected.Length, "migration catalog must contain exactly seven final-PRD folders");
            Assert(folders.Select(folder => folder.Key).OrderBy(value => value).SequenceEqual(expected.OrderBy(value => value)),
                "migration catalog keys do not match the final PRD");
            Assert(folders.All(folder => !string.IsNullOrWhiteSpace(folder.SourcePath)), "migration source path is empty");
            Assert(folders.Single(folder => folder.Key == "desktop").SourcePath == KnownFolderPaths.Desktop,
                "desktop migration must use the real User Shell Folders path");
            Assert(folders.Single(folder => folder.Key == "downloads").SourcePath == KnownFolderPaths.Downloads,
                "downloads migration must use the real User Shell Folders path");
            MigrationFolder appData = folders.Single(folder => folder.Key == "appdata_cache");
            Assert(appData.Locations.Count == 5, "AppData migration must include all five WeChat/QQ locations");
            Assert(appData.Locations.Any(location => location.Key == "local-tencent"), "Local Tencent migration location is missing");
            Assert(appData.Locations.Any(location => location.Key == "roaming-tencent"), "Roaming Tencent migration location is missing");
            Assert(appData.Locations.Any(location => location.SourcePath.EndsWith("WeChat Files", StringComparison.OrdinalIgnoreCase)),
                "WeChat Files migration location is missing");
            Assert(appData.Locations.Any(location => location.SourcePath.EndsWith("xwechat_files", StringComparison.OrdinalIgnoreCase)),
                "xwechat_files migration location is missing");
            Assert(appData.Locations.Any(location => location.SourcePath.EndsWith("Tencent Files", StringComparison.OrdinalIgnoreCase)),
                "Tencent Files migration location is missing");
        }

        private static void TestGpuSafetyParsing()
        {
            string guid = GpuService.ParsePowerSchemeGuid("Power Scheme GUID: 8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C  (High performance)");
            Assert(guid == "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "active power scheme GUID parsing failed");
            Assert(GpuService.ParsePowerSchemeGuid("no power scheme") == null, "invalid power scheme output must not produce a GUID");
            Assert(GpuService.ShaderCachePaths(GpuVendor.Nvidia).Any(path => path.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0),
                "NVIDIA shader cache catalog is missing");
            Assert(GpuService.ShaderCachePaths(GpuVendor.Amd).Any(path => path.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0),
                "AMD shader cache catalog is missing");
            Assert(GpuService.ShaderCachePaths(GpuVendor.Intel).Any(path => path.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0),
                "Intel shader cache catalog is missing");

            Assert(UninstallService.RegViewArgument(new InstalledApp { RegistryView = Microsoft.Win32.RegistryView.Registry32 }) == "/reg:32",
                "32-bit uninstall registry backup must preserve the registry view");
            Assert(UninstallService.RegViewArgument(new InstalledApp { RegistryView = Microsoft.Win32.RegistryView.Registry64 }) == "/reg:64",
                "64-bit uninstall registry backup must preserve the registry view");
        }

        private static void TestDiskAnalysis()
        {
            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            try
            {
                File.WriteAllBytes(Path.Combine(root, "one.bin"), new byte[128]);
                File.WriteAllBytes(Path.Combine(root, "nested", "two.txt"), new byte[256]);

                var service = new DiskAnalysisService();
                DiskAnalysisResult result = service.ScanAsync(root, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Root.FileCount == 2, "disk scan should count both files");
                Assert(result.Root.Size == 384, "disk scan should sum exact bytes");
                Assert(result.Root.PhysicalSize > 0, "disk scan did not read physical allocation size");
                Assert(result.Extensions.Any(value => value.Extension == ".txt" && value.Bytes == 256), "extension usage missing");
                Assert(result.Extensions.Any(value => value.Extension == ".txt" && value.PhysicalBytes > 0), "extension physical usage missing");
                Assert(result.LargestFiles.First().Size == 256, "largest file ordering failed");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void TestDiskAnalysisMemoryBound()
        {
            string root = Path.Combine(Path.GetTempPath(), "CDiskGlowBoundedScan_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                for (int index = 0; index < 12; index++)
                    File.WriteAllBytes(Path.Combine(root, "file-" + index + ".bin"), new byte[index + 1]);

                var service = new DiskAnalysisService(2);
                DiskAnalysisResult result = service.ScanAsync(root, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Root.FileCount == 12, "bounded disk scan lost file counts");
                Assert(result.Root.Size == 78, "bounded disk scan lost logical bytes");
                Assert(result.AggregatedFiles == 10, "bounded disk scan did not aggregate excess file nodes");
                Assert(result.Root.Children.Count == 3, "bounded disk scan retained more file detail than configured");
                Assert(result.Root.Children.Any(value => value.IsAggregate && value.FileCount == 10),
                    "bounded disk scan did not expose an aggregate treemap node");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void TestOptimizationData()
        {
            using Stream stream = typeof(CleanupCatalog).Assembly.GetManifestResourceStream("CDiskGlow.Embedded.ZyperData.xml");
            Assert(stream != null, "ZyperData.xml was not embedded in the application");
            XDocument document = XDocument.Load(stream);
            var items = document.Descendants("Item").ToList();
            Assert(items.Count > 100, "optimization catalog is unexpectedly incomplete");
            Assert(items.All(item => item.Element("Optimize") != null), "an optimization entry has no Optimize section");
            Assert(items.All(item => item.Element("Restore") != null), "an optimization entry has no Restore section");
            var supportedCommands = new HashSet<string>(StringComparer.Ordinal)
            {
                "RegWrite", "RegDelete", "SetServiceStart", "ExplorerNotify", "PowerCfg"
            };
            Assert(items.SelectMany(item => item.Element("Optimize").Elements())
                .All(command => supportedCommands.Contains(command.Name.LocalName)),
                "optimization catalog contains a command without reliable execution support");

            XElement diagnostic = items.Single(item => (string)item.Attribute("name") == "18、禁用诊断服务");
            Assert((string)diagnostic.Element("Optimize").Element("SetServiceStart").Attribute("Name") == "DPS",
                "diagnostic service rule must operate on DPS consistently");
            XElement contacts = items.Single(item => (string)item.Attribute("name") == "9、禁用应用访问联系人");
            Assert(contacts.Element("Optimize").Elements().All(command =>
                ((string)command.Attribute("Key") ?? string.Empty).EndsWith(@"ConsentStore\contacts", StringComparison.OrdinalIgnoreCase)),
                "contacts privacy rule writes to a different key than it checks");
        }

        private static void TestOptimizationSnapshotRoundTrip()
        {
            string id = Guid.NewGuid().ToString("N");
            string subKey = @"Software\CDiskGlowTests\" + id;
            string fullKey = @"HKEY_CURRENT_USER\" + subKey;
            string tag = "test-optimization-" + id;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey, true))
                    key.SetValue("Existing", "before", RegistryValueKind.String);

                XElement item = XElement.Parse(
                    "<Item><Optimize>" +
                    "<RegWrite Key=\"" + fullKey + "\" Value=\"Existing\" Type=\"REG_SZ\" Data=\"after\" />" +
                    "<RegWrite Key=\"" + fullKey + "\" Value=\"Created\" Type=\"REG_DWORD\" Data=\"7\" />" +
                    "<RegWrite Key=\"" + fullKey + "\" Value=\"UnsignedDword\" Type=\"REG_DWORD\" Data=\"4294967295\" />" +
                    "</Optimize><Restore /></Item>");

                OptimizationExecutionResult applied = ReliableOptimizationExecutor.Apply(item, tag);
                Assert(applied.Success, applied.Message);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, false))
                {
                    Assert(Convert.ToString(key.GetValue("Existing")) == "after", "optimization value was not applied");
                    Assert(Convert.ToInt32(key.GetValue("Created")) == 7, "second optimization command was not applied");
                    Assert(Convert.ToInt32(key.GetValue("UnsignedDword")) == -1, "unsigned DWORD configuration was not applied");
                }

                OptimizationExecutionResult restored = ReliableOptimizationExecutor.RestoreLatest(tag);
                Assert(restored.Success, restored.Message);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, false))
                {
                    Assert(Convert.ToString(key.GetValue("Existing")) == "before", "original registry value was not restored");
                    Assert(key.GetValue("Created") == null, "new registry value was not removed during restore");
                    Assert(key.GetValue("UnsignedDword") == null, "unsigned DWORD value was not removed during restore");
                }
                Assert(!ReliableOptimizationExecutor.JournaledTags().Contains(tag), "restored optimization snapshot was not removed");
            }
            finally
            {
                ReliableOptimizationExecutor.RestoreLatest(tag);
                Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
