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
    public sealed class FileOperationsDashboard : UserControl
    {
        private readonly ManagedFileService service = new ManagedFileService();
        private readonly TextBox pathBox = new TextBox();
        private readonly ComboBox typeBox = new ComboBox();
        private readonly Button browseButton = UiFactory.SecondaryButton("选择目录");
        private readonly Button scanButton = UiFactory.PrimaryButton("扫描文件");
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly BusyAnimationOverlay busyOverlay = new BusyAnimationOverlay();
        private readonly Label status = UiFactory.StatusLabel("选择磁盘或目录，按类型扫描后勾选文件操作。");
        private readonly ProgressBar progress = new ProgressBar();
        private readonly List<Button> operationButtons = new List<Button>();
        private CancellationTokenSource cancellation;

        public FileOperationsDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "文件筛选与批量操作",
                "按视频、图片、安装包、压缩包和文档筛选，所有写操作执行前都会确认并记录日志。",
                out headerActions);

            var filterBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                ColumnCount = 6,
                Padding = new Padding(0, 8, 0, 6)
            };
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterBar.Controls.Add(LabelCell("扫描目录"), 0, 0);
            pathBox.Dock = DockStyle.Fill;
            pathBox.Font = UiFactory.BaseFont;
            pathBox.Text = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
            pathBox.Margin = new Padding(0, 4, 8, 4);
            filterBar.Controls.Add(pathBox, 1, 0);
            filterBar.Controls.Add(LabelCell("文件类型"), 2, 0);
            typeBox.Dock = DockStyle.Fill;
            typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            typeBox.Font = UiFactory.BaseFont;
            typeBox.Margin = new Padding(0, 4, 8, 4);
            foreach (ManagedFileType type in Enum.GetValues(typeof(ManagedFileType))) typeBox.Items.Add(new TypeItem(type));
            typeBox.SelectedIndex = 0;
            filterBar.Controls.Add(typeBox, 3, 0);
            browseButton.Width = 96;
            scanButton.Width = 96;
            filterBar.Controls.Add(browseButton, 4, 0);
            filterBar.Controls.Add(scanButton, 5, 0);

            ConfigureGrid();
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = AppPalette.Canvas };
            gridHost.Controls.Add(grid);
            busyOverlay.AttachTo(gridHost);

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 82,
                RowCount = 2,
                ColumnCount = 2,
                Padding = new Padding(0, 7, 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bottom.Controls.Add(status, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8, 4, 0, 5);
            bottom.Controls.Add(progress, 1, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            AddAction(actions, "跨盘复制", async delegate { await CopyOrMoveAsync(false); });
            AddAction(actions, "移动", async delegate { await CopyOrMoveAsync(true); });
            AddAction(actions, "批量重命名", RenameAsync);
            AddAction(actions, "批量删除", DeleteAsync);
            AddAction(actions, "文件粉碎", ShredAsync);
            AddAction(actions, "文件夹权限修复", RepairPermissionAsync);
            AddAction(actions, "迁移并生成快捷方式", MigrateAsync, true);
            bottom.SetColumnSpan(actions, 2);
            bottom.Controls.Add(actions, 0, 1);

            Controls.Add(gridHost);
            Controls.Add(bottom);
            Controls.Add(filterBar);
            Controls.Add(header);

            browseButton.Click += BrowseButton_Click;
            scanButton.Click += async delegate { await ScanAsync(); };
        }

        private void ConfigureGrid()
        {
            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 42, ReadOnly = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "类型", Width = 82 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "大小", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Modified", HeaderText = "上次更改", Width = 145 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "路径", Width = 360 });
            foreach (DataGridViewColumn column in grid.Columns) if (column.Name != "Selected") column.ReadOnly = true;
        }

        private static Label LabelCell(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiFactory.SectionFont,
                ForeColor = AppPalette.Text
            };
        }

        private void AddAction(Control parent, string text, Func<Task> handler, bool primary = false)
        {
            Button button = primary ? UiFactory.PrimaryButton(text) : UiFactory.SecondaryButton(text);
            button.AutoSize = true;
            button.MinimumSize = new Size(Math.Max(82, TextRenderer.MeasureText(text, button.Font).Width + 24), 32);
            button.Click += async delegate { await handler(); };
            operationButtons.Add(button);
            parent.Controls.Add(button);
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择要筛选的磁盘或目录",
                SelectedPath = Directory.Exists(pathBox.Text) ? pathBox.Text : string.Empty,
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.SelectedPath;
            }
        }

        private async Task ScanAsync()
        {
            if (cancellation != null)
            {
                cancellation.Cancel();
                return;
            }
            if (!Directory.Exists(pathBox.Text.Trim()))
            {
                MessageBox.Show("请选择存在的目录。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            cancellation = new CancellationTokenSource();
            SetBusy(true, true);
            status.Text = "正在扫描文件...";
            busyOverlay.Start("正在筛选文件", DisplayFormat.SingleLine(pathBox.Text.Trim(), 90));
            try
            {
                ManagedFileType type = ((TypeItem)typeBox.SelectedItem).Type;
                IList<ManagedFileEntry> files = await service.ScanAsync(
                    pathBox.Text.Trim(),
                    type,
                    3000,
                    new Progress<string>(message =>
                    {
                        status.Text = DisplayFormat.SingleLine(message, 180);
                        busyOverlay.UpdateMessage(DisplayFormat.SingleLine(message, 90));
                    }),
                    cancellation.Token);
                grid.Rows.Clear();
                foreach (ManagedFileEntry file in files)
                {
                    int index = grid.Rows.Add(false, file.Name, ManagedFileService.TypeLabel(file.Type), DisplayFormat.Bytes(file.Size),
                        file.ModifiedAt.ToString("yyyy/M/d HH:mm"), file.Path);
                    grid.Rows[index].Tag = file;
                }
                status.Text = string.Format("扫描完成：显示体积最大的 {0:N0} 个匹配文件。", files.Count);
                OperationLogger.Info("文件筛选", pathBox.Text + "，" + ManagedFileService.TypeLabel(type) + "，" + files.Count + " 项");
            }
            catch (OperationCanceledException)
            {
                status.Text = "文件扫描已取消。";
            }
            catch (Exception ex)
            {
                status.Text = "文件扫描失败：" + DisplayFormat.SingleLine(ex.Message, 160);
                OperationLogger.Error("文件筛选", ex.Message);
                MessageBox.Show(ex.Message, "文件扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                busyOverlay.Stop();
                if (!IsDisposed) SetBusy(false, false);
            }
        }

        private List<string> SelectedPaths()
        {
            var paths = new List<string>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (Convert.ToBoolean(row.Cells["Selected"].Value ?? false) && row.Tag is ManagedFileEntry)
                    paths.Add(((ManagedFileEntry)row.Tag).Path);
            }
            if (paths.Count == 0 && grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is ManagedFileEntry)
                paths.Add(((ManagedFileEntry)grid.SelectedRows[0].Tag).Path);
            return paths;
        }

        private async Task CopyOrMoveAsync(bool move)
        {
            List<string> paths = RequireSelection();
            if (paths == null) return;
            string target = SelectTarget(move ? "选择移动目标目录" : "选择复制目标目录");
            if (target == null) return;
            string title = move ? "移动" : "跨盘复制";
            await RunOperationAsync(title, string.Format("将{0} {1} 个文件到：\n{2}\n\n是否继续？", move ? "移动" : "复制", paths.Count, target),
                token => move ? service.MoveAsync(paths, target, token) : service.CopyAsync(paths, target, token));
        }

        private async Task RenameAsync()
        {
            List<string> paths = RequireSelection();
            if (paths == null) return;
            string prefix = TextPrompt.Show(this, "批量重命名", "统一文件名前缀（自动添加 _001 编号并保留扩展名）：", "文件");
            if (prefix == null) return;
            await RunOperationAsync("批量重命名", "将重命名 " + paths.Count + " 个文件，是否继续？", token => service.RenameAsync(paths, prefix, token));
        }

        private async Task DeleteAsync()
        {
            List<string> paths = RequireSelection();
            if (paths == null) return;
            await RunOperationAsync("批量删除", "将删除 " + paths.Count + " 个文件。删除前会写入全局备份，可在底部“一键还原”恢复。\n\n是否继续？",
                token => service.DeleteAsync(paths, token));
        }

        private async Task ShredAsync()
        {
            List<string> paths = RequireSelection();
            if (paths == null) return;
            await RunOperationAsync("文件粉碎", "将对 " + paths.Count + " 个文件覆写两遍后永久删除。该操作无法还原。\n\n是否继续？",
                token => service.ShredAsync(paths, token), MessageBoxIcon.Stop);
        }

        private async Task RepairPermissionAsync()
        {
            string path = pathBox.Text.Trim();
            if (!Directory.Exists(path)) return;
            await RunOperationAsync("文件夹权限修复", "将使用 icacls 重置该目录及子目录权限：\n" + path + "\n\n是否继续？",
                token => service.RepairFolderPermissionAsync(path, token));
        }

        private async Task MigrateAsync()
        {
            List<string> paths = RequireSelection();
            if (paths == null) return;
            string target = SelectTarget("选择普通文件迁移目标目录");
            if (target == null) return;
            await RunOperationAsync("普通文件迁移", "将移动 " + paths.Count + " 个文件，并在原位置生成 .lnk 快捷方式。失败时会尝试回滚。\n\n是否继续？",
                token => service.MigrateWithShortcutsAsync(paths, target, token));
        }

        private List<string> RequireSelection()
        {
            List<string> paths = SelectedPaths();
            if (paths.Count > 0) return paths;
            MessageBox.Show("请先勾选一个或多个文件。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        private string SelectTarget(string title)
        {
            using (var dialog = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true })
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
        }

        private async Task RunOperationAsync(string title, string warning, Func<CancellationToken, Task<FileOperationSummary>> operation, MessageBoxIcon icon = MessageBoxIcon.Warning)
        {
            if (MessageBox.Show(warning, title, MessageBoxButtons.YesNo, icon) != DialogResult.Yes) return;
            cancellation = new CancellationTokenSource();
            SetBusy(true, false);
            status.Text = "正在执行：" + title;
            bool refreshAfterOperation = false;
            try
            {
                FileOperationSummary result = await operation(cancellation.Token);
                status.Text = title + "：" + result.Message.Split('\n')[0].Trim();
                if (result.Success) OperationLogger.Info(title, result.Message);
                else OperationLogger.Error(title, result.Message);
                MessageBox.Show(result.Message, title, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                refreshAfterOperation = true;
            }
            catch (OperationCanceledException)
            {
                status.Text = title + "已取消。";
            }
            catch (Exception ex)
            {
                status.Text = title + "失败：" + DisplayFormat.SingleLine(ex.Message, 150);
                OperationLogger.Error(title, ex.Message);
                MessageBox.Show(ex.Message, title + "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (cancellation != null)
                {
                    cancellation.Dispose();
                    cancellation = null;
                }
                if (!IsDisposed) SetBusy(false, false);
            }
            if (refreshAfterOperation && !IsDisposed) await ScanAsync();
        }

        private void SetBusy(bool busy, bool scanning)
        {
            browseButton.Enabled = !busy;
            pathBox.Enabled = !busy;
            typeBox.Enabled = !busy;
            grid.Enabled = !busy;
            foreach (Button button in operationButtons) button.Enabled = !busy;
            scanButton.Enabled = !busy || scanning;
            scanButton.Text = busy && scanning ? "取消扫描" : "扫描文件";
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) progress.Value = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }

        private sealed class TypeItem
        {
            public ManagedFileType Type { get; private set; }
            public TypeItem(ManagedFileType type) { Type = type; }
            public override string ToString() { return ManagedFileService.TypeLabel(Type); }
        }
    }

    internal static class TextPrompt
    {
        public static string Show(IWin32Window owner, string title, string message, string initialValue)
        {
            using (var form = new Form
            {
                Text = title,
                ClientSize = new Size(520, 160),
                MinimumSize = new Size(520, 160),
                MaximumSize = new Size(520, 160),
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                Font = UiFactory.BaseFont,
                BackColor = AppPalette.Canvas,
                Padding = new Padding(16)
            })
            {
                var label = new Label { Text = message, Dock = DockStyle.Top, Height = 36, ForeColor = AppPalette.Text };
                var input = new TextBox { Text = initialValue, Dock = DockStyle.Top, Font = UiFactory.BaseFont };
                var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
                Button ok = UiFactory.PrimaryButton("确定");
                Button cancel = UiFactory.SecondaryButton("取消");
                ok.DialogResult = DialogResult.OK;
                cancel.DialogResult = DialogResult.Cancel;
                actions.Controls.Add(ok);
                actions.Controls.Add(cancel);
                form.Controls.Add(input);
                form.Controls.Add(label);
                form.Controls.Add(actions);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                if (form.ShowDialog(owner) != DialogResult.OK) return null;
                return input.Text.Trim();
            }
        }
    }
}
