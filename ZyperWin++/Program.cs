using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ZyperWin__
{
    internal static class Program
    {
        private static Mutex singleInstance;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                return RunApplication(args);
            }
            catch (Exception ex)
            {
                if (Array.Exists(args ?? new string[0], value => string.Equals(value, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
                    WriteSmokeError(ex);
                else
                    MessageBox.Show(ex.ToString(), "C DiskGlow 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunApplication(string[] args)
        {
#if NETFRAMEWORK
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#else
            ApplicationConfiguration.Initialize();
#endif
            if (Array.Exists(args ?? new string[0], value => string.Equals(value, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
                return RunSmokeTest();

            bool createdNew;
            singleInstance = new Mutex(true, "CDiskGlow_Net8_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("C DiskGlow 已经在运行中。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            try
            {
                Application.Run(new MainWindow());
            }
            finally
            {
                singleInstance.ReleaseMutex();
                singleInstance.Dispose();
            }
            return 0;
        }

        private static int RunSmokeTest()
        {
            try
            {
                TraceSmoke("enter");
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs args)
                {
                    WriteSmokeError(args.Exception);
                    Environment.Exit(1);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
                {
                    WriteSmokeError(args.ExceptionObject as Exception ?? new Exception(Convert.ToString(args.ExceptionObject)));
                };
                TraceSmoke("handlers-ready");
                using (var shell = new MainWindow())
                {
                    TraceSmoke("main-window-constructed");
                    shell.CreateControl();
                    shell.PerformLayout();
                    if (shell.ContentHostForTests.Dock != DockStyle.Fill ||
                        shell.ContentHostForTests.Width < shell.ClientSize.Width - 2 ||
                        shell.ContentHostForTests.Height <= shell.ClientSize.Height / 2)
                        throw new InvalidOperationException("主内容区没有铺满窗口：" + shell.ContentHostForTests.Bounds);
                    shell.NavigateForTests(MainWindow.FinalModules[0]);
                    shell.PerformLayout();
                    if (shell.ContentHostForTests.Controls.Count != 1 ||
                        shell.ContentHostForTests.Controls[0].Dock != DockStyle.Fill ||
                        shell.ContentHostForTests.Controls[0].Size != shell.ContentHostForTests.ClientSize)
                        throw new InvalidOperationException("默认清理页面没有铺满主内容区。");
                    TraceSmoke("main-window-handle-created");
                }
                TraceSmoke("main-window-disposed");
                Func<Control>[] moduleFactories =
                {
                    () => new CleanupDashboard(),
                    () => new UninstallDashboard(),
                    () => new SystemOptimizationDashboard(),
                    () => new FileManagerDashboard(),
                    () => new RepairDashboard()
                };
                string[] moduleNames = { "cleanup", "uninstall", "optimization", "file-manager", "repair" };
                for (int index = 0; index < moduleFactories.Length; index++)
                {
                    using (Control module = moduleFactories[index]())
                    {
                        TraceSmoke(moduleNames[index] + "-constructed");
                        module.PerformLayout();
                    }
                    TraceSmoke(moduleNames[index] + "-disposed");
                }
                MigrationService.RunJunctionRoundTripSmokeTest(CancellationToken.None);
                TraceSmoke("migration-junction-round-trip-passed");
                TraceSmoke("complete");
                File.WriteAllText("smoke-test-ok.txt", "C DiskGlow module construction passed.");
                return 0;
            }
            catch (Exception ex)
            {
                WriteSmokeError(ex);
                return 1;
            }
        }

        private static void WriteSmokeError(Exception exception)
        {
            try { File.WriteAllText("smoke-test-error.txt", exception == null ? "Unknown smoke-test error." : exception.ToString()); }
            catch { }
        }

        private static void TraceSmoke(string message)
        {
            try { File.AppendAllText("smoke-test-trace.txt", DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); }
            catch { }
        }

    }
}
