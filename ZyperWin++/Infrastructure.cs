using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
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
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
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
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch
            {
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
            if (handler != null) handler(line);
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
