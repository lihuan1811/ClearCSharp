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
        private readonly Button uninstallButton = UiFactory.SecondaryButton("卸载选中");
        private readonly Label status = UiFactory.StatusLabel("正在准备应用列表...");
        private readonly ProgressBar progress = new ProgressBar();
        private IList<InstalledApp> allApps = new List<InstalledApp>();
        private InstalledAppKind currentKind = InstalledAppKind.Desktop;
        private CancellationTokenSource cancellation;

        public UninstallDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "软件卸载",
                "桌面应用与 Windows 商城应用分开显示，卸载前会再次确认。",
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
                Height = 58,
                ColumnCount = 3,
                Padding = new Padding(0, 8, 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(status, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8, 8, 8, 8);
            bottom.Controls.Add(progress, 1, 0);
            uninstallButton.Width = 110;
            bottom.Controls.Add(uninstallButton, 2, 0);

            Controls.Add(grid);
            Controls.Add(bottom);
            Controls.Add(toolbar);
            Controls.Add(header);

            desktopButton.Click += async delegate { await SwitchKindAsync(InstalledAppKind.Desktop); };
            storeButton.Click += async delegate { await SwitchKindAsync(InstalledAppKind.Store); };
            refreshButton.Click += async delegate { await LoadAppsAsync(); };
            uninstallButton.Click += async delegate { await UninstallSelectedAsync(); };
            searchBox.TextChanged += delegate { ApplyFilter(); };
            Load += async delegate { await SwitchKindAsync(InstalledAppKind.Desktop); };
        }

        private void ConfigureGrid()
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "名称",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 250
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Version",
                HeaderText = "版本",
                Width = 135
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Publisher",
                HeaderText = "发布者",
                Width = 210
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Size",
                HeaderText = "估算大小",
                Width = 105
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Kind",
                HeaderText = "类型",
                Width = 125
            });
        }

        private async Task SwitchKindAsync(InstalledAppKind kind)
        {
            currentKind = kind;
            desktopButton.BackColor = kind == InstalledAppKind.Desktop ? AppPalette.Green : Color.White;
            desktopButton.ForeColor = kind == InstalledAppKind.Desktop ? Color.White : AppPalette.Green;
            storeButton.BackColor = kind == InstalledAppKind.Store ? AppPalette.Green : Color.White;
            storeButton.ForeColor = kind == InstalledAppKind.Store ? Color.White : AppPalette.Green;
            await LoadAppsAsync();
        }

        private async Task LoadAppsAsync()
        {
            if (cancellation != null) cancellation.Cancel();
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            status.Text = currentKind == InstalledAppKind.Desktop ? "正在读取注册表应用列表..." : "正在读取 Windows 商城应用...";
            try
            {
                allApps = await service.LoadAsync(currentKind, cancellation.Token);
                ApplyFilter();
                status.Text = string.Format("已加载 {0:N0} 个{1}。", allApps.Count,
                    currentKind == InstalledAppKind.Desktop ? "桌面应用" : "商城应用");
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
                if (cancellation != null)
                {
                    cancellation.Dispose();
                    cancellation = null;
                }
                SetBusy(false);
            }
        }

        private void ApplyFilter()
        {
            string filter = searchBox.Text.Trim();
            grid.Rows.Clear();
            IEnumerable<InstalledApp> apps = allApps;
            if (filter.Length > 0)
            {
                apps = apps.Where(app =>
                    app.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (app.Publisher ?? string.Empty).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
            }

            foreach (InstalledApp app in apps)
            {
                int index = grid.Rows.Add(
                    app.Name,
                    app.Version,
                    app.Publisher,
                    app.SizeText,
                    app.Kind == InstalledAppKind.Desktop ? "应用程序" : "Windows 商城应用");
                grid.Rows[index].Tag = app;
            }
        }

        private async Task UninstallSelectedAsync()
        {
            if (grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("请先选择一个应用。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var app = grid.SelectedRows[0].Tag as InstalledApp;
            if (app == null) return;

            DialogResult answer = MessageBox.Show(
                "即将卸载：" + app.Name + "\n\n该操作由应用自己的卸载程序或 Windows Remove-AppxPackage 执行，是否继续？",
                "确认卸载",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            SetBusy(true);
            status.Text = "正在启动卸载：" + app.Name;
            var source = new CancellationTokenSource();
            try
            {
                ProcessResult result = await service.UninstallAsync(app, source.Token);
                if (!result.Success)
                {
                    string error = string.IsNullOrWhiteSpace(result.Error) ? "卸载命令执行失败。" : result.Error;
                    OperationLogger.Error("软件卸载", app.Name + "：" + error);
                    MessageBox.Show(error, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    status.Text = "卸载失败：" + app.Name;
                    return;
                }

                OperationLogger.Info("软件卸载", "已执行卸载：" + app.Name);
                status.Text = app.Kind == InstalledAppKind.Desktop
                    ? "卸载程序已启动，请按卸载向导完成操作。"
                    : "商城应用已卸载。";
                if (app.Kind == InstalledAppKind.Store) await LoadAppsAsync();
            }
            catch (Exception ex)
            {
                status.Text = "卸载失败：" + ex.Message;
                OperationLogger.Error("软件卸载", app.Name + "：" + ex.Message);
                MessageBox.Show(ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                source.Dispose();
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            refreshButton.Enabled = !busy;
            uninstallButton.Enabled = !busy;
            desktopButton.Enabled = !busy;
            storeButton.Enabled = !busy;
            grid.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) progress.Value = 0;
        }
    }
}
