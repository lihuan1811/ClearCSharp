using System;
using System.Collections.Generic;
using System.Drawing;
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
        private readonly Label status = UiFactory.StatusLabel("请选择清理模块，然后开始扫描。");
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button scanButton = UiFactory.PrimaryButton("扫描");
        private readonly Button cleanButton = UiFactory.SecondaryButton("清理选中");
        private readonly Button selectRecommendedButton = UiFactory.SecondaryButton("默认");
        private readonly Button selectNoneButton = UiFactory.SecondaryButton("全不选");
        private CancellationTokenSource cancellation;

        public CleanupDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "C 盘深度清理",
                "按过期文件、系统相关、缓存、应用和临时文件细分；高风险目录只分析。",
                out headerActions);
            selectNoneButton.Width = 82;
            selectRecommendedButton.Width = 82;
            headerActions.Controls.Add(selectNoneButton);
            headerActions.Controls.Add(selectRecommendedButton);

            ConfigureGrid();

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

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(header);

            scanButton.Click += async delegate { await ScanAsync(); };
            cleanButton.Click += async delegate { await CleanAsync(); };
            selectRecommendedButton.Click += delegate { SelectRecommended(); };
            selectNoneButton.Click += delegate { SelectNone(); };
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

        private void PopulateRules()
        {
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
            try
            {
                var reporter = new Progress<string>(value => status.Text = value);
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
                        DisplayFormat.Bytes(result.Bytes),
                        result.FileCount.ToString("N0"),
                        result.Roots.Count == 0 ? "未找到" : string.Join("；", result.Roots));
                    grid.Rows[index].Tag = result;
                    if (result.Rule.ScanOnly)
                    {
                        grid.Rows[index].Cells["Selected"].ReadOnly = true;
                        grid.Rows[index].DefaultCellStyle.ForeColor = AppPalette.Muted;
                    }
                }
                long total = results.Sum(value => value.Bytes);
                int files = results.Sum(value => value.FileCount);
                status.Text = string.Format("扫描完成：{0:N0} 个文件，可释放 {1}。", files, DisplayFormat.Bytes(total));
                OperationLogger.Info("清理扫描", "C 盘深度清理，可释放 " + DisplayFormat.Bytes(total));
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
                if (isSelected && result != null && result.FileCount > 0) selected.Add(result);
            }
            if (selected.Count == 0)
            {
                MessageBox.Show("请先完成扫描并勾选需要清理的项目。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long bytes = selected.Sum(value => value.Bytes);
            int files = selected.Sum(value => value.FileCount);
            DialogResult answer = MessageBox.Show(
                string.Format("将删除 {0:N0} 个已扫描文件，预计释放 {1}。\n\n默认会先写入清理备份；仅分析项不会删除。是否继续？", files, DisplayFormat.Bytes(bytes)),
                "确认清理",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            cancellation = new CancellationTokenSource();
            SetBusy(true, "清理中...");
            progress.Style = ProgressBarStyle.Marquee;
            try
            {
                CleanupResult result = await service.CleanAsync(
                    selected,
                    new Progress<string>(value => status.Text = value),
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
                row.Cells["Selected"].Value = rule != null && rule.Recommended;
            }
        }

        private void SetBusy(bool busy, string scanText)
        {
            scanButton.Text = scanText;
            cleanButton.Enabled = !busy;
            selectRecommendedButton.Enabled = !busy;
            selectNoneButton.Enabled = !busy;
            grid.Enabled = !busy;
        }

        private void SelectNone()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                row.Cells["Selected"].Value = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }
    }
}
