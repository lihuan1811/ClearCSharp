using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    internal static class FileSystemTools
    {
        public static void ReplaceFile(string temporaryPath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath);
                return;
            }

            string suffix = Guid.NewGuid().ToString("N");
            string backupPath = destinationPath + "." + suffix + ".bak";
            try
            {
                File.Replace(temporaryPath, destinationPath, backupPath, true);
            }
            catch
            {
                if (!File.Exists(temporaryPath)) throw;

                // Some Windows file systems do not implement File.Replace. Keep the
                // previous destination until the temporary file has been promoted.
                string displacedPath = destinationPath + "." + suffix + ".old";
                if (!File.Exists(destinationPath) && File.Exists(backupPath))
                    File.Move(backupPath, destinationPath);
                File.Move(destinationPath, displacedPath);
                bool promoted = false;
                try
                {
                    File.Move(temporaryPath, destinationPath);
                    promoted = true;
                }
                catch
                {
                    try
                    {
                        if (File.Exists(destinationPath)) File.Delete(destinationPath);
                        if (File.Exists(displacedPath)) File.Move(displacedPath, destinationPath);
                    }
                    catch (Exception rollbackError)
                    {
                        OperationLogger.Error("状态文件", "替换失败且回滚失败：" + rollbackError.Message);
                    }
                    throw;
                }
                finally
                {
                    if (promoted)
                    {
                        try { if (File.Exists(displacedPath)) File.Delete(displacedPath); }
                        catch { }
                    }
                }
            }
            try { if (File.Exists(backupPath)) File.Delete(backupPath); }
            catch { }
        }
    }

    internal sealed class AppOperationScope : IDisposable
    {
        private readonly Guid id;
        private int disposed;

        internal AppOperationScope(Guid id)
        {
            this.id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                AppOperationCoordinator.End(id);
        }
    }

    internal static class AppOperationCoordinator
    {
        private static readonly object Sync = new object();
        private static Guid activeId;
        private static string activeDescription;

        public static event Action<bool, string> Changed;

        public static bool IsBusy
        {
            get { lock (Sync) return activeId != Guid.Empty; }
        }

        public static string ActiveDescription
        {
            get { lock (Sync) return activeDescription ?? string.Empty; }
        }

        public static AppOperationScope Begin(string description)
        {
            Guid id = Guid.NewGuid();
            lock (Sync)
            {
                if (activeId != Guid.Empty)
                    throw new InvalidOperationException("正在执行“" + activeDescription + "”，请等待该操作完成。");
                activeId = id;
                activeDescription = string.IsNullOrWhiteSpace(description) ? "系统操作" : description.Trim();
            }
            RaiseChanged(true, ActiveDescription);
            return new AppOperationScope(id);
        }

        internal static void End(Guid id)
        {
            lock (Sync)
            {
                if (activeId != id) return;
                activeId = Guid.Empty;
                activeDescription = null;
            }
            RaiseChanged(false, string.Empty);
        }

        private static void RaiseChanged(bool busy, string description)
        {
            Action<bool, string> handler = Changed;
            if (handler == null) return;
            try { handler(busy, description); }
            catch (Exception ex) { OperationLogger.Error("操作状态", ex.Message); }
        }
    }

    internal static class AppPalette
    {
        public static readonly Color Green = Color.FromArgb(15, 143, 95);
        public static readonly Color GreenHover = Color.FromArgb(41, 166, 119);
        public static readonly Color GreenActive = Color.FromArgb(72, 177, 139);
        public static readonly Color PaleGreen = Color.FromArgb(235, 248, 242);
        public static readonly Color Canvas = Color.FromArgb(246, 248, 247);
        public static readonly Color Border = Color.FromArgb(214, 224, 219);
        public static readonly Color Text = Color.FromArgb(24, 39, 33);
        public static readonly Color Muted = Color.FromArgb(92, 112, 104);
        public static readonly Color Warning = Color.FromArgb(190, 116, 16);
    }

    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public bool TimedOut { get; set; }

        public bool Success
        {
            get { return !TimedOut && ExitCode == 0; }
        }
    }

    public static class ProcessRunner
    {
        static ProcessRunner()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static Task<ProcessResult> RunAsync(
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => Run(fileName, arguments, timeoutMilliseconds, cancellationToken), cancellationToken);
        }

        public static ProcessResult Run(
            string fileName,
            string arguments,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            var result = new ProcessResult { ExitCode = -1, Output = string.Empty, Error = string.Empty };
            var output = new StringBuilder();
            var error = new StringBuilder();
            Encoding processEncoding = OutputEncodingFor(fileName);

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = processEncoding,
                    StandardErrorEncoding = processEncoding
                };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null) output.AppendLine(args.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null) error.AppendLine(args.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var started = Stopwatch.StartNew();
                while (!process.WaitForExit(150))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKill(process);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (timeoutMilliseconds > 0 && started.ElapsedMilliseconds > timeoutMilliseconds)
                    {
                        result.TimedOut = true;
                        TryKill(process);
                        break;
                    }
                }

                if (!result.TimedOut)
                {
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }
            }

            result.Output = output.ToString().Trim();
            result.Error = error.ToString().Trim();
            return result;
        }

        public static Task<ProcessResult> RunPowerShellAsync(
            string script,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                "$OutputEncoding=[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                "$ErrorActionPreference='Stop';" + script));
            return RunAsync(
                "powershell.exe",
                "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                timeoutMilliseconds,
                cancellationToken);
        }

        private static void TryKill(Process process)
        {
            if (process == null) return;
            try
            {
                if (process.HasExited) return;

                // taskkill /T also terminates cmd and PowerShell descendants.
                using (var killer = new Process())
                {
                    killer.StartInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/PID " + process.Id + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };
                    killer.Start();
                    if (!killer.WaitForExit(10000))
                    {
                        try { killer.Kill(); } catch { }
                    }
                }
            }
            catch
            {
            }

            // taskkill itself can time out or return before the target exits.
            try
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit(5000);
            }
            catch { }
        }

        public static Encoding OutputEncodingFor(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            if (string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "pwsh", StringComparison.OrdinalIgnoreCase))
                return new UTF8Encoding(false, false);

            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                return Encoding.GetEncoding(codePage);
            }
            catch
            {
                return Encoding.Default;
            }
        }
    }

    public static class OperationLogger
    {
        private static readonly object Sync = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDiskGlow",
            "logs");
        private static readonly string LogFile = Path.Combine(LogDirectory, "operations.log");

        public static event Action<string> EntryWritten;

        public static string FilePath
        {
            get { return LogFile; }
        }

        public static void Info(string area, string message)
        {
            Write("INFO", area, message);
        }

        public static void Error(string area, string message)
        {
            Write("ERROR", area, message);
        }

        public static string ReadAll()
        {
            lock (Sync)
            {
                try
                {
                    return File.Exists(LogFile) ? File.ReadAllText(LogFile, Encoding.UTF8) : "暂无操作记录。";
                }
                catch (Exception ex)
                {
                    return "读取日志失败：" + ex.Message;
                }
            }
        }

        private static void Write(string level, string area, string message)
        {
            string line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss} [{1}] [{2}] {3}",
                DateTime.Now,
                level,
                area,
                (message ?? string.Empty).Replace("\r", " ").Replace("\n", " "));

            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogFile, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                }
            }

            var handler = EntryWritten;
            if (handler != null)
            {
                try { handler(line); }
                catch { }
            }
        }
    }

    public static class DisplayFormat
    {
        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double value = bytes;
            string[] units = { "KB", "MB", "GB", "TB", "PB" };
            int index = -1;
            do
            {
                value /= 1024d;
                index++;
            }
            while (value >= 1024d && index < units.Length - 1);
            return value.ToString(value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00") + " " + units[index];
        }

        public static string SingleLine(string text, int maximumLength)
        {
            string value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength) + "...";
        }
    }
}
