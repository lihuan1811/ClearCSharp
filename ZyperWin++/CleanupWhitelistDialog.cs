using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class CleanupWhitelistDialog : Form
    {
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly Label status = UiFactory.StatusLabel("白名单路径及其下级内容不会参与扫描或清理。");

        public CleanupWhitelistDialog()
        {
            Text = "管理清理白名单";
            Font = UiFactory.BaseFont;
            BackColor = AppPalette.Canvas;
            ClientSize = new Size(760, 440);
            MinimumSize = new Size(760, 440);
            MaximumSize = new Size(760, 440);
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(14);

            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 42, ReadOnly = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "白名单路径", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 500, ReadOnly = true });

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 56, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(status, 0, 0);
            Button remove = UiFactory.SecondaryButton("移除选中");
            remove.Width = 100;
            bottom.Controls.Add(remove, 1, 0);

            Controls.Add(grid);
            Controls.Add(bottom);
            remove.Click += delegate { RemoveSelected(); };
            Load += delegate { RefreshEntries(); };
        }

        private void RefreshEntries()
        {
            grid.Rows.Clear();
            foreach (string path in CleanupWhitelist.ReadAll()) grid.Rows.Add(false, path);
            status.Text = grid.Rows.Count == 0 ? "当前没有清理白名单。" : "共 " + grid.Rows.Count + " 条白名单路径。";
        }

        private void RemoveSelected()
        {
            var selected = new System.Collections.Generic.List<string>();
            foreach (DataGridViewRow row in grid.Rows)
                if (Convert.ToBoolean(row.Cells["Selected"].Value ?? false)) selected.Add(Convert.ToString(row.Cells["Path"].Value));
            if (selected.Count == 0) return;
            if (MessageBox.Show("将移除 " + selected.Count + " 条白名单路径，后续扫描会重新包含这些内容。是否继续？",
                "确认移除白名单", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            CleanupWhitelist.Remove(selected);
            RefreshEntries();
        }
    }
}
