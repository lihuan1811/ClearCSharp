using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZyperWin__
{
    public static class CleanupWhitelist
    {
        private static readonly object Sync = new object();
        private static readonly string StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDiskGlow",
            "cleanup_whitelist.txt");

        public static IList<string> ReadAll()
        {
            lock (Sync)
            {
                if (!File.Exists(StoragePath)) return new List<string>();
                try
                {
                    return File.ReadAllLines(StoragePath, Encoding.UTF8)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(Normalize)
                        .Where(value => value != null)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
                catch
                {
                    return new List<string>();
                }
            }
        }

        public static bool Add(string path, out string error)
        {
            error = string.Empty;
            string normalized = Normalize(path);
            if (normalized == null || (!File.Exists(normalized) && !Directory.Exists(normalized)))
            {
                error = "白名单路径不存在。";
                return false;
            }
            try
            {
                IList<string> entries = ReadAll();
                if (!entries.Contains(normalized, StringComparer.OrdinalIgnoreCase)) entries.Add(normalized);
                Write(entries);
                OperationLogger.Info("清理白名单", "添加：" + normalized);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void Remove(IEnumerable<string> paths)
        {
            var removed = new HashSet<string>(paths.Select(Normalize).Where(value => value != null), StringComparer.OrdinalIgnoreCase);
            Write(ReadAll().Where(value => !removed.Contains(value)).ToList());
            foreach (string path in removed) OperationLogger.Info("清理白名单", "移除：" + path);
        }

        public static bool Contains(string path, IList<string> entries)
        {
            string normalized = Normalize(path);
            if (normalized == null) return false;
            foreach (string entry in entries ?? ReadAll())
            {
                string root = Normalize(entry);
                if (root == null) continue;
                if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) return true;
                if (Directory.Exists(root) && normalized.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void Write(IEnumerable<string> entries)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StoragePath));
                File.WriteAllLines(StoragePath,
                    entries.Select(Normalize).Where(value => value != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray(),
                    new UTF8Encoding(false));
            }
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
                string root = Path.GetPathRoot(full);
                return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
                    ? full
                    : full.TrimEnd(Path.DirectorySeparatorChar);
            }
            catch { return null; }
        }
    }
}
