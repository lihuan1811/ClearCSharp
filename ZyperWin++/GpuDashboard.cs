using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class GpuDashboard : UserControl
    {
        private readonly GpuService service = new GpuService();
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly Button refreshButton = UiFactory.PrimaryButton("重新检测");
        private readonly Button openButton = UiFactory.SecondaryButton("打开官方控制面板");
        private readonly Label detail = UiFactory.StatusLabel("只显示驱动和当前硬件实际支持的检测或官方操作入口。");
        private readonly ProgressBar progress = new ProgressBar();
        private IList<GpuInfo> devices = new List<GpuInfo>();

        public GpuDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "显卡优化",
                "自动识别 NVIDIA / AMD / Intel，读取驱动、显存、温度、负载与受支持入口。",
                out headerActions);
            refreshButton.Width = 102;
            headerActions.Controls.Add(refreshButton);

            ConfigureGrid();
            grid.SelectionChanged += delegate { ShowSelectedDetails(); };

            var information = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 116,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            detail.Dock = DockStyle.Fill;
            detail.TextAlign = ContentAlignment.TopLeft;
            detail.AutoEllipsis = false;
            openButton.Dock = DockStyle.Right;
            openButton.Width = 170;
            openButton.Enabled = false;
            information.Controls.Add(detail);
            information.Controls.Add(openButton);

            progress.Dock = DockStyle.Bottom;
            progress.Height = 22;
            progress.Margin = new Padding(0, 8, 0, 8);

            Controls.Add(grid);
            Controls.Add(progress);
            Controls.Add(information);
            Controls.Add(header);

            refreshButton.Click += async delegate { await DetectAsync(); };
            openButton.Click += OpenButton_Click;
            Load += async delegate { await DetectAsync(); };
        }

        private void ConfigureGrid()
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "显卡",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 260
            });
            grid.Columns.Add(Column("Vendor", "厂商", 90));
            grid.Columns.Add(Column("Driver", "驱动版本", 145));
            grid.Columns.Add(Column("Memory", "显存", 105));
            grid.Columns.Add(Column("Used", "已用显存", 105));
            grid.Columns.Add(Column("Temperature", "温度", 82));
            grid.Columns.Add(Column("Load", "负载", 82));
            grid.Columns.Add(Column("Api", "支持检测", 165));
        }

        private static DataGridViewTextBoxColumn Column(string name, string title, int width)
        {
            return new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width };
        }

        private async Task DetectAsync()
        {
            SetBusy(true);
            detail.Text = "正在读取显示适配器和厂商驱动能力...";
            try
            {
                devices = await service.DetectAsync(CancellationToken.None);
                grid.Rows.Clear();
                foreach (GpuInfo gpu in devices)
                {
                    string api = gpu.NvidiaSmiAvailable ? "nvidia-smi" :
                        gpu.AdlxEntryAvailable ? "AMD 官方入口" :
                        !string.IsNullOrWhiteSpace(gpu.ControlPanelPath) ? "厂商官方入口" : "基础信息";
                    int index = grid.Rows.Add(
                        gpu.Name,
                        VendorName(gpu.Vendor),
                        gpu.DriverVersion,
                        gpu.DedicatedMemoryBytes > 0 ? DisplayFormat.Bytes(gpu.DedicatedMemoryBytes) : "--",
                        gpu.UsedMemoryBytes.HasValue ? DisplayFormat.Bytes(gpu.UsedMemoryBytes.Value) : "--",
                        gpu.TemperatureCelsius.HasValue ? gpu.TemperatureCelsius.Value + " °C" : "--",
                        gpu.UtilizationPercent.HasValue ? gpu.UtilizationPercent.Value + "%" : "--",
                        api);
                    grid.Rows[index].Tag = gpu;
                }
                detail.Text = devices.Count == 0 ? "未检测到显示适配器。" : "检测完成，请选择显卡查看受支持操作。";
                OperationLogger.Info("显卡检测", "检测到 " + devices.Count + " 个显示适配器");
                if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
            }
            catch (Exception ex)
            {
                detail.Text = "检测失败：" + ex.Message;
                OperationLogger.Error("显卡检测", ex.Message);
            }
            finally
            {
                SetBusy(false);
                ShowSelectedDetails();
            }
        }

        private void ShowSelectedDetails()
        {
            if (grid.SelectedRows.Count == 0)
            {
                openButton.Enabled = false;
                return;
            }
            var gpu = grid.SelectedRows[0].Tag as GpuInfo;
            if (gpu == null) return;
            string operations = gpu.SupportedOperations.Count == 0
                ? "当前驱动未公开可调用的调优入口，仅显示基础信息。"
                : "当前支持：" + string.Join("；", gpu.SupportedOperations.Distinct());
            detail.Text = gpu.Name + Environment.NewLine + operations +
                Environment.NewLine + "本模块不会绕过驱动权限，也不会自动套用跨厂商超频参数。";
            openButton.Enabled = !string.IsNullOrWhiteSpace(gpu.ControlPanelPath);
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            if (grid.SelectedRows.Count == 0) return;
            var gpu = grid.SelectedRows[0].Tag as GpuInfo;
            if (gpu == null || string.IsNullOrWhiteSpace(gpu.ControlPanelPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = gpu.ControlPanelPath, UseShellExecute = true });
                OperationLogger.Info("显卡优化", "打开厂商官方控制面板：" + gpu.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "无法打开控制面板", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy)
        {
            refreshButton.Enabled = !busy;
            grid.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) progress.Value = 0;
        }

        private static string VendorName(GpuVendor vendor)
        {
            switch (vendor)
            {
                case GpuVendor.Nvidia: return "NVIDIA";
                case GpuVendor.Amd: return "AMD";
                case GpuVendor.Intel: return "Intel";
                default: return "未知";
            }
        }
    }
}
