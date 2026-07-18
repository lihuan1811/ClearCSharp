using Microsoft.Win32;
using System;
using System.IO;

namespace ZyperWin__
{
    internal static class KnownFolderPaths
    {
        private const string UserShellFolders = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

        public static string Desktop => Resolve("Desktop", Environment.SpecialFolder.DesktopDirectory, "Desktop");
        public static string Documents => Resolve("Personal", Environment.SpecialFolder.MyDocuments, "Documents");
        public static string Downloads => Resolve("{374DE290-123F-4565-9164-39C4925E467B}", null, "Downloads");
        public static string Pictures => Resolve("My Pictures", Environment.SpecialFolder.MyPictures, "Pictures");
        public static string Videos => Resolve("My Video", Environment.SpecialFolder.MyVideos, "Videos");

        internal static string ResolveValue(string configuredValue, string fallback)
        {
            string candidate = Environment.ExpandEnvironmentVariables(configuredValue ?? string.Empty);
            if (string.IsNullOrWhiteSpace(candidate)) candidate = fallback;
            return Path.GetFullPath(candidate);
        }

        private static string Resolve(string registryValue, Environment.SpecialFolder? specialFolder, string fallbackName)
        {
            string configured = null;
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(UserShellFolders);
                configured = Convert.ToString(key?.GetValue(registryValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
            catch
            {
            }

            string fallback = specialFolder.HasValue ? Environment.GetFolderPath(specialFolder.Value) : string.Empty;
            if (string.IsNullOrWhiteSpace(fallback))
                fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallbackName);
            try { return ResolveValue(configured, fallback); }
            catch { return fallback; }
        }
    }
}
