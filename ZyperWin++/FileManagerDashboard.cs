using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class DiskVisualizationDashboard : UserControl
    {
        private readonly DiskAnalysisService service = new DiskAnalysisService();
        private readonly DataGridView fileGrid = UiFactory.Grid();
        private readonly DataGridView extensionGrid = UiFactory.Grid();
        private readonly TreemapControl treemap = new TreemapControl();
        private readonly TextBox pathBox = new TextBox();
        private readonly ComboBox driveBox = new ComboBox();
        private readonly Label capacityLabel = UiFactory.StatusLabel("正在读取磁盘容量...");
        private readonly Button browseButton = UiFactory.SecondaryButton("选择目录");
        private readonly Button scanButton = UiFactory.PrimaryButton("开始扫描");
        private readonly Button driveCButton = UiFactory.SecondaryButton("C盘");
        private readonly Button driveDButton = UiFactory.SecondaryButton("D盘");
        private readonly Button refreshDrivesButton = UiFactory.SecondaryButton("刷新磁盘");
        private readonly Button upButton = UiFactory.SecondaryButton("上一级");
        private readonly BusyAnimationOverlay busyOverlay = new BusyAnimationOverlay();
        private readonly Label status = UiFactory.StatusLabel("选择目录后开始扫描，扫描过程可取消。");
        private readonly ProgressBar progress = new ProgressBar { Visible = false };
        private readonly ColumnStyle progressColumnStyle = new ColumnStyle(SizeType.Absolute, 0F);
        private readonly SplitContainer gridSplit;
        private readonly SplitContainer treemapSplit;
        private CancellationTokenSource cancellation;
        private DiskAnalysisResult analysis;
        private DiskNode currentNode;

        private string selectedExtension;

        public DiskVisualizationDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel headerActions;
            Panel header = UiFactory.Header(
                "文件管理",
                "目录占用、扩展名统计、最大文件与可下钻方格图。",
                out headerActions);
            upButton.Width = 88;
            upButton.Enabled = false;
            headerActions.Controls.Add(upButton);

            var pathBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 88,
                ColumnCount = 7,
                RowCount = 2,
                Padding = new Padding(0, 6, 0, 4)
            };
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            pathBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            pathBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            pathBar.Controls.Add(new Label
            {
                Text = "选择磁盘",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiFactory.SectionFont,
                ForeColor = AppPalette.Text
            }, 0, 0);
            driveBox.Dock = DockStyle.Fill;
            driveBox.DropDownStyle = ComboBoxStyle.DropDownList;
            driveBox.Font = UiFactory.BaseFont;
            driveBox.Margin = new Padding(0, 4, 8, 4);
            pathBar.Controls.Add(driveBox, 1, 0);
            capacityLabel.Dock = DockStyle.Fill;
            capacityLabel.TextAlign = ContentAlignment.MiddleLeft;
            pathBar.Controls.Add(capacityLabel, 2, 0);
            driveCButton.Width = 58;
            driveDButton.Width = 58;
            refreshDrivesButton.Width = 88;
            pathBar.Controls.Add(driveCButton, 3, 0);
            pathBar.Controls.Add(driveDButton, 4, 0);
            pathBar.Controls.Add(refreshDrivesButton, 5, 0);
            pathBar.Controls.Add(new Label
            {
                Text = "当前目录",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiFactory.SectionFont,
                ForeColor = AppPalette.Text
            }, 0, 1);
            pathBox.Dock = DockStyle.Fill;
            pathBox.Font = UiFactory.BaseFont;
            pathBox.Text = Environment.GetEnvironmentVariable("SystemDrive") + "\\";
            pathBox.Margin = new Padding(0, 4, 8, 4);
            pathBar.Controls.Add(pathBox, 1, 1);
            pathBar.SetColumnSpan(pathBox, 4);
            browseButton.Width = 98;
            scanButton.Width = 98;
            pathBar.Controls.Add(browseButton, 5, 1);
            pathBar.Controls.Add(scanButton, 6, 1);

            ConfigureFileGrid();
            ConfigureExtensionGrid();

            gridSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Size = new Size(1040, 280),
                SplitterDistance = 680,
                SplitterWidth = 5,
                BackColor = AppPalette.Border,
                Panel1MinSize = 500,
                Panel2MinSize = 220
            };
            gridSplit.Panel1.Controls.Add(fileGrid);
            gridSplit.Panel2.Controls.Add(extensionGrid);

            treemapSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Size = new Size(1040, 510),
                SplitterDistance = 170,
                SplitterWidth = 5,
                BackColor = AppPalette.Border,
                Panel1MinSize = 110,
                Panel2MinSize = 110
            };
            treemapSplit.Panel1.Controls.Add(gridSplit);
            treemap.Dock = DockStyle.Fill;
            treemapSplit.Panel2.Controls.Add(treemap);
            var contentHost = new Panel { Dock = DockStyle.Fill, BackColor = AppPalette.Canvas };
            contentHost.Controls.Add(treemapSplit);
            busyOverlay.AttachTo(contentHost);

            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                ColumnCount = 2,
                Padding = new Padding(0, 5, 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(progressColumnStyle);
            bottom.Controls.Add(status, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8, 4, 0, 4);
            bottom.Controls.Add(progress, 1, 0);

            Controls.Add(contentHost);
            Controls.Add(bottom);
            Controls.Add(pathBar);
            Controls.Add(header);

            browseButton.Click += BrowseButton_Click;
            driveBox.SelectedIndexChanged += delegate { SelectDriveFromList(); };
            driveCButton.Click += delegate { SelectDrive("C:\\"); };
            driveDButton.Click += delegate { SelectDrive("D:\\"); };
            refreshDrivesButton.Click += delegate { PopulateDrives(); };
            scanButton.Click += async delegate { await ScanAsync(); };
            upButton.Click += delegate { NavigateToParent(); };
            fileGrid.CellDoubleClick += FileGrid_CellDoubleClick;
            fileGrid.CellPainting += FileGrid_CellPainting;
            extensionGrid.CellClick += ExtensionGrid_CellClick;
            treemap.NodeSelected += Treemap_NodeSelected;
            gridSplit.Resize += delegate { SetSplitterRatio(gridSplit, 0.68d); };
            treemapSplit.Resize += delegate { SetSplitterRatio(treemapSplit, 0.52d); };
            PopulateDrives();
        }

        internal int FileTablePanelHeightForTests { get { return treemapSplit.Panel1.ClientSize.Height; } }
        internal int TreemapPanelHeightForTests { get { return treemapSplit.Panel2.ClientSize.Height; } }
        internal DataGridViewColumn PercentBarColumnForTests { get { return fileGrid.Columns["ChildPercent"]; } }
        internal float ProgressColumnWidthForTests { get { return progressColumnStyle.Width; } }

        private void PopulateDrives()
        {
            string currentRoot = null;
            try { currentRoot = Path.GetPathRoot(pathBox.Text); } catch { }
            driveBox.BeginUpdate();
            driveBox.Items.Clear();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives()
                    .Where(value => value.IsReady && (value.DriveType == DriveType.Fixed || value.DriveType == DriveType.Removable))
                    .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
                    driveBox.Items.Add(new DriveItem(drive));
            }
            catch (Exception ex) { OperationLogger.Error("磁盘枚举", ex.Message); }
            driveBox.EndUpdate();
            driveDButton.Enabled = driveBox.Items.Cast<DriveItem>().Any(value => string.Equals(value.Root, "D:\\", StringComparison.OrdinalIgnoreCase));
            DriveItem selected = driveBox.Items.Cast<DriveItem>().FirstOrDefault(value =>
                string.Equals(value.Root, currentRoot, StringComparison.OrdinalIgnoreCase));
            if (selected != null) driveBox.SelectedItem = selected;
            else if (driveBox.Items.Count > 0) driveBox.SelectedIndex = 0;
            else capacityLabel.Text = "没有可用的固定磁盘或移动磁盘";
        }

        private void SelectDrive(string root)
        {
            DriveItem item = driveBox.Items.Cast<DriveItem>().FirstOrDefault(value =>
                string.Equals(value.Root, root, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                MessageBox.Show(root + " 当前不可用。", "选择磁盘", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            driveBox.SelectedItem = item;
        }

        private void SelectDriveFromList()
        {
            var item = driveBox.SelectedItem as DriveItem;
            if (item == null) return;
            pathBox.Text = item.Root;
            capacityLabel.Text = string.Format("总计 {0}  已用 {1}  可用 {2}", DisplayFormat.Bytes(item.TotalBytes),
                DisplayFormat.Bytes(item.TotalBytes - item.FreeBytes), DisplayFormat.Bytes(item.FreeBytes));
        }

        private void ConfigureFileGrid()
        {
            fileGrid.Columns.Add(Column("Name", "名称", 170, true));
            var childPercent = Column("ChildPercent", "子树百分比", 92, false);
            childPercent.ValueType = typeof(double);
            childPercent.DefaultCellStyle.Format = "0.0'%'";
            fileGrid.Columns.Add(childPercent);
            fileGrid.Columns.Add(Column("Percent", "百分比", 62, false));
            fileGrid.Columns.Add(Column("Physical", "物理大小", 82, false));
            fileGrid.Columns.Add(Column("Logical", "逻辑大小", 82, false));
            fileGrid.Columns.Add(Column("Files", "文件", 60, false));
            fileGrid.Columns.Add(Column("Modified", "上次更改", 110, false));
        }

        private void ConfigureExtensionGrid()
        {
            extensionGrid.Columns.Add(Column("Extension", "扩展名", 68, false));
            extensionGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Color",
                HeaderText = "颜色",
                Width = 48
            });
            extensionGrid.Columns.Add(Column("Bytes", "字节", 78, false));
            extensionGrid.Columns.Add(Column("Percent", "% 字节", 62, false));
            extensionGrid.Columns.Add(Column("Files", "文件", 58, false));
            extensionGrid.CellPainting += ExtensionGrid_CellPainting;
        }

        private static DataGridViewTextBoxColumn Column(string name, string title, int width, bool fill)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = title,
                Width = width,
                AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                MinimumWidth = fill ? width : 20
            };
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择要分析的目录",
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

            string path = pathBox.Text.Trim();
            if (!Directory.Exists(path))
            {
                MessageBox.Show("请选择存在的目录。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            cancellation = new CancellationTokenSource();
            SetBusy(true);
            status.Text = "正在扫描：" + path;
            busyOverlay.Start("正在分析磁盘文件", DisplayFormat.SingleLine(path, 90));
            try
            {
                analysis = await service.ScanAsync(
                    path,
                    new Progress<string>(value =>
                    {
                        status.Text = DisplayFormat.SingleLine(value, 190);
                        busyOverlay.UpdateMessage(DisplayFormat.SingleLine(value, 90));
                    }),
                    cancellation.Token);
                currentNode = analysis.Root;
                selectedExtension = null;
                ShowNode(currentNode);
                PopulateExtensions();
                status.Text = string.Format(
                    "扫描完成：{0:N0} 个文件，物理占用 {1}，逻辑大小 {2}，跳过 {3:N0} 个无权限路径{4}。",
                    analysis.Root.FileCount,
                    DisplayFormat.Bytes(analysis.Root.PhysicalSize),
                    DisplayFormat.Bytes(analysis.Root.Size),
                    analysis.SkippedPaths,
                    analysis.AggregatedFiles > 0 || analysis.AggregatedDirectories > 0
                        ? "，其中 " + analysis.AggregatedFiles.ToString("N0") + " 个文件、" +
                          analysis.AggregatedDirectories.ToString("N0") + " 个目录已合并显示以控制内存"
                        : string.Empty);
                OperationLogger.Info("文件扫描", path + "，物理占用 " + DisplayFormat.Bytes(analysis.Root.PhysicalSize) +
                    "，逻辑大小 " + DisplayFormat.Bytes(analysis.Root.Size));
            }
            catch (OperationCanceledException)
            {
                status.Text = "扫描已取消。";
            }
            catch (Exception ex)
            {
                status.Text = "扫描失败：" + ex.Message;
                OperationLogger.Error("文件扫描", ex.Message);
                MessageBox.Show(ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                busyOverlay.Stop();
                SetBusy(false);
            }
        }

        private void ShowNode(DiskNode node)
        {
            currentNode = node;
            pathBox.Text = node.FullPath;
            fileGrid.Rows.Clear();
            long total = Math.Max(1, node.Size);
            foreach (DiskNode child in node.Children)
            {
                double percent = child.Size * 100d / total;
                int index = fileGrid.Rows.Add(
                    (child.IsDirectory ? "[+] " : "") + child.Name,
                    percent,
                    percent.ToString("0.0") + "%",
                    DisplayFormat.Bytes(child.PhysicalSize),
                    DisplayFormat.Bytes(child.Size),
                    child.FileCount.ToString("N0"),
                    child.LastWriteTime == DateTime.MinValue ? "--" : child.LastWriteTime.ToString("yyyy/M/d HH:mm"));
                fileGrid.Rows[index].Tag = child;
            }
            treemap.Root = FilterNode(node, selectedExtension);
            upButton.Enabled = analysis != null && node != analysis.Root;
        }

        private void PopulateExtensions()
        {
            extensionGrid.Rows.Clear();
            long total = Math.Max(1, analysis.Root.Size);
            int allIndex = extensionGrid.Rows.Add("全部", string.Empty, DisplayFormat.Bytes(analysis.Root.Size), "100.0%", analysis.Root.FileCount.ToString("N0"));
            extensionGrid.Rows[allIndex].Tag = string.Empty;
            foreach (ExtensionUsage usage in analysis.Extensions)
            {
                int index = extensionGrid.Rows.Add(
                    usage.Extension,
                    string.Empty,
                    DisplayFormat.Bytes(usage.Bytes),
                    (usage.Bytes * 100d / total).ToString("0.0") + "%",
                    usage.FileCount.ToString("N0"));
                extensionGrid.Rows[index].Tag = usage.Extension;
            }
        }

        private async void ExtensionGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || currentNode == null) return;
            selectedExtension = Convert.ToString(extensionGrid.Rows[e.RowIndex].Tag);
            if (string.IsNullOrWhiteSpace(selectedExtension))
            {
                treemap.Root = currentNode;
                status.Text = "方格图已显示全部文件类型。";
                return;
            }

            if (cancellation != null) return;
            cancellation = new CancellationTokenSource();
            SetBusy(true);
            status.Text = "正在生成 " + selectedExtension + " 的精确方格图...";
            busyOverlay.Start("正在筛选文件类型", selectedExtension);
            try
            {
                DiskNode filtered = await service.ScanExtensionAsync(
                    currentNode.FullPath,
                    selectedExtension,
                    new Progress<string>(value => busyOverlay.UpdateMessage(DisplayFormat.SingleLine(value, 90))),
                    cancellation.Token);
                treemap.Root = filtered;
                status.Text = string.Format("{0}：{1:N0} 个文件，共 {2}。点击“全部”恢复完整方格图。",
                    selectedExtension, filtered.FileCount, DisplayFormat.Bytes(filtered.Size));
            }
            catch (OperationCanceledException)
            {
                status.Text = "文件类型筛选已取消。";
            }
            catch (Exception ex)
            {
                status.Text = "文件类型筛选失败：" + ex.Message;
                OperationLogger.Error("文件类型筛选", ex.Message);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                busyOverlay.Stop();
                if (!IsDisposed) SetBusy(false);
            }
        }

        private static DiskNode FilterNode(DiskNode node, string extension)
        {
            if (node == null || string.IsNullOrWhiteSpace(extension)) return node;
            if (!node.IsDirectory)
                return string.Equals(node.Extension, extension, StringComparison.OrdinalIgnoreCase) ? node : null;

            var filtered = new DiskNode
            {
                Name = node.Name,
                FullPath = node.FullPath,
                IsDirectory = true,
                LastWriteTime = node.LastWriteTime
            };
            foreach (DiskNode child in node.Children)
            {
                DiskNode match = FilterNode(child, extension);
                if (match == null || match.Size <= 0) continue;
                filtered.Children.Add(match);
                filtered.Size += match.Size;
                filtered.PhysicalSize += match.PhysicalSize;
                filtered.FileCount += match.FileCount;
            }
            return filtered;
        }

        private void FileGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var node = fileGrid.Rows[e.RowIndex].Tag as DiskNode;
            if (node != null && node.IsDirectory && !node.IsAggregate)
            {
                selectedExtension = null;
                ShowNode(node);
            }
        }

        private void Treemap_NodeSelected(object sender, DiskNodeEventArgs e)
        {
            if (e.Node == null || !e.Node.IsDirectory || e.Node.IsAggregate) return;
            DiskNode original = analysis == null ? null : FindNodeByPath(analysis.Root, e.Node.FullPath);
            selectedExtension = null;
            ShowNode(original ?? e.Node);
        }

        private static DiskNode FindNodeByPath(DiskNode root, string fullPath)
        {
            if (root == null) return null;
            if (string.Equals(root.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) return root;
            foreach (DiskNode child in root.Children)
            {
                if (!child.IsDirectory) continue;
                DiskNode match = FindNodeByPath(child, fullPath);
                if (match != null) return match;
            }
            return null;
        }

        private void NavigateToParent()
        {
            if (analysis == null || currentNode == null || currentNode == analysis.Root) return;
            DiskNode parent = FindParent(analysis.Root, currentNode);
            if (parent != null) ShowNode(parent);
        }

        private static DiskNode FindParent(DiskNode root, DiskNode target)
        {
            foreach (DiskNode child in root.Children)
            {
                if (ReferenceEquals(child, target)) return root;
                if (child.IsDirectory)
                {
                    DiskNode found = FindParent(child, target);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void ExtensionGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != extensionGrid.Columns["Color"].Index) return;
            e.PaintBackground(e.ClipBounds, true);
            string extension = Convert.ToString(extensionGrid.Rows[e.RowIndex].Tag);
            Rectangle swatch = new Rectangle(e.CellBounds.X + 12, e.CellBounds.Y + 7, e.CellBounds.Width - 24, e.CellBounds.Height - 14);
            using (var brush = new SolidBrush(TreemapControl.ColorForExtension(extension))) e.Graphics.FillRectangle(brush, swatch);
            e.Handled = true;
        }

        private void FileGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != fileGrid.Columns["ChildPercent"].Index) return;

            e.PaintBackground(e.ClipBounds, true);
            double percent;
            if (!double.TryParse(Convert.ToString(e.Value), out percent)) percent = 0d;
            percent = Math.Max(0d, Math.Min(100d, percent));

            var track = new Rectangle(
                e.CellBounds.X + 7,
                e.CellBounds.Y + Math.Max(4, (e.CellBounds.Height - 12) / 2),
                Math.Max(1, e.CellBounds.Width - 14),
                12);
            using (var background = new SolidBrush(Color.FromArgb(229, 234, 231)))
                e.Graphics.FillRectangle(background, track);
            int fillWidth = (int)Math.Round(track.Width * percent / 100d);
            if (fillWidth > 0)
            {
                var fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                using (var brush = new SolidBrush(AppPalette.Green)) e.Graphics.FillRectangle(brush, fill);
            }
            using (var border = new Pen(AppPalette.Border)) e.Graphics.DrawRectangle(border, track);
            e.Handled = true;
        }

        private void SetBusy(bool busy)
        {
            scanButton.Text = busy ? "取消扫描" : "开始扫描";
            browseButton.Enabled = !busy;
            driveBox.Enabled = !busy;
            driveCButton.Enabled = !busy;
            driveDButton.Enabled = !busy && driveBox.Items.Cast<DriveItem>().Any(value => string.Equals(value.Root, "D:\\", StringComparison.OrdinalIgnoreCase));
            refreshDrivesButton.Enabled = !busy;
            pathBox.Enabled = !busy;
            fileGrid.Enabled = !busy;
            extensionGrid.Enabled = !busy;
            upButton.Enabled = !busy && analysis != null && currentNode != analysis.Root;
            progressColumnStyle.Width = busy ? 220F : 0F;
            progress.Visible = busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) progress.Value = 0;
        }

        private static void SetSplitterRatio(SplitContainer splitter, double ratio)
        {
            int length = splitter.Orientation == Orientation.Vertical
                ? splitter.ClientSize.Width
                : splitter.ClientSize.Height;
            int available = length - splitter.SplitterWidth;
            int minimum = splitter.Panel1MinSize;
            int maximum = available - splitter.Panel2MinSize;
            if (available <= 0 || maximum < minimum) return;

            int distance = (int)Math.Round(available * ratio);
            splitter.SplitterDistance = Math.Max(minimum, Math.Min(maximum, distance));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cancellation != null) cancellation.Cancel();
            base.Dispose(disposing);
        }

        private sealed class DriveItem
        {
            public string Root { get; private set; }
            public long TotalBytes { get; private set; }
            public long FreeBytes { get; private set; }
            private readonly string label;

            public DriveItem(DriveInfo drive)
            {
                Root = drive.RootDirectory.FullName;
                TotalBytes = drive.TotalSize;
                FreeBytes = drive.AvailableFreeSpace;
                string kind = drive.DriveType == DriveType.Removable ? "移动磁盘" : "本地磁盘";
                label = Root + "  " + kind;
            }

            public override string ToString() { return label; }
        }
    }

    public sealed class DiskNodeEventArgs : EventArgs
    {
        public DiskNode Node { get; private set; }

        public DiskNodeEventArgs(DiskNode node)
        {
            Node = node;
        }
    }

    public sealed class TreemapControl : Control
    {
        private sealed class HitRegion
        {
            public RectangleF Bounds;
            public DiskNode Node;
            public int Depth;
        }

        private readonly List<HitRegion> hitRegions = new List<HitRegion>();
        private DiskNode root;

        public event EventHandler<DiskNodeEventArgs> NodeSelected;

        public DiskNode Root
        {
            get { return root; }
            set
            {
                root = value;
                Invalidate();
            }
        }

        public TreemapControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(30, 35, 33);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            hitRegions.Clear();
            if (root == null || root.Size <= 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(190, 205, 198)))
                    e.Graphics.DrawString("扫描完成后在这里显示文件方格图", UiFactory.BaseFont, brush, new PointF(18, 18));
                return;
            }

            RectangleF bounds = new RectangleF(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            DrawChildren(e.Graphics, root, bounds, 0, true);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            HitRegion selected = hitRegions
                .Where(region => region.Bounds.Contains(e.Location))
                .OrderByDescending(region => region.Depth)
                .ThenBy(region => region.Bounds.Width * region.Bounds.Height)
                .FirstOrDefault();
            if (selected == null) return;
            var handler = NodeSelected;
            if (handler != null) handler(this, new DiskNodeEventArgs(selected.Node));
        }

        private void DrawChildren(Graphics graphics, DiskNode parent, RectangleF bounds, int depth, bool horizontal)
        {
            if (parent.Children.Count == 0 || bounds.Width < 2 || bounds.Height < 2) return;
            long total = parent.Children.Sum(value => Math.Max(0, value.Size));
            if (total <= 0) return;

            float cursor = horizontal ? bounds.X : bounds.Y;
            float available = horizontal ? bounds.Width : bounds.Height;
            for (int index = 0; index < parent.Children.Count; index++)
            {
                DiskNode child = parent.Children[index];
                float share = index == parent.Children.Count - 1
                    ? (horizontal ? bounds.Right : bounds.Bottom) - cursor
                    : available * (float)(child.Size / (double)total);
                if (share < 0.35F) continue;

                RectangleF rectangle = horizontal
                    ? new RectangleF(cursor, bounds.Y, share, bounds.Height)
                    : new RectangleF(bounds.X, cursor, bounds.Width, share);
                cursor += share;

                DrawNode(graphics, child, rectangle, depth);
                hitRegions.Add(new HitRegion { Bounds = rectangle, Node = child, Depth = depth });

                RectangleF inner = RectangleF.Inflate(rectangle, -1.4F, -1.4F);
                if (child.IsDirectory && child.Children.Count > 0 && inner.Width >= 14 && inner.Height >= 12 && depth < 7)
                    DrawChildren(graphics, child, inner, depth + 1, !horizontal);
            }
        }

        private static void DrawNode(Graphics graphics, DiskNode node, RectangleF rectangle, int depth)
        {
            Color baseColor = ColorForExtension(node.IsDirectory && node.Children.Count > 0
                ? LargestExtension(node)
                : node.Extension);
            float factor = Math.Max(0.52F, 1F - depth * 0.055F);
            Color dark = Color.FromArgb(
                Math.Max(0, (int)(baseColor.R * factor)),
                Math.Max(0, (int)(baseColor.G * factor)),
                Math.Max(0, (int)(baseColor.B * factor)));
            Color light = Color.FromArgb(
                Math.Min(255, dark.R + 42),
                Math.Min(255, dark.G + 42),
                Math.Min(255, dark.B + 42));
            using (var brush = new LinearGradientBrush(rectangle, dark, light, LinearGradientMode.Vertical))
                graphics.FillRectangle(brush, rectangle);
            using (var pen = new Pen(Color.FromArgb(115, Color.Black), 1F))
                graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, Math.Max(0, rectangle.Width - 1), Math.Max(0, rectangle.Height - 1));

            if (rectangle.Width > 80 && rectangle.Height > 28)
            {
                string text = node.Name + "  " + DisplayFormat.Bytes(node.Size);
                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular))
                    graphics.DrawString(text, font, brush, RectangleF.Inflate(rectangle, -4, -3));
            }
        }

        private static string LargestExtension(DiskNode node)
        {
            DiskNode largest = node.Children.FirstOrDefault(value => !value.IsDirectory) ?? node.Children.FirstOrDefault();
            if (largest == null) return "文件夹";
            return largest.IsDirectory ? LargestExtension(largest) : largest.Extension;
        }

        public static Color ColorForExtension(string extension)
        {
            Color[] palette =
            {
                Color.FromArgb(69, 64, 171), Color.FromArgb(170, 61, 58), Color.FromArgb(49, 151, 58),
                Color.FromArgb(15, 139, 143), Color.FromArgb(146, 23, 158), Color.FromArgb(196, 151, 0),
                Color.FromArgb(30, 91, 140), Color.FromArgb(104, 169, 36), Color.FromArgb(203, 34, 125),
                Color.FromArgb(74, 82, 86), Color.FromArgb(19, 157, 112), Color.FromArgb(181, 94, 16)
            };
            int hash = 17;
            foreach (char character in extension ?? string.Empty) hash = unchecked(hash * 31 + character);
            return palette[(hash & int.MaxValue) % palette.Length];
        }
    }
}
