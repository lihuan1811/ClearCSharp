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
            Run("Uninstall command parsing", TestCommandParsing);
            Run("Cleanup catalog safety", TestCleanupCatalog);
            Run("Disk analysis", TestDiskAnalysis);
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

            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert(!CleanupService.IsSafeCleanupRoot(user), "user profile root must be protected");
            Assert(!CleanupService.IsSafeCleanupRoot(Path.GetPathRoot(Environment.CurrentDirectory)), "drive root must be protected");
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
