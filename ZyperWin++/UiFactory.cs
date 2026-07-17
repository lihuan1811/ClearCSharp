using System;
using System.Drawing;
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
            return grid;
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
