using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class RepairDashboard : UserControl
    {
        private sealed class RepairAction
        {
            public string Name;
            public string Description;
            public string FileName;
            public string Arguments;
            public bool ChangesSystem;
            public int Timeout;
        }

        private readonly ListView list = new ListView();
        private readonly TextBox output = new TextBox();
        private readonly Button runButton = UiFactory.PrimaryButton("执行选中");
        private readonly Button cancelButton = UiFactory.SecondaryButton("取消");
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label status = UiFactory.StatusLabel("选择修复项目后执行，命令输出会完整保留在下方。 ");
        private readonly IList<RepairAction> actions;
        private CancellationTokenSource cancellation;

        public RepairDashboard()
        {
            actions = new List<RepairAction>
            {
                new RepairAction { Name = "DISM 快速检查", Description = "检查组件存储是否已被标记损坏", FileName = "dism.exe", Arguments = "/Online /Cleanup-Image /CheckHealth", Timeout = 120000 },
                new RepairAction { Name = "DISM 深度扫描", Description = "扫描 Windows 组件存储损坏", FileName = "dism.exe", Arguments = "/Online /Cleanup-Image /ScanHealth", Timeout = 1200000 },
                new RepairAction { Name = "DISM 修复组件", Description = "使用 Windows Update 修复组件存储", FileName = "dism.exe", Arguments = "/Online /Cleanup-Image /RestoreHealth", ChangesSystem = true, Timeout = 1800000 },
                new RepairAction { Name = "SFC 系统文件检查", Description = "扫描并修复受保护的 Windows 系统文件", FileName = "sfc.exe", Arguments = "/scannow", ChangesSystem = true, Timeout = 1800000 },
                new RepairAction { Name = "CHKDSK 在线扫描", Description = "在线扫描 C 盘文件系统，不安排重启修复", FileName = "chkdsk.exe", Arguments = "C: /scan", Timeout = 1200000 },
                new RepairAction { Name = "重置 Winsock", Description = "重置网络套接字目录，完成后通常需要重启", FileName = "netsh.exe", Arguments = "winsock reset", ChangesSystem = true, Timeout = 120000 }
            };

            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "系统修复",
                "调用 Windows 官方 DISM、SFC、CHKDSK 与 netsh 工具。",
                out headerActions);
            runButton.Width = 102;
            cancelButton.Width = 84;
            cancelButton.Enabled = false;
            headerActions.Controls.Add(runButton);
            headerActions.Controls.Add(cancelButton);

            list.Dock = DockStyle.Top;
            list.Height = 230;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.Font = UiFactory.BaseFont;
            list.Columns.Add("修复项目", 220);
            list.Columns.Add("说明", 620);
            list.Columns.Add("影响", 130);
            foreach (RepairAction action in actions)
            {
                var item = new ListViewItem(action.Name);
                item.SubItems.Add(action.Description);
                item.SubItems.Add(action.ChangesSystem ? "会修改系统" : "仅检查");
                item.Tag = action;
                list.Items.Add(item);
            }
            if (list.Items.Count > 0) list.Items[0].Selected = true;

            output.Dock = DockStyle.Fill;
            output.Multiline = true;
            output.ScrollBars = ScrollBars.Both;
            output.ReadOnly = true;
            output.WordWrap = false;
            output.Font = new Font("Consolas", 9F);
            output.BackColor = Color.White;
            output.ForeColor = AppPalette.Text;

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                ColumnCount = 2,
                Padding = new Padding(0, 6, 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240F));
            bottom.Controls.Add(status, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8, 5, 0, 5);
            bottom.Controls.Add(progress, 1, 0);

            Controls.Add(output);
            Controls.Add(bottom);
            Controls.Add(list);
            Controls.Add(header);

            runButton.Click += async delegate { await RunSelectedAsync(); };
            cancelButton.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
        }

        private async Task RunSelectedAsync()
        {
            if (list.SelectedItems.Count == 0)
            {
                MessageBox.Show("请选择一个修复项目。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var action = list.SelectedItems[0].Tag as RepairAction;
            if (action == null) return;

            if (action.ChangesSystem)
            {
                DialogResult answer = MessageBox.Show(
                    action.Description + "\n\n此操作会修改系统状态，是否继续？",
                    "确认系统修复",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }

            cancellation = new CancellationTokenSource();
            SetBusy(true);
            output.Clear();
            output.AppendText("> " + action.FileName + " " + action.Arguments + Environment.NewLine + Environment.NewLine);
            status.Text = "正在执行：" + action.Name;
            try
            {
                ProcessResult result = await ProcessRunner.RunAsync(
                    action.FileName,
                    action.Arguments,
                    action.Timeout,
                    cancellation.Token);
                output.AppendText(result.Output + Environment.NewLine);
                if (!string.IsNullOrWhiteSpace(result.Error)) output.AppendText(Environment.NewLine + result.Error);
                status.Text = result.Success ? "执行完成：" + action.Name : "执行失败，退出码 " + result.ExitCode;
                if (result.Success) OperationLogger.Info("系统修复", action.Name + " 执行成功");
                else OperationLogger.Error("系统修复", action.Name + "，" + DisplayFormat.SingleLine(result.Error, 180));
            }
            catch (OperationCanceledException)
            {
                status.Text = "操作已取消。";
                output.AppendText(Environment.NewLine + "操作已取消。" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                status.Text = "执行失败：" + ex.Message;
                output.AppendText(Environment.NewLine + ex + Environment.NewLine);
                OperationLogger.Error("系统修复", action.Name + "：" + ex.Message);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            runButton.Enabled = !busy;
            cancelButton.Enabled = busy;
            list.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) progress.Value = 0;
        }
    }
}
