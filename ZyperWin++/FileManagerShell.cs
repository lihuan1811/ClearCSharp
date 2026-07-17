using System.Drawing;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class FileManagerDashboard : UserControl
    {
        public FileManagerDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = UiFactory.SectionFont,
                Padding = new Point(18, 6)
            };
            tabs.TabPages.Add(Page("磁盘可视化", new DiskVisualizationDashboard()));
            tabs.TabPages.Add(Page("文件筛选与批量操作", new FileOperationsDashboard()));
            tabs.TabPages.Add(Page("系统目录一键迁移专区", new MigrationDashboard()));
            Controls.Add(tabs);
        }

        private static TabPage Page(string title, Control content)
        {
            var page = new TabPage(title) { BackColor = AppPalette.Canvas, Padding = new Padding(0) };
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            return page;
        }
    }
}
