using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class MainWindow : AntdUI.Window
    {
        private readonly AntdUI.PageHeader titleBar = new AntdUI.PageHeader();
        private readonly Panel navigation = new Panel();
        private readonly FlowLayoutPanel navigationButtons = new FlowLayoutPanel();
        private readonly Panel content = new Panel();
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

            Controls.Add(content);
            Controls.Add(shellStatus);
            Controls.Add(navigation);
            Controls.Add(titleBar);

            Shown += delegate { Navigate("C 盘清理"); };
        }

        public void SetMenuEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(SetMenuEnabled), enabled);
                return;
            }
            navigationButtons.Enabled = enabled;
            shellStatus.Text = enabled ? "管理员权限已启用" : "正在执行系统操作，请稍候...";
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

            AddNavigationButton("C 盘清理");
            AddNavigationButton("系统优化");
            AddNavigationButton("软件卸载");
            AddNavigationButton("文件管理");
            AddNavigationButton("显卡优化");
            AddNavigationButton("系统修复");
            AddNavigationButton("操作日志");

            navigation.Controls.Add(navigationButtons);
            navigation.Controls.Add(brand);
        }

        private void BuildStatusBar()
        {
            shellStatus.Dock = DockStyle.Bottom;
            shellStatus.Height = 26;
            shellStatus.BackColor = Color.White;
            shellStatus.ForeColor = AppPalette.Muted;
            shellStatus.TextAlign = ContentAlignment.MiddleLeft;
            shellStatus.Padding = new Padding(14, 0, 0, 0);
            shellStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            shellStatus.Text = "管理员权限已启用";
        }

        private void AddNavigationButton(string module)
        {
            var button = new Button
            {
                Text = module,
                Width = 122,
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
                    case "系统优化":
                        control = new Optimize();
                        break;
                    case "软件卸载":
                        control = new UninstallDashboard();
                        break;
                    case "文件管理":
                        control = new FileManagerDashboard();
                        break;
                    case "显卡优化":
                        control = new GpuDashboard();
                        break;
                    case "系统修复":
                        control = new RepairDashboard();
                        break;
                    case "操作日志":
                        control = new LogDashboard();
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
                if (control is Optimize) UiFactory.ApplyTheme(control);
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
