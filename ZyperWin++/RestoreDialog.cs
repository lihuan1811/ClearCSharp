using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class RestoreDialog : Form
    {
        private readonly DataGridView grid = UiFactory.Grid();
        private readonly Label status = UiFactory.StatusLabel("选择清理备份后还原到原位置。系统优化请在系统智能优化页面使用“还原”。");

        public RestoreDialog()
        {
            Text = "全局一键还原";
            Font = UiFactory.BaseFont;
            BackColor = AppPalette.Canvas;
            ClientSize = new Size(900, 520);
            MinimumSize = new Size(900, 520);
            MaximumSize = new Size(900, 520);
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(14);

            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", Width = 42 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Created", HeaderText = "备份时间", Width = 155 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "大小", Width = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Source",
                HeaderText = "原路径",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 380
            });
            grid.ReadOnly = false;
            foreach (DataGridViewColumn column in grid.Columns) column.ReadOnly = column.Name != "Selected";

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 56, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(status, 0, 0);
            var restore = UiFactory.PrimaryButton("还原选中");
            restore.Width = 108;
            bottom.Controls.Add(restore, 1, 0);

            Controls.Add(grid);
            Controls.Add(bottom);
            restore.Click += Restore_Click;
            Load += delegate { RefreshRecords(); };
        }

        private void RefreshRecords()
        {
            grid.Rows.Clear();
            foreach (BackupRecord record in BackupStore.ReadRecords())
            {
                int index = grid.Rows.Add(false, record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), DisplayFormat.Bytes(record.Bytes), record.SourcePath);
                grid.Rows[index].Tag = record;
            }
            status.Text = grid.Rows.Count == 0 ? "暂无可还原的清理备份。" : "共 " + grid.Rows.Count + " 条清理备份。";
        }

        private void Restore_Click(object sender, EventArgs e)
        {
            int restored = 0;
            int failed = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!Convert.ToBoolean(row.Cells["Selected"].Value ?? false)) continue;
                var record = row.Tag as BackupRecord;
                if (record == null) continue;
                string error;
                if (BackupStore.TryRestore(record, out error)) restored++;
                else failed++;
            }
            status.Text = string.Format("还原完成：成功 {0}，失败 {1}。", restored, failed);
            MessageBox.Show(status.Text, "全局一键还原", MessageBoxButtons.OK,
                failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
