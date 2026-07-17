using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ZyperWin__
{
    public sealed class BackupRecord
    {
        public DateTime CreatedAt { get; set; }
        public long Bytes { get; set; }
        public string SourcePath { get; set; }
        public string BackupPath { get; set; }
    }

    public static class BackupStore
    {
        private static readonly object Sync = new object();

        public static string RootPath
        {
            get
            {
                string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                try
                {
                    DriveInfo target = DriveInfo.GetDrives()
                        .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
                        .Where(drive => !string.Equals(drive.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(drive => drive.AvailableFreeSpace)
                        .FirstOrDefault();
                    if (target != null) return Path.Combine(target.RootDirectory.FullName, "C_DiskGlow_Backups");
                }
                catch
                {
                }
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CDiskGlow",
                    "backups");
            }
        }

        public static bool TryBackup(string sourcePath, out string error)
        {
            error = null;
            try
            {
                var source = new FileInfo(sourcePath);
                if (!source.Exists) return true;
                string root = RootPath;
                Directory.CreateDirectory(root);
                string key = Hash(source.FullName + "|" + DateTime.UtcNow.Ticks);
                string folder = Path.Combine(root, key.Substring(0, 2), key);
                Directory.CreateDirectory(folder);
                string destination = Path.Combine(folder, source.Name);
                File.Copy(source.FullName, destination, true);

                string line = string.Join("\t", new[]
                {
                    DateTime.UtcNow.ToString("o"),
                    source.Length.ToString(),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(source.FullName)),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(destination))
                });
                lock (Sync)
                {
                    File.AppendAllText(Path.Combine(root, "manifest.tsv"), line + Environment.NewLine, new UTF8Encoding(false));
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "备份失败：" + sourcePath + "，" + ex.Message;
                return false;
            }
        }

        public static IList<BackupRecord> ReadRecords()
        {
            var records = new List<BackupRecord>();
            string manifest = Path.Combine(RootPath, "manifest.tsv");
            if (!File.Exists(manifest)) return records;
            string[] lines;
            lock (Sync)
            {
                try { lines = File.ReadAllLines(manifest, Encoding.UTF8); }
                catch { return records; }
            }
            foreach (string line in lines)
            {
                string[] fields = line.Split('\t');
                if (fields.Length != 4) continue;
                try
                {
                    var record = new BackupRecord
                    {
                        CreatedAt = DateTime.Parse(fields[0]).ToLocalTime(),
                        Bytes = long.Parse(fields[1]),
                        SourcePath = Encoding.UTF8.GetString(Convert.FromBase64String(fields[2])),
                        BackupPath = Encoding.UTF8.GetString(Convert.FromBase64String(fields[3]))
                    };
                    if (File.Exists(record.BackupPath)) records.Add(record);
                }
                catch
                {
                }
            }
            return records.OrderByDescending(record => record.CreatedAt).ToList();
        }

        public static bool TryRestore(BackupRecord record, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(record.BackupPath)) throw new FileNotFoundException("备份文件不存在。", record.BackupPath);
                string parent = Path.GetDirectoryName(record.SourcePath);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                File.Copy(record.BackupPath, record.SourcePath, true);
                OperationLogger.Info("备份还原", record.SourcePath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void Prune(int maximumCount, long maximumBytes)
        {
            IList<BackupRecord> records = ReadRecords();
            long total = records.Sum(record => record.Bytes);
            foreach (BackupRecord record in records.OrderBy(value => value.CreatedAt).ToList())
            {
                if (records.Count <= maximumCount && total <= maximumBytes) break;
                try
                {
                    File.Delete(record.BackupPath);
                    total -= record.Bytes;
                    records.Remove(record);
                }
                catch
                {
                }
            }
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return string.Concat(bytes.Select(item => item.ToString("x2")));
            }
        }
    }
}
