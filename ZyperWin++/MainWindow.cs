using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class MainWindow : AntdUI.Window
    {
        public static readonly string[] FinalModules =
        {
            "C盘深度清理",
            "软件强力卸载",
            "系统智能优化",
            "磁盘文件管理器",
            "CMD 系统修复"
        };

        private readonly AntdUI.PageHeader titleBar = new AntdUI.PageHeader();
        private readonly Panel navigation = new Panel();
        private readonly FlowLayoutPanel navigationButtons = new FlowLayoutPanel();
        private readonly Panel content = new Panel();
        private readonly Panel bottomBar = new Panel();
        private readonly FlowLayoutPanel bottomActions = new FlowLayoutPanel();
        private readonly Label shellStatus = new Label();
        private readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        private string activeModule;

        public MainWindow()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = UiFactory.BaseFont;
            Text = "C DiskGlow";
            ClientSize = new Size(1120, 700);
            MinimumSize = new Size(1120, 700);
            MaximumSize = new Size(1120, 700);
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = AppPalette.Canvas;

            BuildTitleBar();
            BuildNavigation();
            BuildStatusBar();

            content.Dock = DockStyle.Fill;
            content.BackColor = AppPalette.Canvas;

            Controls.Add(content);
            Controls.Add(bottomBar);
            Controls.Add(navigation);
            Controls.Add(titleBar);

            AppOperationCoordinator.Changed += OnOperationChanged;
            FormClosing += MainWindowFormClosing;
            FormClosed += delegate { AppOperationCoordinator.Changed -= OnOperationChanged; };
            Shown += delegate { Navigate(FinalModules[0]); };
        }

        internal Control ContentHostForTests { get { return content; } }
        internal Control NavigationHostForTests { get { return navigation; } }
        internal Control TitleBarHostForTests { get { return titleBar; } }
        internal Control BottomBarHostForTests { get { return bottomBar; } }
        internal void NavigateForTests(string module) { Navigate(module); }

        public void SetMenuEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetMenuEnabled), enabled);
                return;
            }
            bool allow = enabled && !AppOperationCoordinator.IsBusy;
            navigationButtons.Enabled = allow;
            bottomActions.Enabled = allow;
            shellStatus.Text = enabled ? "管理员权限已启用" : "正在执行系统操作，请稍候...";
        }

        private void OnOperationChanged(bool busy, string description)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool, string>(OnOperationChanged), busy, description);
                return;
            }
            navigationButtons.Enabled = !busy;
            bottomActions.Enabled = !busy;
            shellStatus.Text = busy ? "正在执行：" + description + "，请稍候..." : "管理员权限已启用";
        }

        private void MainWindowFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!AppOperationCoordinator.IsBusy) return;
            e.Cancel = true;
            MessageBox.Show("正在执行“" + AppOperationCoordinator.ActiveDescription + "”，完成后才能关闭程序。",
                "系统操作进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void ReportStatus(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ReportStatus), message);
                return;
            }
            shellStatus.Text = message;
        }

        private void BuildTitleBar()
        {
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 34;
            titleBar.Text = "C DiskGlow  ·  Windows 清理与维护";
            titleBar.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            titleBar.ShowButton = true;
            titleBar.ShowIcon = false;
            titleBar.MaximizeBox = false;
            titleBar.MinimizeBox = true;
            titleBar.DragMove = true;
            titleBar.BackColor = Color.White;
            titleBar.ForeColor = AppPalette.Text;
            titleBar.DividerShow = true;
            titleBar.DividerColor = AppPalette.Border;
        }

        private void BuildNavigation()
        {
            navigation.Dock = DockStyle.Top;
            navigation.Height = 58;
            navigation.BackColor = AppPalette.Green;

            var brand = new Label
            {
                Text = "C DiskGlow",
                Dock = DockStyle.Left,
                Width = 154,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            navigationButtons.Dock = DockStyle.Fill;
            navigationButtons.BackColor = AppPalette.Green;
            navigationButtons.FlowDirection = FlowDirection.LeftToRight;
            navigationButtons.WrapContents = false;
            navigationButtons.Padding = new Padding(4, 8, 0, 6);

            foreach (string module in FinalModules) AddNavigationButton(module);

            navigation.Controls.Add(navigationButtons);
            navigation.Controls.Add(brand);
        }

        private void BuildStatusBar()
        {
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 34;
            bottomBar.BackColor = Color.White;
            bottomBar.Padding = new Padding(8, 3, 8, 3);
            shellStatus.Dock = DockStyle.Fill;
            shellStatus.Height = 26;
            shellStatus.BackColor = Color.White;
            shellStatus.ForeColor = AppPalette.Muted;
            shellStatus.TextAlign = ContentAlignment.MiddleLeft;
            shellStatus.Padding = new Padding(14, 0, 0, 0);
            shellStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            shellStatus.Text = "管理员权限已启用";
            bottomActions.Dock = DockStyle.Right;
            bottomActions.AutoSize = true;
            bottomActions.FlowDirection = FlowDirection.LeftToRight;
            bottomActions.WrapContents = false;
            bottomActions.Margin = new Padding(0);
            Button logs = UiFactory.SecondaryButton("操作日志");
            Button restore = UiFactory.SecondaryButton("全局一键还原");
            logs.Height = 27;
            logs.Width = 86;
            restore.Height = 27;
            restore.Width = 112;
            logs.Click += delegate { using (var dialog = new Form { Text = "全部操作日志", ClientSize = new Size(900, 560), StartPosition = FormStartPosition.CenterParent }) { dialog.Controls.Add(new LogDashboard { Dock = DockStyle.Fill }); dialog.ShowDialog(this); } };
            restore.Click += delegate { using (var dialog = new RestoreDialog()) dialog.ShowDialog(this); };
            bottomActions.Controls.Add(logs);
            bottomActions.Controls.Add(restore);
            bottomBar.Controls.Add(shellStatus);
            bottomBar.Controls.Add(bottomActions);
        }

        private void AddNavigationButton(string module)
        {
            var button = new Button
            {
                Text = module,
                Width = 174,
                Height = 42,
                Margin = new Padding(0, 0, 2, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = AppPalette.Green,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AppPalette.GreenHover;
            button.FlatAppearance.MouseDownBackColor = AppPalette.GreenActive;
            button.Click += delegate { Navigate(module); };
            buttons[module] = button;
            navigationButtons.Controls.Add(button);
        }

        private void Navigate(string module)
        {
            if (AppOperationCoordinator.IsBusy)
            {
                MessageBox.Show("正在执行“" + AppOperationCoordinator.ActiveDescription + "”，完成后才能切换模块。",
                    "系统操作进行中", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (module == activeModule) return;
            activeModule = module;
            foreach (KeyValuePair<string, Button> pair in buttons)
            {
                pair.Value.BackColor = pair.Key == module ? AppPalette.GreenActive : AppPalette.Green;
                pair.Value.Font = new Font(
                    "Microsoft YaHei UI",
                    9.5F,
                    pair.Key == module ? FontStyle.Bold : FontStyle.Regular);
            }

            shellStatus.Text = "正在打开：" + module;
            UseWaitCursor = true;
            try
            {
                Control control;
                switch (module)
                {
                    case "系统智能优化":
                        control = new SystemOptimizationDashboard();
                        break;
                    case "软件强力卸载":
                        control = new UninstallDashboard();
                        break;
                    case "磁盘文件管理器":
                        control = new FileManagerDashboard();
                        break;
                    case "CMD 系统修复":
                        control = new RepairDashboard();
                        break;
                    default:
                        control = new CleanupDashboard();
                        break;
                }
                control.Dock = DockStyle.Fill;
                content.SuspendLayout();
                foreach (Control old in content.Controls) old.Dispose();
                content.Controls.Clear();
                content.Controls.Add(control);
                content.ResumeLayout(true);
                shellStatus.Text = "当前模块：" + module;
            }
            catch (Exception ex)
            {
                OperationLogger.Error("界面", "打开 " + module + " 失败：" + ex.Message);
                MessageBox.Show(ex.Message, "模块加载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                activeModule = null;
            }
            finally
            {
                UseWaitCursor = false;
            }
        }
    }
}
