using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ZyperWin__
{
    internal static class UiFactory
    {
        public static readonly Font BaseFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        public static readonly Font TitleFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
        public static readonly Font SectionFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);

        public static Button PrimaryButton(string text)
        {
            return Button(text, AppPalette.Green, Color.White, AppPalette.Green);
        }

        public static Button SecondaryButton(string text)
        {
            return Button(text, Color.White, AppPalette.Green, AppPalette.Border);
        }

        public static Button Button(string text, Color backColor, Color foreColor, Color borderColor)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = false,
                Height = 34,
                MinimumSize = new Size(88, 34),
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Font = BaseFont,
                Cursor = Cursors.Hand,
                Padding = new Padding(10, 0, 10, 0),
                Margin = new Padding(4)
            };
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = backColor == Color.White ? AppPalette.PaleGreen : AppPalette.GreenHover;
            button.FlatAppearance.MouseDownBackColor = backColor == Color.White ? Color.FromArgb(220, 241, 231) : AppPalette.GreenActive;
            return button;
        }

        public static Label Title(string text, string subtitle)
        {
            return new Label
            {
                Text = text + Environment.NewLine + subtitle,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = TitleFont,
                ForeColor = AppPalette.Text,
                Padding = new Padding(0, 4, 0, 0)
            };
        }

        public static Label StatusLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = BaseFont,
                ForeColor = AppPalette.Muted,
                AutoEllipsis = true
            };
        }

        public static DataGridView Grid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.FromArgb(232, 237, 234),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ReadOnly = true,
                EnableHeadersVisualStyles = false,
                Font = BaseFont,
                ColumnHeadersHeight = 32,
                RowTemplate = { Height = 29 }
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 245, 242);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = AppPalette.Text;
            grid.ColumnHeadersDefaultCellStyle.Font = SectionFont;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 245, 242);
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = AppPalette.Text;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 241, 230);
            grid.DefaultCellStyle.SelectionForeColor = AppPalette.Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 250);
            AttachStandardGridTools(grid);
            return grid;
        }

        private static void AttachStandardGridTools(DataGridView grid)
        {
            grid.CellToolTipTextNeeded += delegate(object sender, DataGridViewCellToolTipTextNeededEventArgs args)
            {
                if (args.RowIndex < 0 || args.ColumnIndex < 0) return;
                object value = grid.Rows[args.RowIndex].Cells[args.ColumnIndex].FormattedValue;
                string text = Convert.ToString(value);
                if (!string.IsNullOrWhiteSpace(text)) args.ToolTipText = grid.Columns[args.ColumnIndex].HeaderText + "：" + text;
            };
            grid.MouseDown += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Right) return;
                DataGridView.HitTestInfo hit = grid.HitTest(args.X, args.Y);
                if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
                    grid.CurrentCell = grid.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开文件位置", null, delegate
            {
                string path = CurrentPath(grid);
                if (string.IsNullOrWhiteSpace(path)) return;
                string target = File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + path + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            });
            menu.Items.Add("复制完整路径", null, delegate
            {
                string path = CurrentPath(grid);
                if (!string.IsNullOrWhiteSpace(path)) Clipboard.SetText(path);
            });
            menu.Items.Add("复制当前单元格", null, delegate
            {
                if (grid.CurrentCell != null) Clipboard.SetText(Convert.ToString(grid.CurrentCell.FormattedValue) ?? string.Empty);
            });
            menu.Items.Add("复制整行", null, delegate
            {
                if (grid.CurrentRow == null) return;
                Clipboard.SetText(string.Join("\t", grid.CurrentRow.Cells.Cast<DataGridViewCell>()
                    .Select(value => Convert.ToString(value.FormattedValue) ?? string.Empty)));
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("查看操作日志", null, delegate
            {
                string directory = Path.GetDirectoryName(OperationLogger.FilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                if (!File.Exists(OperationLogger.FilePath)) File.WriteAllText(OperationLogger.FilePath, string.Empty, System.Text.Encoding.UTF8);
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + OperationLogger.FilePath + "\"") { UseShellExecute = true });
            });
            menu.Opening += delegate
            {
                bool hasCell = grid.CurrentCell != null;
                string path = CurrentPath(grid);
                menu.Items[0].Enabled = !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));
                menu.Items[1].Enabled = !string.IsNullOrWhiteSpace(path);
                menu.Items[2].Enabled = hasCell;
                menu.Items[3].Enabled = grid.CurrentRow != null;
            };
            grid.ContextMenuStrip = menu;
        }

        private static string CurrentPath(DataGridView grid)
        {
            if (grid.CurrentRow == null) return null;
            string[] names = { "Path", "InstallLocation", "Source", "TargetPath", "FullPath" };
            foreach (string name in names)
            {
                if (!grid.Columns.Contains(name)) continue;
                string value = Convert.ToString(grid.CurrentRow.Cells[name].FormattedValue);
                if (string.IsNullOrWhiteSpace(value) || value.IndexOf('；') >= 0) continue;
                return Environment.ExpandEnvironmentVariables(value.Trim());
            }
            return null;
        }

        public static Panel Header(string title, string subtitle, out FlowLayoutPanel actions)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Color.White,
                Padding = new Padding(16, 10, 12, 8)
            };
            var titleLabel = Title(title, subtitle);
            titleLabel.Dock = DockStyle.Fill;
            actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 14, 0, 0)
            };
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(actions);
            return panel;
        }

        public static void ApplyTheme(Control root)
        {
            root.Font = BaseFont;
            root.BackColor = AppPalette.Canvas;
            foreach (Control child in root.Controls)
            {
                if (child is AntdUI.Button)
                {
                    var button = (AntdUI.Button)child;
                    button.BackColor = AppPalette.Green;
                    button.ForeColor = Color.White;
                    button.Radius = 4;
                }
                else if (child is Label)
                {
                    child.ForeColor = AppPalette.Text;
                }
                ApplyTheme(child);
            }
        }
    }
}
