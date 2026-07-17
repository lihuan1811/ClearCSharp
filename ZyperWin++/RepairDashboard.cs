using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class RepairDashboard : UserControl
    {
        private sealed class RepairAction
        {
            public string Id;
            public string Name;
            public string Risk;
            public string Description;
            public string FileName;
            public string Arguments;
            public string DisplayCommand;
            public bool Recommended;
            public bool Deep;
            public int Timeout;
        }

        private readonly DataGridView grid = UiFactory.Grid();
        private readonly TextBox output = new TextBox();
        private readonly Button recommendedButton = UiFactory.SecondaryButton("推荐安全修复");
        private readonly Button deepButton = UiFactory.SecondaryButton("深度系统修复");
        private readonly Button runButton = UiFactory.PrimaryButton("执行选中修复");
        private readonly Button cancelButton = UiFactory.SecondaryButton("取消");
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label status = UiFactory.StatusLabel("推荐模式已选择安全修复项，也可以单独执行任意一项。");
        private readonly IList<RepairAction> actions;
        private CancellationTokenSource cancellation;
        private bool deepMode;

        public RepairDashboard()
        {
            actions = CreateActions();
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "CMD 系统修复工具箱",
                "仅调用微软原生命令，提供推荐安全修复、深度故障修复和独立可选项。",
                out headerActions);
            runButton.Width = 126;
            cancelButton.Width = 78;
            cancelButton.Enabled = false;
            headerActions.Controls.Add(runButton);
            headerActions.Controls.Add(cancelButton);

            var modeBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(0, 8, 0, 6),
                WrapContents = false,
                BackColor = AppPalette.Canvas
            };
            recommendedButton.Width = 126;
            deepButton.Width = 126;
            modeBar.Controls.Add(recommendedButton);
            modeBar.Controls.Add(deepButton);

            ConfigureGrid();

            output.Dock = DockStyle.Bottom;
            output.Height = 145;
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

            Controls.Add(grid);
            Controls.Add(output);
            Controls.Add(bottom);
            Controls.Add(modeBar);
            Controls.Add(header);

            recommendedButton.Click += delegate { SelectMode(false); };
            deepButton.Click += delegate { SelectMode(true); };
            runButton.Click += async delegate { await RunSelectedAsync(); };
            cancelButton.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            grid.CellContentClick += async delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex >= 0 && grid.Columns[args.ColumnIndex].Name == "Run")
                {
                    var action = grid.Rows[args.RowIndex].Tag as RepairAction;
                    if (action != null) await RunActionsAsync(new List<RepairAction> { action });
                }
            };
            SelectMode(false);
        }

        private void ConfigureGrid()
        {
            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "选择", Width = 58, ReadOnly = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "修复项", Width = 190 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Risk", HeaderText = "风险", Width = 72 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "说明", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Command", HeaderText = "底层命令", Width = 320 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Run", HeaderText = "单独执行", Text = "执行", UseColumnTextForButtonValue = true, Width = 88 });
            foreach (DataGridViewColumn column in grid.Columns)
                if (column.Name != "Selected" && column.Name != "Run") column.ReadOnly = true;
        }

        private void SelectMode(bool deep)
        {
            if (deep && !deepMode)
            {
                if (MessageBox.Show("深度修复包含 DISM、CHKDSK /F /R、系统更新组件重置和商店缓存重置，耗时较长且可能需要重启。\n\n是否继续选择？",
                    "深度系统修复风险确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            deepMode = deep;
            recommendedButton.BackColor = deep ? Color.White : AppPalette.Green;
            recommendedButton.ForeColor = deep ? AppPalette.Green : Color.White;
            deepButton.BackColor = deep ? AppPalette.Green : Color.White;
            deepButton.ForeColor = deep ? Color.White : AppPalette.Green;
            PopulateActions();
            status.Text = deep ? "深度模式已选择全部修复项。" : "推荐模式已选择 4 个安全修复项。";
        }

        private void PopulateActions()
        {
            grid.Rows.Clear();
            foreach (RepairAction action in actions)
            {
                int index = grid.Rows.Add(deepMode || action.Recommended, action.Name, action.Risk, action.Description, action.DisplayCommand, "执行");
                grid.Rows[index].Tag = action;
                if (action.Deep) grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 225);
            }
        }

        private async Task RunSelectedAsync()
        {
            grid.EndEdit();
            var selected = new List<RepairAction>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (Convert.ToBoolean(row.Cells["Selected"].Value ?? false) && row.Tag is RepairAction)
                    selected.Add((RepairAction)row.Tag);
            }
            if (selected.Count == 0)
            {
                MessageBox.Show("请先勾选修复项。", "系统修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await RunActionsAsync(selected);
        }

        private async Task RunActionsAsync(IList<RepairAction> selected)
        {
            bool hasDeep = selected.Any(action => action.Deep);
            string names = string.Join("、", selected.Select(action => action.Name));
            string warning = "即将执行：" + names + "。";
            if (hasDeep) warning += "\n\n其中包含深度修复，可能耗时较长、重置系统组件或要求重启。";
            if (MessageBox.Show(warning + "\n\n是否继续？", hasDeep ? "深度修复确认" : "确认系统修复",
                MessageBoxButtons.YesNo, hasDeep ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;

            cancellation = new CancellationTokenSource();
            SetBusy(true);
            progress.Style = ProgressBarStyle.Continuous;
            progress.Minimum = 0;
            progress.Maximum = selected.Count;
            progress.Value = 0;
            output.Clear();
            int succeeded = 0;
            var failures = new List<string>();
            try
            {
                for (int index = 0; index < selected.Count; index++)
                {
                    RepairAction action = selected[index];
                    status.Text = string.Format("正在执行 {0}/{1}：{2}", index + 1, selected.Count, action.Name);
                    output.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + action.Name + Environment.NewLine);
                    output.AppendText("> " + action.DisplayCommand + Environment.NewLine);
                    ProcessResult result = await RunActionAsync(action, cancellation.Token);
                    if (!string.IsNullOrWhiteSpace(result.Output)) output.AppendText(result.Output + Environment.NewLine);
                    if (!string.IsNullOrWhiteSpace(result.Error)) output.AppendText(result.Error + Environment.NewLine);
                    output.AppendText("退出码：" + result.ExitCode + Environment.NewLine + Environment.NewLine);
                    if (result.Success)
                    {
                        succeeded++;
                        OperationLogger.Info("系统修复", action.Name + " 执行成功");
                    }
                    else
                    {
                        failures.Add(action.Name + "（退出码 " + result.ExitCode + "）");
                        OperationLogger.Error("系统修复", action.Name + "，" + DisplayFormat.SingleLine(result.Error, 180));
                    }
                    progress.Value = index + 1;
                }
                status.Text = string.Format("修复执行完成：成功 {0}，失败 {1}。", succeeded, failures.Count);
                if (failures.Count > 0)
                    MessageBox.Show(string.Join(Environment.NewLine, failures), "部分修复失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                status.Text = "系统修复已取消。";
                output.AppendText("操作已取消。" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                status.Text = "执行失败：" + DisplayFormat.SingleLine(ex.Message, 160);
                output.AppendText(ex + Environment.NewLine);
                OperationLogger.Error("系统修复", ex.Message);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                if (!IsDisposed) SetBusy(false);
            }
        }

        private static Task<ProcessResult> RunActionAsync(RepairAction action, CancellationToken cancellationToken)
        {
            if (action.Id == "dism_restore_health" && Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1)
                return ProcessRunner.RunAsync("sfc.exe", "/scannow", action.Timeout, cancellationToken);
            return ProcessRunner.RunAsync(action.FileName, action.Arguments, action.Timeout, cancellationToken);
        }

        private static IList<RepairAction> CreateActions()
        {
            var actions = new List<RepairAction>
            {
                Action("sfc_scan", "SFC 系统文件修复", "安全", "检查并修复受保护系统文件。", "sfc.exe", "/scannow", "sfc /scannow", true, false, 1800000),
                Action("chkdsk_scan", "CHKDSK 磁盘安全扫描", "安全", "以只读方式检查 C 盘文件系统错误。", "chkdsk.exe", "C:", "chkdsk C:", true, false, 1200000),
                Action("flush_dns", "DNS 刷新", "安全", "清空 DNS 解析缓存。", "ipconfig.exe", "/flushdns", "ipconfig /flushdns", true, false, 120000),
                Action("winsock_reset", "Winsock 网络重置", "安全", "重置 Windows 网络套接字目录，完成后通常需要重启。", "netsh.exe", "winsock reset", "netsh winsock reset", true, false, 120000),
                Action("dism_restore_health", "DISM 系统镜像修复", "谨慎", "使用 DISM 修复系统组件仓库；Windows 7 自动回退到 SFC。", "dism.exe", "/Online /Cleanup-Image /RestoreHealth", "DISM /Online /Cleanup-Image /RestoreHealth", false, true, 1800000),
                Action("chkdsk_deep", "磁盘错误深度修复", "谨慎", "安排 C 盘深度修复，可能需要重启。", "cmd.exe", "/D /C echo Y|chkdsk C: /F /R", "echo Y|chkdsk C: /F /R", false, true, 1800000),
                Action("windows_update_reset", "系统更新组件修复", "谨慎", "停止更新服务并重建 SoftwareDistribution 和 catroot2。", "cmd.exe",
                    "/D /C net stop wuauserv & net stop bits & net stop cryptsvc & if exist %systemroot%\\SoftwareDistribution.old rmdir /S /Q %systemroot%\\SoftwareDistribution.old & if exist %systemroot%\\System32\\catroot2.old rmdir /S /Q %systemroot%\\System32\\catroot2.old & ren %systemroot%\\SoftwareDistribution SoftwareDistribution.old & ren %systemroot%\\System32\\catroot2 catroot2.old & net start cryptsvc & net start bits & net start wuauserv",
                    "重建 SoftwareDistribution 和 catroot2", false, true, 600000)
            };
            string wsreset = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsreset.exe");
            actions.Add(Action("cache_reset", "缓存重置修复", "谨慎", "重置微软商店缓存。", File.Exists(wsreset) ? wsreset : "wsreset.exe", string.Empty,
                "wsreset.exe", false, true, 300000));
            return actions;
        }

        private static RepairAction Action(string id, string name, string risk, string description, string fileName, string arguments,
            string displayCommand, bool recommended, bool deep, int timeout)
        {
            return new RepairAction
            {
                Id = id,
                Name = name,
                Risk = risk,
                Description = description,
                FileName = fileName,
                Arguments = arguments,
                DisplayCommand = displayCommand,
                Recommended = recommended,
                Deep = deep,
                Timeout = timeout
            };
        }

        private void SetBusy(bool busy)
        {
            runButton.Enabled = !busy;
            cancelButton.Enabled = busy;
            recommendedButton.Enabled = !busy;
            deepButton.Enabled = !busy;
            grid.Enabled = !busy;
            if (busy && progress.Style != ProgressBarStyle.Continuous) progress.Style = ProgressBarStyle.Marquee;
            if (!busy)
            {
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = 0;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }
    }
}
