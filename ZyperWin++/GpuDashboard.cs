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
        private readonly DataGridView operationGrid = UiFactory.Grid();
        private readonly SplitContainer split = new SplitContainer();
        private readonly Button refreshButton = UiFactory.PrimaryButton("重新检测");
        private readonly Button openButton = UiFactory.SecondaryButton("打开官方控制面板");
        private readonly Button applyButton = UiFactory.PrimaryButton("应用选中");
        private readonly Button restoreButton = UiFactory.SecondaryButton("还原选中");
        private readonly Label detail = UiFactory.StatusLabel("只显示驱动和当前硬件实际支持的检测或官方操作入口。");
        private readonly ProgressBar progress = new ProgressBar();
        private IList<GpuInfo> devices = new List<GpuInfo>();
        private CancellationTokenSource cancellation;

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
            ConfigureOperationGrid();
            grid.SelectionChanged += delegate { ShowSelectedDetails(); };
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterDistance = 280;
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(operationGrid);

            var information = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 94,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            detail.Dock = DockStyle.Fill;
            detail.TextAlign = ContentAlignment.TopLeft;
            detail.AutoEllipsis = false;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 382, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            openButton.Width = 166;
            applyButton.Width = 96;
            restoreButton.Width = 96;
            openButton.Enabled = false;
            applyButton.Enabled = false;
            restoreButton.Enabled = false;
            information.Controls.Add(detail);
            actions.Controls.Add(openButton);
            actions.Controls.Add(restoreButton);
            actions.Controls.Add(applyButton);
            information.Controls.Add(actions);

            progress.Dock = DockStyle.Bottom;
            progress.Height = 22;
            progress.Margin = new Padding(0, 8, 0, 8);

            Controls.Add(split);
            Controls.Add(progress);
            Controls.Add(information);
            Controls.Add(header);

            refreshButton.Click += async delegate { await DetectAsync(); };
            openButton.Click += OpenButton_Click;
            applyButton.Click += async delegate { await ApplySelectedAsync(); };
            restoreButton.Click += async delegate { await RestoreSelectedAsync(); };
            Load += async delegate { await DetectAsync(); };
        }

        private void ConfigureOperationGrid()
        {
            operationGrid.ReadOnly = false;
            operationGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "选择", Width = 58, ReadOnly = false });
            operationGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "当前机器支持的操作", Width = 220, ReadOnly = true });
            operationGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "状态", Width = 110, ReadOnly = true });
            operationGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Description",
                HeaderText = "作用与还原方式",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 320,
                ReadOnly = true
            });
            operationGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (operationGrid.IsCurrentCellDirty) operationGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            operationGrid.CellValueChanged += delegate { UpdateOperationButtons(); };
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
            if (cancellation != null) cancellation.Cancel();
            var source = new CancellationTokenSource();
            cancellation = source;
            SetBusy(true);
            detail.Text = "正在读取显示适配器和厂商驱动能力...";
            try
            {
                IList<GpuInfo> detected = await service.DetectAsync(source.Token);
                if (source.IsCancellationRequested || IsDisposed) return;
                devices = detected;
                grid.Rows.Clear();
                operationGrid.Rows.Clear();
                foreach (GpuInfo gpu in devices)
                {
                    string api = gpu.NvidiaSmiAvailable ? "nvidia-smi" :
                        gpu.AdlxEntryAvailable ? "AMD ADLX" :
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
            catch (OperationCanceledException)
            {
                if (!IsDisposed) detail.Text = "显卡检测已取消。";
            }
            catch (Exception ex)
            {
                detail.Text = "检测失败：" + ex.Message;
                OperationLogger.Error("显卡检测", ex.Message);
            }
            finally
            {
                bool isCurrent = ReferenceEquals(cancellation, source);
                if (isCurrent) cancellation = null;
                source.Dispose();
                if (isCurrent && !IsDisposed)
                {
                    SetBusy(false);
                    ShowSelectedDetails();
                }
            }
        }

        private void ShowSelectedDetails()
        {
            if (grid.SelectedRows.Count == 0)
            {
                openButton.Enabled = false;
                operationGrid.Rows.Clear();
                UpdateOperationButtons();
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
            operationGrid.Rows.Clear();
            foreach (GpuOptimizationOperation operation in gpu.OptimizationOperations)
            {
                int index = operationGrid.Rows.Add(false, operation.Name,
                    operation.CanRestore ? "可还原" : (operation.CanApply ? "可应用" : "当前已启用"), operation.Description);
                operationGrid.Rows[index].Tag = operation;
                if (!operation.CanApply && !operation.CanRestore)
                {
                    operationGrid.Rows[index].Cells["Selected"].ReadOnly = true;
                    operationGrid.Rows[index].DefaultCellStyle.ForeColor = AppPalette.Muted;
                }
            }
            UpdateOperationButtons();
        }

        private IList<GpuOptimizationOperation> SelectedOperations(Func<GpuOptimizationOperation, bool> predicate)
        {
            var selected = new List<GpuOptimizationOperation>();
            foreach (DataGridViewRow row in operationGrid.Rows)
            {
                var operation = row.Tag as GpuOptimizationOperation;
                if (operation != null && predicate(operation) && Convert.ToBoolean(row.Cells["Selected"].Value ?? false)) selected.Add(operation);
            }
            return selected;
        }

        private void UpdateOperationButtons()
        {
            bool idle = cancellation == null;
            applyButton.Enabled = idle && SelectedOperations(value => value.CanApply).Count > 0;
            restoreButton.Enabled = idle && SelectedOperations(value => value.CanRestore).Count > 0;
        }

        private async Task ApplySelectedAsync()
        {
            IList<GpuOptimizationOperation> operations = SelectedOperations(value => value.CanApply);
            if (operations.Count == 0) return;
            string names = string.Join("、", operations.Select(value => value.Name));
            if (MessageBox.Show("将应用：" + names + "\n\n每项操作都会写入日志和还原记录。是否继续？",
                "确认显卡优化", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunOperationsAsync("应用显卡优化", operations, false);
        }

        private async Task RestoreSelectedAsync()
        {
            IList<GpuOptimizationOperation> operations = SelectedOperations(value => value.CanRestore);
            if (operations.Count == 0) return;
            if (MessageBox.Show("将按操作记录还原：" + string.Join("、", operations.Select(value => value.Name)) + "\n\n是否继续？",
                "确认还原显卡优化", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunOperationsAsync("还原显卡优化", operations, true);
        }

        private async Task RunOperationsAsync(string title, IList<GpuOptimizationOperation> operations, bool restore)
        {
            var source = new CancellationTokenSource();
            cancellation = source;
            SetBusy(true);
            detail.Text = title + "正在执行...";
            try
            {
                FileOperationSummary result = restore
                    ? await service.RestoreAsync(operations, source.Token)
                    : await service.ApplyAsync(operations, source.Token);
                MessageBox.Show(result.Message, title, MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException) { detail.Text = title + "已取消。"; }
            catch (Exception ex)
            {
                OperationLogger.Error("显卡优化", ex.Message);
                MessageBox.Show(ex.Message, title + "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation = null;
                source.Dispose();
                if (!IsDisposed)
                {
                    SetBusy(false);
                    await DetectAsync();
                }
            }
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
            operationGrid.Enabled = !busy;
            var gpu = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as GpuInfo : null;
            openButton.Enabled = !busy && gpu != null && !string.IsNullOrWhiteSpace(gpu.ControlPanelPath);
            if (busy)
            {
                applyButton.Enabled = false;
                restoreButton.Enabled = false;
            }
            else UpdateOperationButtons();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }
    }
}
