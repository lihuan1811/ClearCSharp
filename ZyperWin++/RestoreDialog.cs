using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class RestoreDialog : Form
    {
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly Label status = UiFactory.StatusLabel("正在读取可还原内容...");
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button restoreFilesButton = UiFactory.PrimaryButton("还原选中文件");
        private readonly Button restoreOptimizationsButton = UiFactory.SecondaryButton("还原系统优化");
        private readonly Button restoreMigrationsButton = UiFactory.SecondaryButton("还原目录迁移");
        private CancellationTokenSource cancellation;

        public RestoreDialog()
        {
            Text = "全局一键还原";
            Font = UiFactory.BaseFont;
            BackColor = AppPalette.Canvas;
            ClientSize = new Size(940, 560);
            MinimumSize = new Size(940, 560);
            MaximumSize = new Size(940, 560);
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(14);

            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = AppPalette.PaleGreen, Padding = new Padding(14, 10, 14, 8) };
            header.Controls.Add(new Label
            {
                Text = "可逆变更还原中心",
                Dock = DockStyle.Top,
                Height = 28,
                Font = UiFactory.TitleFont,
                ForeColor = AppPalette.Text
            });
            header.Controls.Add(new Label
            {
                Text = "文件清理、由本程序记录的 ZyperWin++ 系统优化和系统目录迁移分别还原。",
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = UiFactory.BaseFont,
                ForeColor = AppPalette.Muted
            });

            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 42 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Created", HeaderText = "备份时间", Width = 155 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "大小", Width = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Source",
                HeaderText = "清理文件原路径",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 430
            });
            grid.ReadOnly = false;
            foreach (DataGridViewColumn column in grid.Columns) column.ReadOnly = column.Name != "Selected";

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 78,
                RowCount = 2,
                ColumnCount = 2,
                Padding = new Padding(0, 8, 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bottom.Controls.Add(status, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8, 4, 0, 4);
            bottom.Controls.Add(progress, 1, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            restoreFilesButton.Width = 116;
            restoreOptimizationsButton.Width = 118;
            restoreMigrationsButton.Width = 118;
            actions.Controls.Add(restoreFilesButton);
            actions.Controls.Add(restoreOptimizationsButton);
            actions.Controls.Add(restoreMigrationsButton);
            bottom.SetColumnSpan(actions, 2);
            bottom.Controls.Add(actions, 0, 1);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(header);
            restoreFilesButton.Click += async delegate { await RestoreFilesAsync(); };
            restoreOptimizationsButton.Click += async delegate { await RestoreOptimizationsAsync(); };
            restoreMigrationsButton.Click += async delegate { await RestoreMigrationsAsync(); };
            Load += delegate { RefreshRecords(); };
        }

        private void RefreshRecords()
        {
            grid.Rows.Clear();
            foreach (BackupRecord record in BackupStore.ReadRecords())
            {
                int index = grid.Rows.Add(false, record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), DisplayFormat.Bytes(record.Bytes), record.SourcePath);
                grid.Rows[index].Tag = record;
            }
            int optimizations = Optimize.JournaledOptimizationCount();
            int migrations = MigrationService.MigratedRecordCount();
            status.Text = string.Format("文件备份 {0} 条；系统优化记录 {1} 项；目录迁移 {2} 项。", grid.Rows.Count, optimizations, migrations);
            restoreOptimizationsButton.Enabled = optimizations > 0;
            restoreMigrationsButton.Enabled = migrations > 0;
        }

        private async Task RestoreFilesAsync()
        {
            List<BackupRecord> records = SelectedRecords();
            if (records.Count == 0)
            {
                MessageBox.Show("请先勾选要还原的文件备份。", "全局一键还原", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("将把 " + records.Count + " 个文件备份写回原路径，同名现有文件会被覆盖。\n\n是否继续？",
                "确认还原文件", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            cancellation = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                int restored = 0;
                var errors = new List<string>();
                await Task.Run(() =>
                {
                    foreach (BackupRecord record in records)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        string error;
                        if (BackupStore.TryRestore(record, out error)) restored++;
                        else errors.Add(record.SourcePath + "：" + error);
                    }
                }, cancellation.Token);
                status.Text = string.Format("文件还原完成：成功 {0}，失败 {1}。", restored, errors.Count);
                if (errors.Count > 0) MessageBox.Show(string.Join(Environment.NewLine, errors.Take(10)), "部分文件还原失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                status.Text = "文件还原已取消。";
            }
            finally
            {
                FinishBusy();
            }
        }

        private async Task RestoreOptimizationsAsync()
        {
            int count = Optimize.JournaledOptimizationCount();
            if (count == 0) return;
            if (MessageBox.Show("将还原本程序变更日志中记录的 " + count + " 个 ZyperWin++ 系统优化项。不会还原未由本程序记录的用户设置。\n\n是否继续？",
                "确认还原系统优化", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SetBusy(true);
            try
            {
                using (var optimize = new Optimize())
                {
                    optimize.CreateControl();
                    int restored = await optimize.RestoreJournaledOptimizationsAsync();
                    status.Text = "系统优化还原完成：" + restored + " 项。";
                }
            }
            catch (Exception ex)
            {
                status.Text = "系统优化还原失败：" + DisplayFormat.SingleLine(ex.Message, 150);
                OperationLogger.Error("全局还原", ex.Message);
                MessageBox.Show(ex.Message, "系统优化还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                RefreshRecords();
            }
        }

        private async Task RestoreMigrationsAsync()
        {
            int count = MigrationService.MigratedRecordCount();
            if (count == 0) return;
            if (MessageBox.Show("将把 " + count + " 个已迁移系统目录移回原路径。请关闭相关软件并确认 C 盘空间充足。\n\n是否继续？",
                "确认还原目录迁移", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                var service = new MigrationService();
                FileOperationSummary result = await service.RestoreAllAsync(cancellation.Token);
                status.Text = "目录迁移还原：" + result.Message.Split('\n')[0].Trim();
                if (!result.Success) MessageBox.Show(result.Message, "部分目录还原失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                status.Text = "目录迁移还原已取消。";
            }
            finally
            {
                FinishBusy();
            }
        }

        private List<BackupRecord> SelectedRecords()
        {
            var records = new List<BackupRecord>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (Convert.ToBoolean(row.Cells["Selected"].Value ?? false) && row.Tag is BackupRecord)
                    records.Add((BackupRecord)row.Tag);
            }
            return records;
        }

        private void SetBusy(bool busy)
        {
            ControlBox = !busy;
            grid.Enabled = !busy;
            restoreFilesButton.Enabled = !busy;
            restoreOptimizationsButton.Enabled = !busy && Optimize.JournaledOptimizationCount() > 0;
            restoreMigrationsButton.Enabled = !busy && MigrationService.MigratedRecordCount() > 0;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) progress.Value = 0;
        }

        private void FinishBusy()
        {
            if (cancellation != null)
            {
                cancellation.Dispose();
                cancellation = null;
            }
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshRecords();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }
    }
}
