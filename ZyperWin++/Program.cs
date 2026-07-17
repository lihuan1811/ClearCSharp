using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ZyperWin__
{
    internal static class Program
    {
        private static Mutex singleInstance;

        [STAThread]
        private static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
            bool createdNew;
            singleInstance = new Mutex(true, "CDiskGlow_Net48_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("C DiskGlow 已经在运行中。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainWindow());
            }
            finally
            {
                singleInstance.ReleaseMutex();
                singleInstance.Dispose();
            }
        }

        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            if (!string.Equals(requested.Name, "AntdUI", StringComparison.OrdinalIgnoreCase)) return null;

            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("CDiskGlow.Embedded.AntdUI.dll"))
            {
                if (stream == null) return null;
                if (stream.Length > int.MaxValue) return null;
                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                return Assembly.Load(bytes);
            }
        }
    }
}
