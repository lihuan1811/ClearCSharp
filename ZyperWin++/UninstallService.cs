using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZyperWin__
{
    public enum InstalledAppKind
    {
        Desktop,
        Store
    }

    public sealed class InstalledApp
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public long EstimatedBytes { get; set; }
        public string UninstallCommand { get; set; }
        public string QuietUninstallCommand { get; set; }
        public InstalledAppKind Kind { get; set; }

        public string SizeText
        {
            get { return EstimatedBytes > 0 ? DisplayFormat.Bytes(EstimatedBytes) : "--"; }
        }
    }

    public static class CommandLineTools
    {
        public static void SplitExecutable(string commandLine, out string executable, out string arguments)
        {
            executable = string.Empty;
            arguments = string.Empty;
            string value = Environment.ExpandEnvironmentVariables(commandLine ?? string.Empty).Trim();
            if (value.Length == 0) return;

            if (value[0] == '"')
            {
                int closing = value.IndexOf('"', 1);
                if (closing > 0)
                {
                    executable = value.Substring(1, closing - 1);
                    arguments = value.Substring(closing + 1).Trim();
                    return;
                }
            }

            int exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                int end = exeIndex + 4;
                executable = value.Substring(0, end).Trim().Trim('"');
                arguments = value.Substring(end).Trim();
                return;
            }

            int separator = value.IndexOf(' ');
            executable = separator < 0 ? value : value.Substring(0, separator);
            arguments = separator < 0 ? string.Empty : value.Substring(separator + 1).Trim();
        }

        public static string EscapePowerShellLiteral(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }
    }

    public sealed class UninstallService
    {
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public Task<IList<InstalledApp>> LoadAsync(InstalledAppKind kind, CancellationToken cancellationToken)
        {
            return kind == InstalledAppKind.Store
                ? LoadStoreAppsAsync(cancellationToken)
                : Task.Run<IList<InstalledApp>>(() => LoadDesktopApps(cancellationToken), cancellationToken);
        }

        public Task<ProcessResult> UninstallAsync(InstalledApp app, CancellationToken cancellationToken)
        {
            if (app.Kind == InstalledAppKind.Store)
            {
                string script = "Remove-AppxPackage -Package '" +
                    CommandLineTools.EscapePowerShellLiteral(app.Id) + "' -ErrorAction Stop";
                return ProcessRunner.RunPowerShellAsync(script, 180000, cancellationToken);
            }

            return Task.Run(() => LaunchDesktopUninstaller(app), cancellationToken);
        }

        private static IList<InstalledApp> LoadDesktopApps(CancellationToken cancellationToken)
        {
            var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            ReadRegistryView(apps, RegistryHive.LocalMachine, RegistryView.Registry64, cancellationToken);
            ReadRegistryView(apps, RegistryHive.LocalMachine, RegistryView.Registry32, cancellationToken);
            ReadRegistryView(apps, RegistryHive.CurrentUser, RegistryView.Registry64, cancellationToken);
            ReadRegistryView(apps, RegistryHive.CurrentUser, RegistryView.Registry32, cancellationToken);
            return apps.Values.OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void ReadRegistryView(
            IDictionary<string, InstalledApp> apps,
            RegistryHive hive,
            RegistryView view,
            CancellationToken cancellationToken)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey root = baseKey.OpenSubKey(UninstallPath))
                {
                    if (root == null) return;
                    foreach (string subKeyName in root.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            using (RegistryKey key = root.OpenSubKey(subKeyName))
                            {
                                if (key == null) continue;
                                string name = Convert.ToString(key.GetValue("DisplayName"), CultureInfo.CurrentCulture);
                                string uninstall = Convert.ToString(key.GetValue("UninstallString"), CultureInfo.CurrentCulture);
                                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uninstall)) continue;
                                if (Convert.ToInt32(key.GetValue("SystemComponent", 0), CultureInfo.InvariantCulture) == 1) continue;

                                long estimatedBytes = 0;
                                object estimatedSize = key.GetValue("EstimatedSize");
                                if (estimatedSize != null)
                                {
                                    long kilobytes;
                                    if (long.TryParse(Convert.ToString(estimatedSize, CultureInfo.InvariantCulture), out kilobytes))
                                        estimatedBytes = kilobytes * 1024L;
                                }

                                string identity = name + "|" + Convert.ToString(key.GetValue("DisplayVersion"));
                                if (!apps.ContainsKey(identity))
                                {
                                    apps[identity] = new InstalledApp
                                    {
                                        Id = subKeyName,
                                        Name = name.Trim(),
                                        Version = Convert.ToString(key.GetValue("DisplayVersion")),
                                        Publisher = Convert.ToString(key.GetValue("Publisher")),
                                        EstimatedBytes = estimatedBytes,
                                        UninstallCommand = uninstall,
                                        QuietUninstallCommand = Convert.ToString(key.GetValue("QuietUninstallString")),
                                        Kind = InstalledAppKind.Desktop
                                    };
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static async Task<IList<InstalledApp>> LoadStoreAppsAsync(CancellationToken cancellationToken)
        {
            const string separator = "\u001f";
            string script =
                "Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | " +
                "ForEach-Object { $_.PackageFullName + '" + separator + "' + $_.Name + '" + separator +
                "' + $_.Version + '" + separator + "' + $_.PublisherDisplayName }";
            ProcessResult result = await ProcessRunner.RunPowerShellAsync(script, 120000, cancellationToken);
            if (!result.Success) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "读取商城应用失败。" : result.Error);

            var apps = new List<InstalledApp>();
            foreach (string line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split(new[] { '\u001f' });
                if (fields.Length < 2) continue;
                apps.Add(new InstalledApp
                {
                    Id = fields[0].Trim(),
                    Name = fields[1].Trim(),
                    Version = fields.Length > 2 ? fields[2].Trim() : string.Empty,
                    Publisher = fields.Length > 3 ? fields[3].Trim() : string.Empty,
                    Kind = InstalledAppKind.Store
                });
            }
            return apps.OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static ProcessResult LaunchDesktopUninstaller(InstalledApp app)
        {
            string command = string.IsNullOrWhiteSpace(app.UninstallCommand)
                ? app.QuietUninstallCommand
                : app.UninstallCommand;
            string executable;
            string arguments;
            CommandLineTools.SplitExecutable(command, out executable, out arguments);

            if (string.IsNullOrWhiteSpace(executable))
            {
                return new ProcessResult { ExitCode = -1, Error = "卸载命令为空。", Output = string.Empty };
            }

            if (string.Equals(Path.GetFileName(executable), "msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(executable), "msiexec", StringComparison.OrdinalIgnoreCase))
            {
                arguments = arguments.Replace("/I", "/X").Replace("/i", "/x");
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = true,
                    WorkingDirectory = Directory.Exists(Path.GetDirectoryName(executable))
                        ? Path.GetDirectoryName(executable)
                        : Environment.CurrentDirectory
                };
                Process process = Process.Start(startInfo);
                if (process == null)
                    return new ProcessResult { ExitCode = -1, Error = "无法启动卸载程序。", Output = string.Empty };

                OperationLogger.Info("软件卸载", "已启动桌面应用卸载程序：" + app.Name);
                return new ProcessResult { ExitCode = 0, Output = "卸载程序已启动。", Error = string.Empty };
            }
            catch (Exception ex)
            {
                return new ProcessResult { ExitCode = -1, Error = ex.Message, Output = string.Empty };
            }
        }
    }
}
