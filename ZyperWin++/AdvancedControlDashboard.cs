using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class AdvancedControlDashboard : UserControl
    {
        private readonly SystemControlService service = new SystemControlService();
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly Button refreshButton = UiFactory.SecondaryButton("重新检测");
        private readonly Button restoreButton = UiFactory.SecondaryButton("还原选中");
        private readonly Button applyButton = UiFactory.PrimaryButton("执行选中");
        private readonly Label status = UiFactory.StatusLabel("正在检测当前系统支持的高级操作...");
        private readonly ProgressBar progress = new ProgressBar();
        private CancellationTokenSource cancellation;

        public AdvancedControlDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(12);

            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "选择", Width = 58, ReadOnly = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "管控分类", Width = 130, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "真实操作", Width = 230, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "当前状态", Width = 170, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Risk", HeaderText = "风险", Width = 80, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Description",
                HeaderText = "官方入口、验证与还原方式",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 320,
                ReadOnly = true
            });
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate { UpdateButtons(); };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(0, 8, 0, 0) };
            status.Dock = DockStyle.Fill;
            progress.Dock = DockStyle.Bottom;
            progress.Height = 20;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 330, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            refreshButton.Width = 96;
            restoreButton.Width = 96;
            applyButton.Width = 96;
            actions.Controls.Add(refreshButton);
            actions.Controls.Add(restoreButton);
            actions.Controls.Add(applyButton);
            bottom.Controls.Add(status);
            bottom.Controls.Add(actions);
            bottom.Controls.Add(progress);

            Controls.Add(grid);
            Controls.Add(bottom);
            refreshButton.Click += async delegate { await RefreshAsync(); };
            applyButton.Click += async delegate { await RunAsync(false); };
            restoreButton.Click += async delegate { await RunAsync(true); };
            Load += async delegate { await RefreshAsync(); };
        }

        private async Task RefreshAsync()
        {
            if (cancellation != null) cancellation.Cancel();
            var source = new CancellationTokenSource();
            cancellation = source;
            SetBusy(true);
            status.Text = "正在读取 Windows Update、Defender、Edge 和系统代理状态...";
            try
            {
                IList<SystemControlOperation> operations = await service.DetectAsync(source.Token);
                if (source.IsCancellationRequested || IsDisposed) return;
                grid.Rows.Clear();
                foreach (SystemControlOperation operation in operations)
                {
                    int rowIndex = grid.Rows.Add(false, operation.Group, operation.Name, operation.CurrentState,
                        operation.Risk, operation.Description);
                    DataGridViewRow row = grid.Rows[rowIndex];
                    row.Tag = operation;
                    if (!operation.CanApply && !operation.CanRestore)
                    {
                        row.Cells["Selected"].ReadOnly = true;
                        row.DefaultCellStyle.ForeColor = AppPalette.Muted;
                    }
                    if (operation.Risk == "高风险") row.DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 235);
                }
                status.Text = "检测完成：只列出当前系统可识别的官方操作；执行前会再次确认。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                status.Text = "检测失败：" + DisplayFormat.SingleLine(ex.Message, 180);
                OperationLogger.Error("高级系统管控", ex.Message);
            }
            finally
            {
                bool current = ReferenceEquals(cancellation, source);
                if (current) cancellation = null;
                source.Dispose();
                if (current && !IsDisposed) SetBusy(false);
            }
        }

        private async Task RunAsync(bool restore)
        {
            IList<SystemControlOperation> selected = Selected(restore);
            if (selected.Count == 0)
            {
                MessageBox.Show("请先勾选可" + (restore ? "还原" : "执行") + "的操作。", "高级系统管控",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string names = string.Join("、", selected.Select(value => value.Name));
            string warning = (restore ? "将按真实快照还原：" : "将执行：") + names;
            if (selected.Any(value => value.Risk == "高风险"))
                warning += "\n\n包含高风险操作，可能降低实时安全保护。";
            if (MessageBox.Show(warning + "\n\n是否继续？", restore ? "确认还原" : "确认执行",
                MessageBoxButtons.YesNo, selected.Any(value => value.Risk == "高风险") ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;

            AppOperationScope operationScope = null;
            var source = new CancellationTokenSource();
            cancellation = source;
            SetBusy(true);
            try
            {
                operationScope = AppOperationCoordinator.Begin(restore ? "高级系统管控还原" : "高级系统管控");
                FileOperationSummary result = restore
                    ? await service.RestoreAsync(selected, source.Token)
                    : await service.ApplyAsync(selected, source.Token);
                MessageBox.Show(result.Message, restore ? "还原结果" : "执行结果", MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException) { status.Text = "操作已取消。"; }
            catch (Exception ex)
            {
                OperationLogger.Error("高级系统管控", ex.Message);
                MessageBox.Show(ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation = null;
                source.Dispose();
                if (operationScope != null) operationScope.Dispose();
                if (!IsDisposed)
                {
                    SetBusy(false);
                    await RefreshAsync();
                }
            }
        }

        private IList<SystemControlOperation> Selected(bool restore)
        {
            var result = new List<SystemControlOperation>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                var operation = row.Tag as SystemControlOperation;
                if (operation != null && Convert.ToBoolean(row.Cells["Selected"].Value ?? false) &&
                    (restore ? operation.CanRestore : operation.CanApply)) result.Add(operation);
            }
            return result;
        }

        private void UpdateButtons()
        {
            bool idle = cancellation == null;
            applyButton.Enabled = idle && Selected(false).Count > 0;
            restoreButton.Enabled = idle && Selected(true).Count > 0;
        }

        private void SetBusy(bool busy)
        {
            grid.Enabled = !busy;
            refreshButton.Enabled = !busy;
            if (busy)
            {
                applyButton.Enabled = false;
                restoreButton.Enabled = false;
            }
            else UpdateButtons();
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) progress.Value = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }
    }
}
