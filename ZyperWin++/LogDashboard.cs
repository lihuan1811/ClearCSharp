using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class LogDashboard : UserControl
    {
        private readonly TextBox logBox = new TextBox();

        public LogDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "操作日志",
                "扫描、清理、卸载、优化、显卡检测与系统修复都会写入本地日志。",
                out headerActions);
            Button refresh = UiFactory.PrimaryButton("刷新");
            Button openFolder = UiFactory.SecondaryButton("打开日志目录");
            refresh.Width = 82;
            openFolder.Width = 116;
            headerActions.Controls.Add(refresh);
            headerActions.Controls.Add(openFolder);

            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.WordWrap = false;
            logBox.ReadOnly = true;
            logBox.BackColor = Color.White;
            logBox.ForeColor = AppPalette.Text;
            logBox.Font = new Font("Consolas", 9F);

            Controls.Add(logBox);
            Controls.Add(header);

            refresh.Click += delegate { RefreshLog(); };
            openFolder.Click += delegate
            {
                try
                {
                    string folder = System.IO.Path.GetDirectoryName(OperationLogger.FilePath);
                    System.IO.Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "无法打开日志目录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            Load += delegate { RefreshLog(); };
            OperationLogger.EntryWritten += OnEntryWritten;
            Disposed += delegate { OperationLogger.EntryWritten -= OnEntryWritten; };
        }

        private void RefreshLog()
        {
            logBox.Text = OperationLogger.ReadAll();
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void OnEntryWritten(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnEntryWritten), line);
                return;
            }
            logBox.AppendText((logBox.TextLength == 0 ? string.Empty : Environment.NewLine) + line);
        }
    }
}
