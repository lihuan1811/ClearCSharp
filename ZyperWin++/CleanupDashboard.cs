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
        private readonly Button selectRecommendedButton = UiFactory.SecondaryButton("推荐项");
        private readonly Dictionary<CleanupKind, Button> kindButtons = new Dictionary<CleanupKind, Button>();
        private CancellationTokenSource cancellation;
        private CleanupKind currentKind = CleanupKind.DriveC;

        public CleanupDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "C 盘清理",
                "先扫描再清理，QQ 与微信只处理缓存、临时文件和日志。",
                out headerActions);

            foreach (CleanupKind kind in Enum.GetValues(typeof(CleanupKind)))
            {
                var button = UiFactory.SecondaryButton(KindName(kind));
                button.Width = 94;
                CleanupKind captured = kind;
                button.Click += delegate { SwitchKind(captured); };
                kindButtons[kind] = button;
                headerActions.Controls.Add(button);
            }

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
            selectRecommendedButton.Width = 92;
            scanButton.Width = 90;
            cleanButton.Width = 104;
            actions.Controls.Add(selectRecommendedButton);
            actions.Controls.Add(scanButton);
            actions.Controls.Add(cleanButton);
            bottom.Controls.Add(actions, 1, 1);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(header);

            scanButton.Click += async delegate { await ScanAsync(); };
            cleanButton.Click += async delegate { await CleanAsync(); };
            selectRecommendedButton.Click += delegate { SelectRecommended(); };
            SwitchKind(CleanupKind.DriveC);
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
                Name = "Name",
                HeaderText = "清理项",
                Width = 165,
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
                Width = 260,
                ReadOnly = true
            });
        }

        private void SwitchKind(CleanupKind kind)
        {
            if (cancellation != null) cancellation.Cancel();
            currentKind = kind;
            foreach (KeyValuePair<CleanupKind, Button> pair in kindButtons)
            {
                bool active = pair.Key == kind;
                pair.Value.BackColor = active ? AppPalette.Green : Color.White;
                pair.Value.ForeColor = active ? Color.White : AppPalette.Green;
            }
            PopulateRules();
            status.Text = "准备扫描" + KindName(kind) + "路径。";
        }

        private void PopulateRules()
        {
            grid.Rows.Clear();
            foreach (CleanupRule rule in CleanupCatalog.GetRules(currentKind))
            {
                int index = grid.Rows.Add(
                    rule.Recommended,
                    rule.Name,
                    rule.Description,
                    "待扫描",
                    "--",
                    string.Join("；", rule.PathTemplates.Select(Environment.ExpandEnvironmentVariables)));
                grid.Rows[index].Tag = rule;
            }
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
                IList<CleanupScanResult> results = await service.ScanAsync(currentKind, reporter, cancellation.Token);
                grid.Rows.Clear();
                foreach (CleanupScanResult result in results)
                {
                    int index = grid.Rows.Add(
                        result.Rule.Recommended,
                        result.Rule.Name,
                        result.Rule.Description,
                        DisplayFormat.Bytes(result.Bytes),
                        result.FileCount.ToString("N0"),
                        result.Roots.Count == 0 ? "未找到" : string.Join("；", result.Roots));
                    grid.Rows[index].Tag = result;
                }
                long total = results.Sum(value => value.Bytes);
                int files = results.Sum(value => value.FileCount);
                status.Text = string.Format("扫描完成：{0:N0} 个文件，可释放 {1}。", files, DisplayFormat.Bytes(total));
                OperationLogger.Info("清理扫描", KindName(currentKind) + "，可释放 " + DisplayFormat.Bytes(total));
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
                string.Format("将永久删除 {0:N0} 个缓存/临时文件，预计释放 {1}。\n\n清理删除不可还原，是否继续？", files, DisplayFormat.Bytes(bytes)),
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
            grid.Enabled = !busy;
            foreach (Button button in kindButtons.Values) button.Enabled = !busy;
        }

        private static string KindName(CleanupKind kind)
        {
            switch (kind)
            {
                case CleanupKind.QQ: return "QQ 专清";
                case CleanupKind.WeChat: return "微信专清";
                default: return "C 盘清理";
            }
        }
    }
}
