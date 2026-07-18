using System.Drawing;
using System.Windows.Forms;

namespace ZyperWin__
{
    public sealed class SystemOptimizationDashboard : UserControl
    {
        public SystemOptimizationDashboard()
        {
            Dock = DockStyle.Fill;
            BackColor = AppPalette.Canvas;
            Padding = new Padding(14);

            FlowLayoutPanel actions;
            Panel header = UiFactory.Header(
                "系统智能优化",
                "直接使用 ZyperWin++ 优化规则，并按实际驱动能力提供显卡专属入口。",
                out actions);

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = UiFactory.BaseFont,
                Padding = new Point(14, 7)
            };
            var zyperPage = new TabPage("ZyperWin++ 系统优化") { BackColor = AppPalette.Canvas, Padding = new Padding(6) };
            var optimize = new Optimize { Dock = DockStyle.Fill };
            UiFactory.ApplyTheme(optimize);
            zyperPage.Controls.Add(optimize);

            var gpuPage = new TabPage("显卡专属") { BackColor = AppPalette.Canvas, Padding = new Padding(0) };
            gpuPage.Controls.Add(new GpuDashboard { Dock = DockStyle.Fill });

            var advancedPage = new TabPage("高级系统管控") { BackColor = AppPalette.Canvas, Padding = new Padding(0) };
            advancedPage.Controls.Add(new AdvancedControlDashboard { Dock = DockStyle.Fill });

            tabs.TabPages.Add(zyperPage);
            tabs.TabPages.Add(gpuPage);
            tabs.TabPages.Add(advancedPage);
            Controls.Add(tabs);
            Controls.Add(header);
        }
    }
}
