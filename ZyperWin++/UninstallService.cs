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
        public string InstallLocation { get; set; }
        public string InstallDate { get; set; }
        public string RegistryPath { get; set; }
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

        public Task<ProcessResult> CleanResidualsAsync(InstalledApp app, bool removeInstallDirectory, CancellationToken cancellationToken)
        {
            return Task.Run(() => CleanResiduals(app, removeInstallDirectory, cancellationToken), cancellationToken);
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
                                        InstallLocation = Convert.ToString(key.GetValue("InstallLocation")),
                                        InstallDate = Convert.ToString(key.GetValue("InstallDate")),
                                        RegistryPath = (hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") + UninstallPath + "\\" + subKeyName,
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
                "' + $_.Version + '" + separator + "' + $_.PublisherDisplayName + '" + separator + "' + $_.InstallLocation }";
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
                    InstallLocation = fields.Length > 4 ? fields[4].Trim() : string.Empty,
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
                if (arguments.StartsWith("/I", StringComparison.OrdinalIgnoreCase)) arguments = "/X" + arguments.Substring(2);
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

                process.WaitForExit();
                int exitCode = process.ExitCode;
                bool accepted = exitCode == 0 || exitCode == 1641 || exitCode == 3010;
                OperationLogger.Info("软件卸载", "卸载程序已结束：" + app.Name + "，退出码 " + exitCode);
                return new ProcessResult
                {
                    ExitCode = accepted ? 0 : exitCode,
                    Output = "卸载程序已结束，退出码 " + exitCode + "。",
                    Error = accepted ? string.Empty : "卸载程序返回退出码 " + exitCode
                };
            }
            catch (Exception ex)
            {
                return new ProcessResult { ExitCode = -1, Error = ex.Message, Output = string.Empty };
            }
        }

        private static ProcessResult CleanResiduals(InstalledApp app, bool removeInstallDirectory, CancellationToken cancellationToken)
        {
            var messages = new List<string>();
            var errors = new List<string>();
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(app.RegistryPath))
            {
                try
                {
                    ProcessResult query = ProcessRunner.Run("reg.exe", "query \"" + app.RegistryPath + "\"", 30000, cancellationToken);
                    if (!query.Success) messages.Add("卸载注册表项已不存在。");
                    else
                    {
                        string backupDirectory = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "CDiskGlow",
                            "registry_backups");
                        Directory.CreateDirectory(backupDirectory);
                        string safeName = string.Concat(app.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
                        string backup = Path.Combine(backupDirectory, safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".reg");
                        ProcessResult export = ProcessRunner.Run("reg.exe", "export \"" + app.RegistryPath + "\" \"" + backup + "\" /y", 60000, cancellationToken);
                        if (!export.Success) errors.Add("注册表备份失败：" + export.Error);
                        else
                        {
                            messages.Add("已备份卸载注册表：" + backup);
                            ProcessResult remove = ProcessRunner.Run("reg.exe", "delete \"" + app.RegistryPath + "\" /f", 60000, cancellationToken);
                            if (remove.Success) messages.Add("已删除卸载注册表残留。");
                            else errors.Add("删除卸载注册表残留失败：" + remove.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("注册表备份失败：" + ex.Message);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            RemoveStartupResiduals(app, Registry.CurrentUser, messages, errors);
            RemoveStartupResiduals(app, Registry.LocalMachine, messages, errors);
            cancellationToken.ThrowIfCancellationRequested();
            RemoveShortcuts(app, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), messages, errors);
            RemoveShortcuts(app, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), messages, errors);
            RemoveShortcuts(app, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), messages, errors);
            RemoveShortcuts(app, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), messages, errors);

            if (removeInstallDirectory && IsSafeInstallLocation(app.InstallLocation))
            {
                try
                {
                    if (Directory.Exists(app.InstallLocation))
                    {
                        Directory.Delete(app.InstallLocation, true);
                        messages.Add("已删除安装残留目录：" + app.InstallLocation);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("删除安装目录失败：" + ex.Message);
                }
            }

            string output = string.Join(Environment.NewLine, messages);
            string error = string.Join(Environment.NewLine, errors);
            OperationLogger.Info("卸载残留", app.Name + "，完成 " + messages.Count + " 项，失败 " + errors.Count + " 项");
            return new ProcessResult { ExitCode = errors.Count == 0 ? 0 : 1, Output = output, Error = error };
        }

        private static void RemoveStartupResiduals(InstalledApp app, RegistryKey hive, IList<string> messages, IList<string> errors)
        {
            try
            {
                using (RegistryKey run = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (run == null) return;
                    foreach (string valueName in run.GetValueNames())
                    {
                        string value = Convert.ToString(run.GetValue(valueName));
                        if (!MatchesApplication(app, valueName + " " + value)) continue;
                        run.DeleteValue(valueName, false);
                        messages.Add("已删除启动项：" + valueName);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add("清理启动项失败：" + ex.Message);
            }
        }

        private static void RemoveShortcuts(InstalledApp app, string root, IList<string> messages, IList<string> errors)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            try
            {
                foreach (string shortcut in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    if (!MatchesApplication(app, Path.GetFileNameWithoutExtension(shortcut))) continue;
                    File.Delete(shortcut);
                    messages.Add("已删除快捷方式：" + shortcut);
                }
            }
            catch (Exception ex)
            {
                errors.Add("清理快捷方式失败：" + ex.Message);
            }
        }

        private static bool MatchesApplication(InstalledApp app, string value)
        {
            string text = value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(app.InstallLocation) && text.IndexOf(app.InstallLocation, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string name = (app.Name ?? string.Empty).Trim();
            return name.Length >= 4 && text.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static bool IsSafeInstallLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location)) return false;
            string full;
            try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(location)).TrimEnd(Path.DirectorySeparatorChar); }
            catch { return false; }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (IsSameOrChild(full, windows)) return false;
            string[] protectedRoots =
            {
                Path.GetPathRoot(full),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            return protectedRoots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .All(root => !string.Equals(full, Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSameOrChild(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
