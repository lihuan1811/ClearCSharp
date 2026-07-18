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
    public sealed class MigrationDashboard : UserControl
    {
        private readonly MigrationService service = new MigrationService();
        private readonly TextBox targetBox = new TextBox();
        private readonly Button browseButton = UiFactory.SecondaryButton("选择目标");
        private readonly Button refreshButton = UiFactory.SecondaryButton("刷新状态");
        private readonly Button migrateButton = UiFactory.PrimaryButton("迁移选中");
        private readonly Button restoreButton = UiFactory.SecondaryButton("还原选中");
        private readonly Button restoreAllButton = UiFactory.SecondaryButton("还原所有迁移目录");
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly Label status = UiFactory.StatusLabel("正在读取系统目录迁移状态...");
        private readonly ProgressBar progress = new ProgressBar();
        private CancellationTokenSource cancellation;

        public MigrationDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "系统目录一键迁移专区",
                "迁移前请关闭微信、QQ 和其它占用文件的软件；目标必须是其它 NTFS 固定磁盘。",
                out headerActions);
            refreshButton.Width = 94;
            headerActions.Controls.Add(refreshButton);

            var targetBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                ColumnCount = 3,
                Padding = new Padding(0, 8, 0, 6)
            };
            targetBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            targetBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            targetBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetBar.Controls.Add(new Label
            {
                Text = "迁移目标",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiFactory.SectionFont,
                ForeColor = AppPalette.Text
            }, 0, 0);
            targetBox.Dock = DockStyle.Fill;
            targetBox.Font = UiFactory.BaseFont;
            targetBox.Margin = new Padding(0, 4, 8, 4);
            targetBox.Text = SuggestedTargetRoot();
            targetBar.Controls.Add(targetBox, 1, 0);
            browseButton.Width = 96;
            targetBar.Controls.Add(browseButton, 2, 0);

            ConfigureGrid();

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 60, ColumnCount = 3, Padding = new Padding(0, 8, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(status, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8);
            bottom.Controls.Add(progress, 1, 0);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            restoreAllButton.Width = 148;
            restoreButton.Width = 98;
            migrateButton.Width = 98;
            actions.Controls.Add(restoreAllButton);
            actions.Controls.Add(restoreButton);
            actions.Controls.Add(migrateButton);
            bottom.Controls.Add(actions, 2, 0);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(targetBar);
            Controls.Add(header);

            browseButton.Click += BrowseButton_Click;
            refreshButton.Click += async delegate { await RefreshAsync(); };
            migrateButton.Click += async delegate { await MigrateSelectedAsync(); };
            restoreButton.Click += async delegate { await RestoreSelectedAsync(); };
            restoreAllButton.Click += async delegate { await RestoreAllAsync(); };
            Load += async delegate { await RefreshAsync(); };
        }

        private void ConfigureGrid()
        {
            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 42, ReadOnly = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "目录", Width = 210 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "大小", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "状态", Width = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "原路径", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "迁移目标", Width = 320 });
            foreach (DataGridViewColumn column in grid.Columns) if (column.Name != "Selected") column.ReadOnly = true;
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择其它 NTFS 磁盘上的空目标目录",
                SelectedPath = Directory.Exists(targetBox.Text) ? targetBox.Text : string.Empty,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) targetBox.Text = dialog.SelectedPath;
            }
        }

        private async Task RefreshAsync()
        {
            if (cancellation != null) return;
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            status.Text = "正在读取系统目录迁移状态...";
            try
            {
                IList<MigrationFolder> folders = await service.ScanAsync(cancellation.Token);
                grid.Rows.Clear();
                foreach (MigrationFolder folder in folders)
                {
                    int index = grid.Rows.Add(false, folder.Name, DisplayFormat.Bytes(folder.Size),
                        folder.Migrated ? "已迁移" : (folder.Partial ? "部分迁移" : (folder.Exists ? "未迁移" : "不存在")),
                        folder.SourcePath, folder.TargetPath);
                    grid.Rows[index].Tag = folder;
                    if (folder.Migrated || folder.Partial) grid.Rows[index].DefaultCellStyle.BackColor = AppPalette.PaleGreen;
                }
                status.Text = "迁移状态已刷新；仅显示最终需求中的 7 个系统目录。";
            }
            catch (OperationCanceledException)
            {
                status.Text = "状态读取已取消。";
            }
            catch (Exception ex)
            {
                status.Text = "读取失败：" + DisplayFormat.SingleLine(ex.Message, 160);
                OperationLogger.Error("系统目录迁移", ex.Message);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                if (!IsDisposed) SetBusy(false);
            }
        }

        private IList<MigrationFolder> SelectedFolders(bool migrated)
        {
            var selected = new List<MigrationFolder>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                var folder = row.Tag as MigrationFolder;
                if (folder == null) continue;
                bool canSelect = migrated ? (folder.Migrated || folder.Partial) : (!folder.Migrated && !folder.Partial);
                if (!canSelect) continue;
                if (Convert.ToBoolean(row.Cells["Selected"].Value ?? false)) selected.Add(folder);
            }
            return selected;
        }

        private async Task MigrateSelectedAsync()
        {
            IList<MigrationFolder> folders = SelectedFolders(false);
            if (folders.Count == 0)
            {
                MessageBox.Show("请勾选一个或多个未迁移目录。", "系统目录迁移", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(targetBox.Text))
            {
                MessageBox.Show("请先选择迁移目标目录。", "系统目录迁移", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string names = string.Join("、", folders.Select(folder => folder.Name));
            if (MessageBox.Show("请先关闭所有占用这些目录的软件。\n\n将迁移：" + names + "\n目标：" + targetBox.Text.Trim() +
                "\n\n程序会移动原文件、建立目录连接，并更新 User Shell Folders / Shell Folders 或 TEMP / TMP。是否继续？",
                "确认系统目录迁移", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunBatchAsync("系统目录迁移", folders, folder => service.MigrateAsync(folder.Key, targetBox.Text.Trim(), cancellation.Token));
        }

        private async Task RestoreSelectedAsync()
        {
            IList<MigrationFolder> folders = SelectedFolders(true);
            if (folders.Count == 0)
            {
                MessageBox.Show("请勾选一个或多个已迁移目录。", "还原迁移目录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("将把选中目录移回 C 盘原路径并恢复系统配置。请确认 C 盘空间充足。\n\n是否继续？",
                "确认还原迁移目录", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunBatchAsync("还原迁移目录", folders, folder => service.RestoreAsync(folder.Key, cancellation.Token));
        }

        private async Task RestoreAllAsync()
        {
            if (MessageBox.Show("将还原本程序记录的全部系统目录迁移。请关闭相关软件并确认 C 盘空间充足。\n\n是否继续？",
                "还原所有迁移目录", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            status.Text = "正在还原所有迁移目录...";
            try
            {
                FileOperationSummary result = await service.RestoreAllAsync(cancellation.Token);
                ShowResult("还原所有迁移目录", result);
            }
            catch (OperationCanceledException)
            {
                status.Text = "还原已取消。";
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                if (!IsDisposed)
                {
                    SetBusy(false);
                    await RefreshAsync();
                }
            }
        }

        private async Task RunBatchAsync(string title, IList<MigrationFolder> folders, Func<MigrationFolder, Task<FileOperationSummary>> action)
        {
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            progress.Style = ProgressBarStyle.Continuous;
            progress.Minimum = 0;
            progress.Maximum = folders.Count;
            progress.Value = 0;
            var combined = new FileOperationSummary();
            try
            {
                for (int index = 0; index < folders.Count; index++)
                {
                    status.Text = string.Format("{0} {1}/{2}：{3}", title, index + 1, folders.Count, folders[index].Name);
                    FileOperationSummary result = await action(folders[index]);
                    foreach (string path in result.AffectedPaths) combined.AffectedPaths.Add(path);
                    foreach (string error in result.Errors) combined.Errors.Add(folders[index].Name + "：" + error);
                    progress.Value = index + 1;
                }
                ShowResult(title, combined);
            }
            catch (OperationCanceledException)
            {
                status.Text = title + "已取消，已完成项保持可还原。";
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                if (!IsDisposed)
                {
                    SetBusy(false);
                    await RefreshAsync();
                }
            }
        }

        private void ShowResult(string title, FileOperationSummary result)
        {
            status.Text = title + "：" + result.Message.Split('\n')[0].Trim();
            if (result.Success) OperationLogger.Info(title, result.Message); else OperationLogger.Error(title, result.Message);
            MessageBox.Show(result.Message, title, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void SetBusy(bool busy)
        {
            browseButton.Enabled = !busy;
            refreshButton.Enabled = !busy;
            migrateButton.Enabled = !busy;
            restoreButton.Enabled = !busy;
            restoreAllButton.Enabled = !busy;
            targetBox.Enabled = !busy;
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

        private static string SuggestedTargetRoot()
        {
            try
            {
                string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                DriveInfo target = DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
                    .Where(drive => string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(drive => !string.Equals(drive.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase));
                if (target != null) return Path.Combine(target.RootDirectory.FullName, "C_DiskGlow_Migrated");
            }
            catch
            {
            }
            return string.Empty;
        }
    }
}
