using System;
using System.IO;
using System.Linq;
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
            Run("Final navigation contract", TestFinalNavigation);
            Run("Disk analysis", TestDiskAnalysis);
            Run("Managed file classification", TestManagedFiles);
            Run("Migration catalog contract", TestMigrationCatalog);
            Run("Zyper optimization data", TestOptimizationData);

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
            string[] expectedCategories = { "过期文件", "系统相关", "缓存文件", "应用程序", "临时文件" };
            Assert(driveRules.Select(rule => rule.Category).Distinct().OrderBy(value => value)
                .SequenceEqual(expectedCategories.OrderBy(value => value)), "cleanup categories no longer match the final PRD");
            Assert(driveRules.Any(rule => rule.ScanOnly && rule.Name.Contains("WinSxS")), "high-risk WinSxS paths must remain scan-only");
            Assert(driveRules.All(rule => !rule.Name.Contains("QQ专清") && !rule.Name.Contains("微信专清")), "removed dedicated cleaners returned");

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

        private static void TestFinalNavigation()
        {
            string[] expected = { "C盘深度清理", "软件强力卸载", "系统智能优化", "磁盘文件管理器", "CMD 系统修复" };
            Assert(MainWindow.FinalModules.SequenceEqual(expected), "main navigation no longer matches the final five modules");
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

        private static void TestMigrationCatalog()
        {
            string[] expected = { "desktop", "documents", "downloads", "pictures", "videos", "appdata_cache", "temp" };
            var folders = MigrationService.Catalog();
            Assert(folders.Count == expected.Length, "migration catalog must contain exactly seven final-PRD folders");
            Assert(folders.Select(folder => folder.Key).OrderBy(value => value).SequenceEqual(expected.OrderBy(value => value)),
                "migration catalog keys do not match the final PRD");
            Assert(folders.All(folder => !string.IsNullOrWhiteSpace(folder.SourcePath)), "migration source path is empty");
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
                Assert(result.Extensions.Any(value => value.Extension == ".txt" && value.Bytes == 256), "extension usage missing");
                Assert(result.LargestFiles.First().Size == 256, "largest file ordering failed");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void TestOptimizationData()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bin", "ZyperData.xml");
            Assert(File.Exists(path), "ZyperData.xml was not copied to test output");
            XDocument document = XDocument.Load(path);
            var items = document.Descendants("Item").ToList();
            Assert(items.Count > 100, "optimization catalog is unexpectedly incomplete");
            Assert(items.All(item => item.Element("Optimize") != null), "an optimization entry has no Optimize section");
            Assert(items.All(item => item.Element("Restore") != null), "an optimization entry has no Restore section");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
