using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class CleanupDashboard : UserControl
    {
        private readonly CleanupService service = new CleanupService();
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly DataGridView fileGrid = UiFactory.Grid();
        private readonly SplitContainer resultsSplit = new SplitContainer();
        private readonly BusyAnimationOverlay busyOverlay = new BusyAnimationOverlay();
        private readonly Label status = UiFactory.StatusLabel("请选择清理模块，然后开始扫描。");
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button scanButton = UiFactory.PrimaryButton("扫描");
        private readonly Button cleanButton = UiFactory.SecondaryButton("清理选中");
        private readonly Button selectRecommendedButton = UiFactory.SecondaryButton("默认");
        private readonly Button selectAllButton = UiFactory.SecondaryButton("全选");
        private readonly Button selectNoneButton = UiFactory.SecondaryButton("全不选");
        private readonly Button refreshButton = UiFactory.SecondaryButton("刷新");
        private readonly Button addWhitelistButton = UiFactory.SecondaryButton("添加白名单");
        private readonly Button manageWhitelistButton = UiFactory.SecondaryButton("管理白名单");
        private CancellationTokenSource cancellation;
        private CleanupScanResult visibleFiles;
        private bool changingSelection;

        public CleanupDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "C盘深度清理",
                "按过期文件、系统相关、缓存、应用和临时文件细分；高风险目录只分析。",
                out headerActions);
            selectNoneButton.Width = 82;
            selectAllButton.Width = 82;
            selectRecommendedButton.Width = 82;
            refreshButton.Width = 82;
            addWhitelistButton.Width = 100;
            manageWhitelistButton.Width = 100;
            headerActions.Controls.Add(manageWhitelistButton);
            headerActions.Controls.Add(addWhitelistButton);
            headerActions.Controls.Add(refreshButton);
            headerActions.Controls.Add(selectNoneButton);
            headerActions.Controls.Add(selectAllButton);
            headerActions.Controls.Add(selectRecommendedButton);

            ConfigureGrid();
            ConfigureFileGrid();
            resultsSplit.Dock = DockStyle.Fill;
            resultsSplit.Orientation = Orientation.Horizontal;
            resultsSplit.SplitterDistance = 250;
            resultsSplit.Panel1.Controls.Add(grid);
            resultsSplit.Panel2.Controls.Add(fileGrid);
            var resultsHost = new Panel { Dock = DockStyle.Fill, BackColor = AppPalette.Canvas };
            resultsHost.Controls.Add(resultsSplit);
            busyOverlay.AttachTo(resultsHost);

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 76,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = AppPalette.Canvas
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            progress.Dock = DockStyle.Fill;
            progress.Style = ProgressBarStyle.Blocks;
            progress.Margin = new Padding(0, 4, 12, 4);
            bottom.Controls.Add(status, 0, 0);
            bottom.SetColumnSpan(status, 2);
            bottom.Controls.Add(progress, 0, 1);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Dock = DockStyle.Fill
            };
            scanButton.Width = 90;
            cleanButton.Width = 104;
            actions.Controls.Add(scanButton);
            actions.Controls.Add(cleanButton);
            bottom.Controls.Add(actions, 1, 1);

            Controls.Add(resultsHost);
            Controls.Add(bottom);
            Controls.Add(header);

            scanButton.Click += async delegate { await ScanAsync(); };
            cleanButton.Click += async delegate { await CleanAsync(); };
            selectRecommendedButton.Click += delegate { SelectRecommended(); };
            selectAllButton.Click += delegate { SelectAll(); };
            selectNoneButton.Click += delegate { SelectNone(); };
            refreshButton.Click += delegate { PopulateRules(); };
            addWhitelistButton.Click += AddWhitelistButton_Click;
            manageWhitelistButton.Click += delegate { using (var dialog = new CleanupWhitelistDialog()) dialog.ShowDialog(this); };
            grid.SelectionChanged += delegate { ShowSelectedRuleFiles(); };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += Grid_CellValueChanged;
            PopulateRules();
        }

        private void ConfigureGrid()
        {
            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Selected",
                HeaderText = "",
                Width = 42,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Category",
                HeaderText = "分类",
                Width = 92,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "清理项",
                Width = 190,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Description",
                HeaderText = "说明",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 220,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Risk",
                HeaderText = "风险",
                Width = 76,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Size",
                HeaderText = "可释放",
                Width = 100,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Files",
                HeaderText = "文件数",
                Width = 85,
                ReadOnly = true
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Path",
                HeaderText = "扫描位置",
                Width = 230,
                ReadOnly = true
            });
        }

        private void ConfigureFileGrid()
        {
            fileGrid.ReadOnly = false;
            fileGrid.VirtualMode = true;
            fileGrid.RowCount = 0;
            fileGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Selected",
                HeaderText = "清理",
                Width = 54,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            fileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "文件名", Width = 190, ReadOnly = true });
            fileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "大小", Width = 95, ReadOnly = true });
            fileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Modified", HeaderText = "修改时间", Width = 145, ReadOnly = true });
            fileGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Path",
                HeaderText = "完整路径",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 280,
                ReadOnly = true
            });
            fileGrid.CellValueNeeded += FileGrid_CellValueNeeded;
            fileGrid.CellValuePushed += FileGrid_CellValuePushed;
            fileGrid.CellToolTipTextNeeded += FileGrid_CellToolTipTextNeeded;
            fileGrid.CellBeginEdit += delegate(object sender, DataGridViewCellCancelEventArgs args)
            {
                if (visibleFiles != null && visibleFiles.Rule.ScanOnly) args.Cancel = true;
            };
            fileGrid.MouseDown += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Right) return;
                DataGridView.HitTestInfo hit = fileGrid.HitTest(args.X, args.Y);
                if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0) fileGrid.CurrentCell = fileGrid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
            };
            fileGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (fileGrid.IsCurrentCellDirty) fileGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开文件位置", null, delegate { OpenCurrentFileLocation(); });
            menu.Items.Add("复制完整路径", null, delegate { CopyCurrentFilePath(); });
            menu.Items.Add("查看底层操作", null, delegate { ShowCurrentFileOperation(); });
            menu.Items.Add("单独清理此文件", null, async delegate { await CleanCurrentFileAsync(); });
            menu.Items.Add("添加到白名单", null, delegate { AddCurrentFileToWhitelist(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("查看操作日志", null, delegate { OpenOperationLog(); });
            fileGrid.ContextMenuStrip = menu;
        }

        private void PopulateRules()
        {
            visibleFiles = null;
            fileGrid.RowCount = 0;
            grid.Rows.Clear();
            foreach (CleanupRule rule in CleanupCatalog.GetRules(CleanupKind.DriveC))
            {
                int index = grid.Rows.Add(
                    rule.Recommended && !rule.ScanOnly,
                    rule.Category,
                    rule.Name,
                    rule.Description,
                    rule.Risk,
                    "待扫描",
                    "--",
                    string.Join("；", rule.PathTemplates.Select(Environment.ExpandEnvironmentVariables)));
                grid.Rows[index].Tag = rule;
                if (rule.ScanOnly)
                {
                    grid.Rows[index].Cells["Selected"].ReadOnly = true;
                    grid.Rows[index].DefaultCellStyle.ForeColor = AppPalette.Muted;
                }
            }
            status.Text = "已加载 " + grid.Rows.Count + " 条清理规则，勾选后点击扫描。";
        }

        private async Task ScanAsync()
        {
            if (cancellation != null)
            {
                cancellation.Cancel();
                return;
            }

            cancellation = new CancellationTokenSource();
            SetBusy(true, "取消扫描");
            progress.Style = ProgressBarStyle.Marquee;
            busyOverlay.Start("正在扫描 C 盘", "正在读取清理规则和文件大小");
            try
            {
                var reporter = new Progress<string>(value =>
                {
                    status.Text = value;
                    busyOverlay.UpdateMessage(DisplayFormat.SingleLine(value, 90));
                });
                IList<CleanupScanResult> results = await service.ScanAsync(CleanupKind.DriveC, reporter, cancellation.Token);
                grid.Rows.Clear();
                foreach (CleanupScanResult result in results)
                {
                    int index = grid.Rows.Add(
                        result.Rule.Recommended && !result.Rule.ScanOnly,
                        result.Rule.Category,
                        result.Rule.Name,
                        result.Rule.Description,
                        result.Rule.Risk,
                        result.Rule.ScanOnly ? "仅分析 " + DisplayFormat.Bytes(result.Bytes) : DisplayFormat.Bytes(result.Bytes),
                        result.FileCount.ToString("N0"),
                        result.Roots.Count == 0 ? "未找到" : string.Join("；", result.Roots));
                    grid.Rows[index].Tag = result;
                    if (result.Rule.ScanOnly)
                    {
                        grid.Rows[index].Cells["Selected"].ReadOnly = true;
                        grid.Rows[index].DefaultCellStyle.ForeColor = AppPalette.Muted;
                    }
                }
                if (grid.Rows.Count > 0)
                {
                    grid.ClearSelection();
                    grid.Rows[0].Selected = true;
                    grid.CurrentCell = grid.Rows[0].Cells["Name"];
                    ShowSelectedRuleFiles();
                }
                long detected = results.Sum(value => value.Bytes);
                long cleanable = results.Where(value => !value.Rule.ScanOnly).Sum(value => value.Bytes);
                int files = results.Sum(value => value.FileCount);
                status.Text = string.Format("扫描完成：{0:N0} 个文件，检测 {1}，可清理 {2}。", files, DisplayFormat.Bytes(detected), DisplayFormat.Bytes(cleanable));
                OperationLogger.Info("清理扫描", "C盘深度清理，检测 " + DisplayFormat.Bytes(detected) + "，可清理 " + DisplayFormat.Bytes(cleanable));
            }
            catch (OperationCanceledException)
            {
                status.Text = "扫描已取消。";
            }
            catch (Exception ex)
            {
                status.Text = "扫描失败：" + ex.Message;
                OperationLogger.Error("清理扫描", ex.Message);
                MessageBox.Show(ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                busyOverlay.Stop();
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = 0;
                SetBusy(false, "扫描");
            }
        }

        private async Task CleanAsync()
        {
            var selected = new List<CleanupScanResult>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool isSelected = Convert.ToBoolean(row.Cells["Selected"].Value ?? false);
                var result = row.Tag as CleanupScanResult;
                if (isSelected && result != null && result.SelectedFiles.Count > 0)
                    selected.Add(CleanupService.CreateSelection(result));
            }
            if (selected.Count == 0)
            {
                MessageBox.Show("请先完成扫描并勾选需要清理的项目。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long bytes = selected.Sum(value => value.Bytes);
            int files = selected.Sum(value => value.FileCount);
            string backupRoot;
            bool createBackup = BackupStore.TryGetSpaceReleasingRoot(out backupRoot);
            string backupNotice = createBackup
                ? "删除前会备份到：" + backupRoot
                : "未检测到 C 盘以外的可用磁盘。为了真实释放 C 盘空间，本次不会创建备份，删除后无法从本程序还原。";
            DialogResult answer = MessageBox.Show(
                string.Format("将删除 {0:N0} 个已扫描文件，预计释放 {1}。\n\n{2}\n仅分析项不会删除。是否继续？", files, DisplayFormat.Bytes(bytes), backupNotice),
                "确认清理",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            cancellation = new CancellationTokenSource();
            SetBusy(true, "取消清理");
            progress.Style = ProgressBarStyle.Marquee;
            busyOverlay.Start("正在清理选中项", createBackup ? "正在备份并删除已确认文件" : "正在删除已确认文件");
            try
            {
                CleanupResult result = await service.CleanAsync(
                    selected,
                    createBackup ? backupRoot : null,
                    new Progress<string>(value =>
                    {
                        status.Text = value;
                        busyOverlay.UpdateMessage(DisplayFormat.SingleLine(value, 90));
                    }),
                    cancellation.Token);
                status.Text = string.Format(
                    "清理完成：删除 {0:N0} 个文件，释放 {1}，失败 {2:N0} 个。",
                    result.DeletedFiles,
                    DisplayFormat.Bytes(result.Bytes),
                    result.FailedFiles);
                MessageBox.Show(status.Text, "清理完成", MessageBoxButtons.OK,
                    result.FailedFiles == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    row.Cells["Selected"].Value = false;
                    row.Cells["Size"].Value = "需重新扫描";
                }
            }
            catch (OperationCanceledException)
            {
                status.Text = "清理已取消。";
            }
            catch (Exception ex)
            {
                OperationLogger.Error("清理", ex.Message);
                MessageBox.Show(ex.Message, "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (cancellation != null)
                {
                    cancellation.Dispose();
                    cancellation = null;
                }
                busyOverlay.Stop();
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = 0;
                SetBusy(false, "扫描");
            }
        }

        private void SelectRecommended()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                CleanupRule rule = row.Tag as CleanupRule;
                if (rule == null)
                {
                    CleanupScanResult result = row.Tag as CleanupScanResult;
                    rule = result == null ? null : result.Rule;
                }
                bool select = rule != null && rule.Recommended && !rule.ScanOnly;
                SetRuleSelection(row, select);
            }
        }

        private void SelectAll()
        {
            if (MessageBox.Show(
                "全选会包含谨慎操作项，例如聊天媒体、接收文件、Cookies 和大型安装包。高风险仅分析项仍不会删除。\n\n是否继续？",
                "确认全选",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (DataGridViewRow row in grid.Rows)
            {
                CleanupRule rule = row.Tag as CleanupRule;
                if (rule == null)
                {
                    CleanupScanResult result = row.Tag as CleanupScanResult;
                    rule = result?.Rule;
                }
                SetRuleSelection(row, rule != null && !rule.ScanOnly);
            }
        }

        private void SetBusy(bool busy, string scanText)
        {
            scanButton.Text = scanText;
            cleanButton.Enabled = !busy;
            selectRecommendedButton.Enabled = !busy;
            selectAllButton.Enabled = !busy;
            selectNoneButton.Enabled = !busy;
            refreshButton.Enabled = !busy;
            addWhitelistButton.Enabled = !busy;
            manageWhitelistButton.Enabled = !busy;
            grid.Enabled = !busy;
            fileGrid.Enabled = !busy;
        }

        private void AddWhitelistButton_Click(object sender, EventArgs e)
        {
            DialogResult kind = MessageBox.Show("添加文件到白名单请选择“是”，添加文件夹请选择“否”。",
                "添加清理白名单", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (kind == DialogResult.Cancel) return;
            string path = null;
            if (kind == DialogResult.Yes)
            {
                using (var dialog = new OpenFileDialog { Title = "选择不参与清理的文件", CheckFileExists = true, Multiselect = false })
                    if (dialog.ShowDialog(this) == DialogResult.OK) path = dialog.FileName;
            }
            else
            {
                using (var dialog = new FolderBrowserDialog { Description = "选择不参与清理的文件夹", ShowNewFolderButton = false })
                    if (dialog.ShowDialog(this) == DialogResult.OK) path = dialog.SelectedPath;
            }
            if (string.IsNullOrWhiteSpace(path)) return;
            string error;
            if (!CleanupWhitelist.Add(path, out error))
                MessageBox.Show(error, "添加白名单失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else status.Text = "已添加清理白名单：" + path;
        }

        private void SelectNone()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                SetRuleSelection(row, false);
            }
            fileGrid.Invalidate();
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (changingSelection || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Selected") return;
            DataGridViewRow row = grid.Rows[e.RowIndex];
            var result = row.Tag as CleanupScanResult;
            if (result == null || result.Rule.ScanOnly) return;
            bool selected = Convert.ToBoolean(row.Cells["Selected"].Value ?? false);
            if (selected) result.SelectedFiles.UnionWith(result.Files);
            else result.SelectedFiles.Clear();
            if (ReferenceEquals(result, visibleFiles)) fileGrid.Invalidate();
        }

        private void SetRuleSelection(DataGridViewRow row, bool selected)
        {
            var result = row.Tag as CleanupScanResult;
            changingSelection = true;
            row.Cells["Selected"].Value = selected;
            changingSelection = false;
            if (result == null) return;
            if (selected) result.SelectedFiles.UnionWith(result.Files);
            else result.SelectedFiles.Clear();
        }

        private void ShowSelectedRuleFiles()
        {
            visibleFiles = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as CleanupScanResult;
            fileGrid.RowCount = visibleFiles == null ? 0 : visibleFiles.Files.Count;
            fileGrid.Invalidate();
        }

        private void FileGrid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            string path = FileAt(e.RowIndex);
            if (path == null) return;
            string column = fileGrid.Columns[e.ColumnIndex].Name;
            if (column == "Selected") e.Value = visibleFiles.SelectedFiles.Contains(path);
            else if (column == "Name") e.Value = Path.GetFileName(path);
            else if (column == "Path") e.Value = path;
            else
            {
                try
                {
                    var info = new FileInfo(path);
                    if (column == "Size") e.Value = DisplayFormat.Bytes(info.Length);
                    else if (column == "Modified") e.Value = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                }
                catch { e.Value = "--"; }
            }
        }

        private void FileGrid_CellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            if (visibleFiles == null || fileGrid.Columns[e.ColumnIndex].Name != "Selected") return;
            string path = FileAt(e.RowIndex);
            if (path == null) return;
            if (Convert.ToBoolean(e.Value)) visibleFiles.SelectedFiles.Add(path);
            else visibleFiles.SelectedFiles.Remove(path);
            DataGridViewRow ruleRow = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => ReferenceEquals(row.Tag, visibleFiles));
            if (ruleRow != null)
            {
                changingSelection = true;
                ruleRow.Cells["Selected"].Value = visibleFiles.SelectedFiles.Count > 0;
                changingSelection = false;
            }
            status.Text = string.Format("{0}：已选择 {1:N0}/{2:N0} 个文件。", visibleFiles.Rule.Name, visibleFiles.SelectedFiles.Count, visibleFiles.FileCount);
        }

        private void FileGrid_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            string path = FileAt(e.RowIndex);
            if (path != null && visibleFiles != null)
                e.ToolTipText = visibleFiles.Rule.Risk + " | " + visibleFiles.Rule.Description + Environment.NewLine + path;
        }

        private string FileAt(int rowIndex)
        {
            return visibleFiles != null && rowIndex >= 0 && rowIndex < visibleFiles.Files.Count
                ? visibleFiles.Files[rowIndex]
                : null;
        }

        private string CurrentFilePath()
        {
            return fileGrid.CurrentCell == null ? null : FileAt(fileGrid.CurrentCell.RowIndex);
        }

        private void OpenCurrentFileLocation()
        {
            string path = CurrentFilePath();
            if (path == null) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }

        private void CopyCurrentFilePath()
        {
            string path = CurrentFilePath();
            if (path != null) Clipboard.SetText(path);
        }

        private void ShowCurrentFileOperation()
        {
            string path = CurrentFilePath();
            if (path == null || visibleFiles == null) return;
            MessageBox.Show("规则：" + visibleFiles.Rule.Name + "\n风险：" + visibleFiles.Rule.Risk +
                "\n操作：可用其它磁盘时先复制备份，然后调用 File.Delete；最后仅删除空目录。\n\n" + path,
                "底层操作", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task CleanCurrentFileAsync()
        {
            string path = CurrentFilePath();
            if (path == null || visibleFiles == null || visibleFiles.Rule.ScanOnly) return;
            string backupRoot;
            bool createBackup = BackupStore.TryGetSpaceReleasingRoot(out backupRoot);
            string notice = createBackup ? "备份位置：" + backupRoot : "没有其它可用磁盘，本次不会备份。";
            if (MessageBox.Show("确定清理此文件？\n" + notice + "\n\n" + path, "单独清理",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            long oneBytes = 0;
            try { if (File.Exists(path)) oneBytes = new FileInfo(path).Length; } catch { }
            var one = new CleanupScanResult
            {
                Rule = visibleFiles.Rule,
                Files = new List<string> { path },
                SelectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path },
                Roots = new List<string>(),
                FileCount = 1,
                Bytes = oneBytes
            };
            cancellation = new CancellationTokenSource();
            SetBusy(true, "取消清理");
            busyOverlay.Start("正在清理文件", DisplayFormat.SingleLine(path, 90));
            try
            {
                CleanupResult result = await service.CleanAsync(new[] { one }, createBackup ? backupRoot : null, null, cancellation.Token);
                if (result.DeletedFiles == 1)
                {
                    visibleFiles.Files.Remove(path);
                    visibleFiles.SelectedFiles.Remove(path);
                    visibleFiles.FileCount--;
                    visibleFiles.Bytes -= one.Bytes;
                    fileGrid.RowCount = visibleFiles.Files.Count;
                    fileGrid.Invalidate();
                    status.Text = "已清理：" + path;
                }
                else MessageBox.Show("文件未能删除，请查看操作日志。", "单独清理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException) { status.Text = "单文件清理已取消。"; }
            catch (Exception ex)
            {
                OperationLogger.Error("单文件清理", ex.Message);
                MessageBox.Show(ex.Message, "单文件清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                busyOverlay.Stop();
                SetBusy(false, "扫描");
            }
        }

        private void AddCurrentFileToWhitelist()
        {
            string path = CurrentFilePath();
            if (path == null) return;
            string error;
            if (!CleanupWhitelist.Add(path, out error)) MessageBox.Show(error, "添加白名单失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else status.Text = "已添加清理白名单：" + path;
        }

        private void OpenOperationLog()
        {
            string directory = Path.GetDirectoryName(OperationLogger.FilePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            if (!File.Exists(OperationLogger.FilePath)) File.WriteAllText(OperationLogger.FilePath, "", System.Text.Encoding.UTF8);
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + OperationLogger.FilePath + "\"") { UseShellExecute = true });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }
    }
}
