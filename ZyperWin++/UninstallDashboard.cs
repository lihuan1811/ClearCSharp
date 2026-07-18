using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class UninstallDashboard : UserControl
    {
        private readonly UninstallService service = new UninstallService();
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly TextBox searchBox = new TextBox();
        private readonly Button desktopButton = UiFactory.SecondaryButton("应用程序");
        private readonly Button storeButton = UiFactory.SecondaryButton("Windows 商城应用");
        private readonly Button refreshButton = UiFactory.PrimaryButton("刷新列表");
        private readonly Button uninstallButton = UiFactory.PrimaryButton("批量卸载选中");
        private readonly Button forceButton = UiFactory.SecondaryButton("强力卸载");
        private readonly Button residualButton = UiFactory.SecondaryButton("清理卸载残留");
        private readonly Label status = UiFactory.StatusLabel("正在准备应用列表...");
        private readonly ProgressBar progress = new ProgressBar();
        private IList<InstalledApp> allApps = new List<InstalledApp>();
        private InstalledAppKind currentKind = InstalledAppKind.Desktop;
        private CancellationTokenSource loadCancellation;
        private CancellationTokenSource operationCancellation;

        public UninstallDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "软件强力卸载",
                "注册表应用与 Windows 商城应用分类显示，支持搜索、批量卸载、备份和残留清理。",
                out headerActions);
            desktopButton.Width = 104;
            storeButton.Width = 154;
            headerActions.Controls.Add(desktopButton);
            headerActions.Controls.Add(storeButton);

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                ColumnCount = 3,
                Padding = new Padding(0, 8, 0, 6),
                BackColor = AppPalette.Canvas
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Controls.Add(new Label
            {
                Text = "搜索应用",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Font = UiFactory.SectionFont,
                ForeColor = AppPalette.Text
            }, 0, 0);
            searchBox.Dock = DockStyle.Fill;
            searchBox.Font = UiFactory.BaseFont;
            searchBox.Margin = new Padding(0, 4, 10, 4);
            toolbar.Controls.Add(searchBox, 1, 0);
            refreshButton.Width = 96;
            toolbar.Controls.Add(refreshButton, 2, 0);

            ConfigureGrid();

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                ColumnCount = 3,
                Padding = new Padding(0, 8, 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(status, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8);
            bottom.Controls.Add(progress, 1, 0);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            residualButton.Width = 126;
            forceButton.Width = 100;
            uninstallButton.Width = 126;
            actions.Controls.Add(residualButton);
            actions.Controls.Add(forceButton);
            actions.Controls.Add(uninstallButton);
            bottom.Controls.Add(actions, 2, 0);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(toolbar);
            Controls.Add(header);

            desktopButton.Click += async delegate { await SwitchKindAsync(InstalledAppKind.Desktop); };
            storeButton.Click += async delegate { await SwitchKindAsync(InstalledAppKind.Store); };
            refreshButton.Click += async delegate { await LoadAppsAsync(); };
            uninstallButton.Click += async delegate { await UninstallSelectedAsync(false); };
            forceButton.Click += async delegate { await UninstallSelectedAsync(true); };
            residualButton.Click += async delegate { await CleanResidualsAsync(); };
            searchBox.TextChanged += delegate { ApplyFilter(); };
            Load += async delegate { await SwitchKindAsync(InstalledAppKind.Desktop); };
        }

        private void ConfigureGrid()
        {
            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 42, ReadOnly = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
            grid.Columns.Add(Column("Version", "版本", 115));
            grid.Columns.Add(Column("Publisher", "发布者", 170));
            grid.Columns.Add(Column("Size", "估算大小", 96));
            grid.Columns.Add(Column("Kind", "类型", 120));
            grid.Columns.Add(Column("InstallDate", "安装日期", 102));
            grid.Columns.Add(Column("InstallLocation", "安装路径", 230));
            foreach (DataGridViewColumn column in grid.Columns)
                if (column.Name != "Selected") column.ReadOnly = true;
            grid.Columns["Size"].SortMode = DataGridViewColumnSortMode.Automatic;
            grid.SortCompare += GridSortCompare;
        }

        private void GridSortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (grid.Columns[e.Column.Index].Name != "Size") return;
            var left = grid.Rows[e.RowIndex1].Tag as InstalledApp;
            var right = grid.Rows[e.RowIndex2].Tag as InstalledApp;
            e.SortResult = (left == null ? 0L : left.EstimatedBytes).CompareTo(right == null ? 0L : right.EstimatedBytes);
            if (e.SortResult == 0)
                e.SortResult = string.Compare(left == null ? string.Empty : left.Name, right == null ? string.Empty : right.Name,
                    StringComparison.CurrentCultureIgnoreCase);
            e.Handled = true;
        }

        private static DataGridViewTextBoxColumn Column(string name, string title, int width)
        {
            return new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width };
        }

        private async Task SwitchKindAsync(InstalledAppKind kind)
        {
            currentKind = kind;
            desktopButton.BackColor = kind == InstalledAppKind.Desktop ? AppPalette.Green : Color.White;
            desktopButton.ForeColor = kind == InstalledAppKind.Desktop ? Color.White : AppPalette.Green;
            storeButton.BackColor = kind == InstalledAppKind.Store ? AppPalette.Green : Color.White;
            storeButton.ForeColor = kind == InstalledAppKind.Store ? Color.White : AppPalette.Green;
            residualButton.Enabled = kind == InstalledAppKind.Desktop;
            forceButton.Enabled = kind == InstalledAppKind.Desktop;
            await LoadAppsAsync();
        }

        private async Task LoadAppsAsync()
        {
            if (loadCancellation != null) loadCancellation.Cancel();
            var source = new CancellationTokenSource();
            loadCancellation = source;
            InstalledAppKind requestedKind = currentKind;
            SetBusy(true);
            status.Text = currentKind == InstalledAppKind.Desktop ? "正在读取注册表应用列表..." : "正在读取 Windows 商城应用...";
            try
            {
                IList<InstalledApp> loaded = await service.LoadAsync(requestedKind, source.Token);
                if (source.IsCancellationRequested || requestedKind != currentKind) return;
                allApps = loaded;
                ApplyFilter();
                status.Text = string.Format("读取完成：{0} {1}项。", currentKind == InstalledAppKind.Desktop ? "应用程序" : "Windows 商城应用", allApps.Count);
            }
            catch (OperationCanceledException)
            {
                status.Text = "加载已取消。";
            }
            catch (Exception ex)
            {
                allApps = new List<InstalledApp>();
                ApplyFilter();
                status.Text = "加载失败：" + DisplayFormat.SingleLine(ex.Message, 180);
                OperationLogger.Error("软件卸载", ex.Message);
            }
            finally
            {
                bool isCurrentRequest = ReferenceEquals(loadCancellation, source);
                if (isCurrentRequest) loadCancellation = null;
                source.Dispose();
                if (isCurrentRequest && !IsDisposed) SetBusy(false);
            }
        }

        private void ApplyFilter()
        {
            string filter = searchBox.Text.Trim();
            IEnumerable<InstalledApp> apps = allApps;
            if (filter.Length > 0)
            {
                apps = apps.Where(app =>
                    app.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (app.Publisher ?? string.Empty).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (app.InstallLocation ?? string.Empty).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
            }
            grid.Rows.Clear();
            foreach (InstalledApp app in apps)
            {
                int index = grid.Rows.Add(false, app.Name, app.Version, app.Publisher, app.SizeText,
                    app.Kind == InstalledAppKind.Desktop ? "应用程序" : "Windows 商城应用",
                    FormatInstallDate(app.InstallDate), app.InstallLocation);
                grid.Rows[index].Tag = app;
            }
        }

        private List<InstalledApp> SelectedApps()
        {
            var selected = new List<InstalledApp>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (Convert.ToBoolean(row.Cells["Selected"].Value ?? false) && row.Tag is InstalledApp)
                    selected.Add((InstalledApp)row.Tag);
            }
            if (selected.Count == 0 && grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is InstalledApp)
                selected.Add((InstalledApp)grid.SelectedRows[0].Tag);
            return selected;
        }

        private async Task UninstallSelectedAsync(bool force)
        {
            List<InstalledApp> selected = SelectedApps();
            if (selected.Count == 0)
            {
                MessageBox.Show("请先勾选要卸载的应用。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string preview = string.Join(Environment.NewLine, selected.Take(8).Select(app => app.Name));
            string warning = force ? "\n\n强力模式会在卸载完成后删除确认过的安装目录、启动项和快捷方式残留。" : string.Empty;
            if (MessageBox.Show("即将卸载 " + selected.Count + " 个应用：\n\n" + preview + warning + "\n\n是否继续？",
                force ? "确认强力卸载" : "确认批量卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            AppOperationScope operationScope = null;
            SetBusy(true);
            operationCancellation = new CancellationTokenSource();
            progress.Style = ProgressBarStyle.Continuous;
            progress.Minimum = 0;
            progress.Maximum = selected.Count;
            progress.Value = 0;
            int succeeded = 0;
            var failures = new List<string>();
            var residuals = new List<UninstallResidualScan>();
            try
            {
                operationScope = AppOperationCoordinator.Begin(force ? "软件强力卸载" : "软件批量卸载");
                for (int index = 0; index < selected.Count; index++)
                {
                    InstalledApp app = selected[index];
                    status.Text = string.Format("正在卸载 {0}/{1}：{2}", index + 1, selected.Count, app.Name);
                    ProcessResult result = await service.UninstallAsync(app, operationCancellation.Token);
                    if (result.Success)
                    {
                        succeeded++;
                        if (app.Kind == InstalledAppKind.Desktop)
                        {
                            if (force)
                            {
                                ProcessResult residual = await service.CleanResidualsAsync(app, true, operationCancellation.Token);
                                if (!residual.Success) failures.Add(app.Name + " 残留：" + residual.Error);
                            }
                            else
                            {
                                status.Text = "正在扫描卸载残留：" + app.Name;
                                await Task.Delay(1200, operationCancellation.Token);
                                UninstallResidualScan scan = await service.ScanResidualsAsync(app, operationCancellation.Token);
                                if (scan.Count > 0) residuals.Add(scan);
                            }
                        }
                    }
                    else failures.Add(app.Name + "：" + (string.IsNullOrWhiteSpace(result.Error) ? "卸载命令失败" : result.Error));
                    progress.Value = index + 1;
                }
                int cleanedResiduals = 0;
                if (!force && residuals.Count > 0)
                {
                    string summary = string.Join(Environment.NewLine, residuals.Take(8).Select(value => value.Summary()));
                    if (MessageBox.Show("卸载完成后检测到 " + residuals.Sum(value => value.Count) + " 项残留：\n\n" + summary +
                        "\n\n是否立即一键清理这些注册表项、启动项、快捷方式和安装目录？",
                        "发现卸载残留", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        foreach (UninstallResidualScan scan in residuals)
                        {
                            status.Text = "正在清理卸载残留：" + scan.App.Name;
                            ProcessResult clean = await service.CleanResidualsAsync(scan.App, true, operationCancellation.Token);
                            if (clean.Success) cleanedResiduals += scan.Count;
                            else failures.Add(scan.App.Name + " 残留：" + clean.Error);
                        }
                    }
                }
                status.Text = string.Format("卸载完成：成功 {0}，失败 {1}，发现残留 {2} 项，已清理 {3} 项。",
                    succeeded, failures.Count, residuals.Sum(value => value.Count), cleanedResiduals);
                OperationLogger.Info("软件卸载", status.Text);
                if (failures.Count > 0)
                    MessageBox.Show(string.Join(Environment.NewLine, failures.Take(10)), "部分卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                status.Text = "卸载已取消。";
            }
            catch (Exception ex)
            {
                status.Text = "卸载失败：" + ex.Message;
                OperationLogger.Error("软件卸载", ex.Message);
                MessageBox.Show(ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                operationCancellation.Dispose();
                operationCancellation = null;
                if (operationScope != null) operationScope.Dispose();
                if (!IsDisposed)
                {
                    SetBusy(false);
                    await LoadAppsAsync();
                }
            }
        }

        private async Task CleanResidualsAsync()
        {
            List<InstalledApp> selected = SelectedApps();
            InstalledApp app = selected.FirstOrDefault();
            if (app == null || app.Kind != InstalledAppKind.Desktop)
            {
                MessageBox.Show("请选择一个桌面应用。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AppOperationScope operationScope = null;
            SetBusy(true);
            operationCancellation = new CancellationTokenSource();
            try
            {
                operationScope = AppOperationCoordinator.Begin("卸载残留清理");
                status.Text = "正在扫描卸载残留：" + app.Name;
                UninstallResidualScan scan = await service.ScanResidualsAsync(app, operationCancellation.Token);
                if (scan.Count == 0)
                {
                    status.Text = "未检测到该应用的可清理残留。";
                    MessageBox.Show(status.Text, "卸载残留", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show("检测到以下残留：\n\n" + scan.Summary() +
                    "\n\n将先导出仍存在的卸载注册表项，再清理上述内容。是否继续？",
                    "确认清理卸载残留", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                ProcessResult result = await service.CleanResidualsAsync(app, true, operationCancellation.Token);
                status.Text = result.Success ? "卸载残留清理完成。" : "部分残留清理失败。";
                MessageBox.Show((result.Output + Environment.NewLine + result.Error).Trim(), status.Text,
                    MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                status.Text = "残留清理已取消。";
            }
            catch (Exception ex)
            {
                status.Text = "残留清理失败：" + DisplayFormat.SingleLine(ex.Message, 160);
                OperationLogger.Error("卸载残留", ex.Message);
                MessageBox.Show(ex.Message, "残留清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                operationCancellation.Dispose();
                operationCancellation = null;
                if (operationScope != null) operationScope.Dispose();
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            refreshButton.Enabled = !busy;
            uninstallButton.Enabled = !busy;
            desktopButton.Enabled = !busy;
            storeButton.Enabled = !busy;
            forceButton.Enabled = !busy && currentKind == InstalledAppKind.Desktop;
            residualButton.Enabled = !busy && currentKind == InstalledAppKind.Desktop;
            grid.Enabled = !busy;
            if (busy && progress.Style != ProgressBarStyle.Continuous) progress.Style = ProgressBarStyle.Marquee;
            if (!busy)
            {
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = 0;
            }
        }

        private static string FormatInstallDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 8) return value;
            return value.Substring(0, 4) + "-" + value.Substring(4, 2) + "-" + value.Substring(6, 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (loadCancellation != null) loadCancellation.Cancel();
                if (operationCancellation != null) operationCancellation.Cancel();
            }
            base.Dispose(disposing);
        }
    }
}
