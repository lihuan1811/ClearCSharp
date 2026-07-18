using AntdUI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace ZyperWin__
{
    public partial class Optimize : UserControl
    {
        private string xmlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bin", "ZyperData.xml");
        private Dictionary<string, bool> optimizationStatus = new Dictionary<string, bool>();
        private XDocument xmlDoc;
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        // 需要重启资源管理器的分类
        private readonly string[] explorerCategories = { "explorer", "外观/资源管理器" };

        public Optimize()
        {
            InitializeComponent();

            // Upstream advanced dialogs depend on release-only batch tools that are
            // not part of the cloned source. Keep unsupported actions out of the UI.
            button9.Visible = false;
            button10.Visible = false;

            // 加载XML文件
            try
            {
                if (File.Exists(xmlFilePath))
                {
                    xmlDoc = XDocument.Load(xmlFilePath);
                }
                else
                {
                    using (Stream stream = Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream("CDiskGlow.Embedded.ZyperData.xml"))
                    {
                        if (stream == null) throw new FileNotFoundException("系统优化规则未找到。", xmlFilePath);
                        xmlDoc = XDocument.Load(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载配置文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            InitializeTreeView();
            ExportSelectedToTree2();
            CheckAllOptimizationStatus();

            // 直接监听tree1的点击事件
            tree1.MouseClick += Tree1_MouseClick_Simple;
        }

        private void InitializeTreeView()
        {
            tree1.Items.Clear();

            // 添加分类节点
            var explorer = new TreeItem("外观/资源管理器") { Checked = true, Tag = "explorer" };
            var xingneng = new TreeItem("性能优化设置") { Checked = true, Tag = "xingneng" };
            var safe = new TreeItem("安全设置") { Checked = true, Tag = "safe" };
            var edge = new TreeItem("Edge优化设置") { Checked = true, Tag = "edge" };
            var system = new TreeItem("系统设置") { Checked = true, Tag = "system" };
            var update = new TreeItem("更新设置") { Checked = true, Tag = "update" };
            var yinsi = new TreeItem("隐私设置") { Checked = true, Tag = "yinsi" };

            AddTreeItem(explorer, "1、隐藏任务栏搜索框", "1、隐藏任务栏搜索框");
            AddTreeItem(explorer, "2、隐藏“任务视图”按钮", "2、隐藏“任务视图”按钮");
            AddTreeItem(explorer, "3、始终在任务栏显示所有图标和通知", "3、始终在任务栏显示所有图标和通知");
            AddTreeItem(explorer, "4、任务栏窗口被占满时合并", "4、任务栏窗口被占满时合并");
            AddTreeItem(explorer, "5、提高前台程序的显示速度", "5、提高前台程序的显示速度");
            AddTreeItem(explorer, "6、不要显示窗口出现和消失动画", "6、不要显示窗口出现和消失动画");
            AddTreeItem(explorer, "7、使「开始」菜单、任务栏、操作中心透明", "7、使「开始」菜单、任务栏、操作中心透明");
            AddTreeItem(explorer, "8、打开资源管理器时显示此电脑", "8、打开资源管理器时显示此电脑");
            AddTreeItem(explorer, "9、总是从内存中卸载无用的DLL", "9、总是从内存中卸载无用的DLL");
            AddTreeItem(explorer, "10、记事本启用自动换行", "10、记事本启用自动换行");
            AddTreeItem(explorer, "11、记事本始终显示状态栏", "11、记事本始终显示状态栏");
            AddTreeItem(explorer, "12、禁止跟踪损坏的快捷方式", "12、禁止跟踪损坏的快捷方式");
            AddTreeItem(explorer, "13、优化Windows文件列表刷新策略", "13、优化Windows文件列表刷新策略");
            AddTreeItem(explorer, "14、显示已知文件类型的扩展名", "14、显示已知文件类型的扩展名");
            AddTreeItem(explorer, "15、不要保留“最近打开的文件”历史记录", "15、不要保留“最近打开的文件”历史记录");
            AddTreeItem(explorer, "16、退出时清除“最近打开的文件”历史记录", "16、退出时清除“最近打开的文件”历史记录");
            AddTreeItem(explorer, "17、创建快捷方式时不添加“快捷方式”字样", "17、创建快捷方式时不添加“快捷方式”字样");
            AddTreeItem(explorer, "18、禁止自动播放", "18、禁止自动播放");
            AddTreeItem(explorer, "19、在单独的进程中打开文件夹窗口", "19、在单独的进程中打开文件夹窗口");
            AddTreeItem(explorer, "20、标题栏显示完整路径", "20、标题栏显示完整路径");
            AddTreeItem(explorer, "21、快速访问不显示常用文件夹", "21、快速访问不显示常用文件夹");
            AddTreeItem(explorer, "22、快速访问不显示最近使用的文件", "22、快速访问不显示最近使用的文件");
            AddTreeItem(explorer, "23、资源管理器崩溃时自动重启", "23、资源管理器崩溃时自动重启");
            AddTreeItem(explorer, "24、桌面显示此电脑", "24、桌面显示此电脑");
            AddTreeItem(explorer, "25、桌面显示回收站", "25、桌面显示回收站");
            AddTreeItem(explorer, "26、隐藏桌面上的“了解此图片”图标", "26、隐藏桌面上的“了解此图片”图标");
            AddTreeItem(explorer, "27、微软拼音默认为英文输入", "27、微软拼音默认为英文输入");
            AddTreeItem(explorer, "28、关闭微软拼音云计算", "28、关闭微软拼音云计算");
            AddTreeItem(explorer, "29、去除本地磁盘重复显示", "29、去除本地磁盘重复显示");
            AddTreeItem(xingneng, "1、优化进程数量", "1、优化进程数量");
            AddTreeItem(xingneng, "2、不允许在「开始」菜单显示建议", "2、不允许在「开始」菜单显示建议");
            AddTreeItem(xingneng, "3、不要在应用商店中查找关联应用", "3、不要在应用商店中查找关联应用");
            AddTreeItem(xingneng, "4、关闭商店应用推广", "4、关闭商店应用推广");
            AddTreeItem(xingneng, "5、禁止应用商店自动下载和安装更新", "5、禁止应用商店自动下载和安装更新");
            AddTreeItem(xingneng, "6、关闭锁屏时的Windows聚焦推广", "6、关闭锁屏时的Windows聚焦推广");
            AddTreeItem(xingneng, "7、关闭“使用Windows时获取技巧和建议”", "7、关闭“使用Windows时获取技巧和建议”");
            AddTreeItem(xingneng, "8、禁止自动安装推荐的应用程序", "8、禁止自动安装推荐的应用程序");
            AddTreeItem(xingneng, "9、关闭游戏录制工具", "9、关闭游戏录制工具");
            AddTreeItem(xingneng, "10、关闭多嘴的小娜", "10、关闭多嘴的小娜");
            AddTreeItem(xingneng, "11、“运行”对话框不要显示历史记录", "11、“运行”对话框不要显示历史记录");
            AddTreeItem(xingneng, "12、隐藏「开始」菜单中的“推荐”", "12、隐藏「开始」菜单中的“推荐”");
            AddTreeItem(xingneng, "13、隐藏「开始」菜单历史记录中推荐的网站", "13、隐藏「开始」菜单历史记录中推荐的网站");
            AddTreeItem(xingneng, "14、加快关机速度", "14、加快关机速度");
            AddTreeItem(xingneng, "15、缩短关闭服务等待时间", "15、缩短关闭服务等待时间");
            AddTreeItem(xingneng, "16、关闭远程协助", "16、关闭远程协助");
            AddTreeItem(xingneng, "17、禁用远程修改注册表", "17、禁用远程修改注册表");
            AddTreeItem(xingneng, "18、禁用诊断服务", "18、禁用诊断服务");
            AddTreeItem(xingneng, "19、禁用SysMain", "19、禁用SysMain");
            AddTreeItem(xingneng, "20、禁用Windows Search", "20、禁用Windows Search");
            AddTreeItem(xingneng, "21、禁用错误报告", "21、禁用错误报告");
            AddTreeItem(xingneng, "22、禁用家庭组", "22、禁用家庭组");
            AddTreeItem(xingneng, "23、禁用客户体验改善计划", "23、禁用客户体验改善计划");
            AddTreeItem(xingneng, "24、禁用NTFS链接跟踪服务", "24、禁用NTFS链接跟踪服务");
            AddTreeItem(xingneng, "25、禁止自动维护计划", "25、禁止自动维护计划");
            AddTreeItem(xingneng, "26、启用大系统缓存以提高性能", "26、启用大系统缓存以提高性能");
            AddTreeItem(xingneng, "27、禁止系统内核与驱动程序分页到硬盘", "27、禁止系统内核与驱动程序分页到硬盘");
            AddTreeItem(xingneng, "28、增加文件管理系统缓存以提高性能", "28、增加文件管理系统缓存以提高性能");
            AddTreeItem(xingneng, "29、启用高性能电源计划", "29、启用高性能电源计划");
            AddTreeItem(xingneng, "30、禁用处理器的幽灵和熔断补丁以提高性能", "30、禁用处理器的幽灵和熔断补丁以提高性能");
            AddTreeItem(xingneng, "31、禁用保留的存储", "31、禁用保留的存储");
            AddTreeItem(xingneng, "32、优化处理器性能", "32、优化处理器性能");
            AddTreeItem(xingneng, "33、加快预读能力改善速度", "33、加快预读能力改善速度");
            AddTreeItem(xingneng, "34、禁止系统自动生成错误报告", "34、禁止系统自动生成错误报告");
            AddTreeItem(xingneng, "35、禁用高精度事件定时器（HPET）", "35、禁用高精度事件定时器（HPET）");
            AddTreeItem(xingneng, "36、关闭系统自动调试功能，提高系统运行速度", "36、关闭系统自动调试功能，提高系统运行速度");
            AddTreeItem(xingneng, "37、关闭程序兼容性助手", "37、关闭程序兼容性助手");
            AddTreeItem(xingneng, "38、启用自动完成设备设置", "38、启用自动完成设备设置");
            AddTreeItem(xingneng, "39、关闭Exploit Protection（乱序内存）", "39、关闭Exploit Protection（乱序内存）");
            AddTreeItem(xingneng, "40、优化Windows Search和小娜的设置", "40、优化Windows Search和小娜的设置");
            AddTreeItem(xingneng, "41、关闭广告 ID", "41、关闭广告 ID");
            AddTreeItem(xingneng, "42、禁用磁盘空间不足警告", "42、禁用磁盘空间不足警告");
            AddTreeItem(xingneng, "43、去除搜索页面信息流和热搜", "43、去除搜索页面信息流和热搜");
            AddTreeItem(xingneng, "44、关闭TSX漏洞补丁", "44、关闭TSX漏洞补丁");
            AddTreeItem(xingneng, "45、开启GPU硬件加速", "45、开启GPU硬件加速");
            AddTreeItem(safe, "1、将用户账号控制（UAC）调整为从不通知", "1、将用户账号控制（UAC）调整为从不通知");
            AddTreeItem(safe, "2、用于内置管理员账户的管理审批模式", "2、用于内置管理员账户的管理审批模式");
            AddTreeItem(safe, "3、以管理审批模式运行所有管理员", "3、以管理审批模式运行所有管理员");
            AddTreeItem(safe, "4、仅提升安全路径下的UIAccess程序", "4、仅提升安全路径下的UIAccess程序");
            AddTreeItem(safe, "5、允许UIAccess程序在非安全桌面上提升", "5、允许UIAccess程序在非安全桌面上提升");
            AddTreeItem(safe, "6、关闭SmartScreen应用筛选器", "6、关闭SmartScreen应用筛选器");
            AddTreeItem(safe, "7、关闭打开程序的安全警告", "7、关闭打开程序的安全警告");
            AddTreeItem(safe, "8、关闭防火墙", "8、关闭防火墙");
            AddTreeItem(safe, "9、关闭内存完整", "9、关闭内存完整");
            AddTreeItem(safe, "10、关闭虚拟化安全性", "10、关闭虚拟化安全性");
            AddTreeItem(edge, "1、不要显示“首次运行”欢迎页面", "1、不要显示“首次运行”欢迎页面");
            AddTreeItem(edge, "2、Edge浏览器关闭后禁止继续运行后台应用", "2、Edge浏览器关闭后禁止继续运行后台应用");
            AddTreeItem(edge, "3、禁用启动增强", "3、禁用启动增强");
            AddTreeItem(edge, "4、阻止必应搜索结果中的所有广告", "4、阻止必应搜索结果中的所有广告");
            AddTreeItem(edge, "5、从新标签页中隐藏默认的热门站点", "5、从新标签页中隐藏默认的热门站点");
            AddTreeItem(edge, "6、隐藏Edge浏览器边栏", "6、隐藏Edge浏览器边栏");
            AddTreeItem(edge, "7、关闭Edge浏览器停止支持旧系统的通知", "7、关闭Edge浏览器停止支持旧系统的通知");
            AddTreeItem(edge, "8、不要发送任何诊断数据", "8、不要发送任何诊断数据");
            AddTreeItem(edge, "9、禁用标签页性能检测器", "9、禁用标签页性能检测器");
            AddTreeItem(edge, "10、禁用新选项卡页面上的微软资讯内容", "10、禁用新选项卡页面上的微软资讯内容");
            AddTreeItem(edge, "11、禁用个性化广告和体验", "11、禁用个性化广告和体验");
            AddTreeItem(edge, "12、禁用不安全的下载警告", "12、禁用不安全的下载警告");
            AddTreeItem(system, "1、关闭休眠", "1、关闭休眠");
            AddTreeItem(system, "2、弹出USB磁盘后彻底断开其电源", "2、弹出USB磁盘后彻底断开其电源");
            AddTreeItem(system, "3、不要将VHD动态文件扩展到最大以节省磁盘空间", "3、不要将VHD动态文件扩展到最大以节省磁盘空间");
            AddTreeItem(system, "4、蓝屏时自动重启", "4、蓝屏时自动重启");
            AddTreeItem(system, "5、关闭系统自动调试功能", "5、关闭系统自动调试功能");
            AddTreeItem(system, "6、将磁盘错误检查的等待时间缩短到五秒", "6、将磁盘错误检查的等待时间缩短到五秒");
            AddTreeItem(system, "7、设备安装禁止创建系统还原点", "7、设备安装禁止创建系统还原点");
            AddTreeItem(system, "8、MSI类软件安装禁止创建系统还原点", "8、MSI类软件安装禁止创建系统还原点");
            AddTreeItem(system, "9、关闭系统还原", "9、关闭系统还原");
            AddTreeItem(system, "10、根据语言设置隐藏字体", "10、根据语言设置隐藏字体");
            AddTreeItem(system, "11、允许字体作为快捷方式安装", "11、允许字体作为快捷方式安装");
            AddTreeItem(system, "12、崩溃时不写入调试信息", "12、崩溃时不写入调试信息");
            AddTreeItem(system, "13、禁用账户登录日志报告", "13、禁用账户登录日志报告");
            AddTreeItem(system, "14、禁用WfpDiag.ETL日志", "14、禁用WfpDiag.ETL日志");
            AddTreeItem(update, "1、自动安装无需重启的更新", "1、自动安装无需重启的更新");
            AddTreeItem(update, "2、更新挂起时若有用户登录则不自动重启计算机", "2、更新挂起时若有用户登录则不自动重启计算机");
            AddTreeItem(update, "3、Windows更新不包括驱动程序", "3、Windows更新不包括驱动程序");
            AddTreeItem(update, "4、禁止Win10/11进行大版本更新", "4、禁止Win10/11进行大版本更新");
            AddTreeItem(update, "5、Windows更新不包括恶意软件删除工具", "5、Windows更新不包括恶意软件删除工具");
            AddTreeItem(update, "6、从不检查系统更新", "6、从不检查系统更新");
            AddTreeItem(update, "7、不要显示“新版本记事本已可用”提示", "7、不要显示“新版本记事本已可用”提示");
            AddTreeItem(yinsi, "1、禁用页面预测功能", "1、禁用页面预测功能");
            AddTreeItem(yinsi, "2、禁用SMS路由器服务", "2、禁用SMS路由器服务");
            AddTreeItem(yinsi, "3、禁用活动收集", "3、禁用活动收集");
            AddTreeItem(yinsi, "4、禁用应用启动跟踪", "4、禁用应用启动跟踪");
            AddTreeItem(yinsi, "5、禁用广告标识符", "5、禁用广告标识符");
            AddTreeItem(yinsi, "6、禁用应用访问文件系统", "6、禁用应用访问文件系统");
            AddTreeItem(yinsi, "7、禁用应用访问文档", "7、禁用应用访问文档");
            AddTreeItem(yinsi, "8、禁用应用访问日历", "8、禁用应用访问日历");
            AddTreeItem(yinsi, "9、禁用应用访问联系人", "9、禁用应用访问联系人");
            AddTreeItem(yinsi, "10、禁用网站语言跟踪", "10、禁用网站语言跟踪");
            AddTreeItem(yinsi, "11、禁用Windows欢迎体验", "11、禁用Windows欢迎体验");
            AddTreeItem(yinsi, "12、禁用反馈频率", "12、禁用反馈频率");
            AddTreeItem(yinsi, "13、禁用诊断数据收集", "13、禁用诊断数据收集");
            AddTreeItem(yinsi, "14、禁用写作习惯跟踪", "14、禁用写作习惯跟踪");
            AddTreeItem(yinsi, "15、禁用设置应用建议", "15、禁用设置应用建议");
            AddTreeItem(yinsi, "16、禁用Bing搜索结果", "16、禁用Bing搜索结果");
            AddTreeItem(yinsi, "17、禁用通讯录收集", "17、禁用通讯录收集");
            AddTreeItem(yinsi, "18、禁用键入文本收集", "18、禁用键入文本收集");
            AddTreeItem(yinsi, "19、禁用搜索历史", "19、禁用搜索历史");
            AddTreeItem(yinsi, "20、禁用赞助商应用安装", "20、禁用赞助商应用安装");
            AddTreeItem(yinsi, "21、禁用自动连接热点", "21、禁用自动连接热点");
            AddTreeItem(yinsi, "22、禁用输入数据个性化", "22、禁用输入数据个性化");
            AddTreeItem(yinsi, "23、禁用键入见解", "23、禁用键入见解");
            AddTreeItem(yinsi, "24、禁用预安装应用", "24、禁用预安装应用");
            AddTreeItem(yinsi, "25、禁用.NET遥测", "25、禁用.NET遥测");
            AddTreeItem(yinsi, "26、禁用PowerShell遥测", "26、禁用PowerShell遥测");
            AddTreeItem(yinsi, "27、禁用遥测服务", "27、禁用遥测服务");
            AddTreeItem(yinsi, "28、禁用语音激活(Cortana)", "28、禁用语音激活(Cortana)");
            AddTreeItem(yinsi, "29、禁用位置服务", "29、禁用位置服务");
            AddTreeItem(yinsi, "30、启用剪切板历史记录", "30、启用剪切板历史记录");
            AddTreeItem(yinsi, "31、禁用定向广告", "31、禁用定向广告");
            AddTreeItem(yinsi, "32、禁用Wi-Fi感知", "32、禁用Wi-Fi感知");
            AddTreeItem(yinsi, "33、禁用步骤记录器", "33、禁用步骤记录器");
            AddTreeItem(yinsi, "34、禁用写入调试信息", "34、禁用写入调试信息");

            tree1.Items.Add(explorer);
            tree1.Items.Add(xingneng);
            tree1.Items.Add(safe);
            tree1.Items.Add(edge);
            tree1.Items.Add(system);
            tree1.Items.Add(update);
            tree1.Items.Add(yinsi);

            tree1.ExpandAll();
        }

        private void AddTreeItem(TreeItem parent, string text, string commandKey)
        {
            var item = new TreeItem(text) { Checked = false, Tag = commandKey };
            parent.Sub.Add(item);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            foreach (var category in tree1.Items)
            {
                category.Checked = true;
                foreach (var item in category.Sub)
                {
                    item.Checked = true;
                }
            }
            tree1.Invalidate();

            // 立即更新label
            label2.Text = "已全选所有优化项目";

            // 延迟刷新tree2
            System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
            {
                this.Invoke(new Action(() => ExportSelectedToTree2()));
            });
        }

        private void button4_Click(object sender, EventArgs e)
        {
            foreach (var category in tree1.Items)
            {
                category.Checked = false;
                foreach (var item in category.Sub)
                {
                    item.Checked = false;
                }
            }
            tree1.Invalidate();

            // 立即更新label
            label2.Text = "已取消所有选择";

            // 延迟刷新tree2
            System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
            {
                this.Invoke(new Action(() => ExportSelectedToTree2()));
            });
        }

        // 基本优化 - 安全且推荐的基础优化
        private void button1_Click(object sender, EventArgs e)
        {
            // 先取消所有勾选
            foreach (var category in tree1.Items)
            {
                category.Checked = false;
                foreach (var item in category.Sub)
                {
                    item.Checked = false;
                }
            }

            // 选择基本优化项目（安全、无风险）
            SelectItemsByTag(
                // 外观/资源管理器
                "5、提高前台程序的显示速度",
                "6、不要显示窗口出现和消失动画",
                "8、打开资源管理器时显示此电脑",
                "9、总是从内存中卸载无用的DLL",
                "12、禁止跟踪损坏的快捷方式",
                "13、优化Windows文件列表刷新策略",
                "14、显示已知文件类型的扩展名",
                "17、创建快捷方式时不添加“快捷方式”字样",
                "18、禁止自动播放",
                "21、快速访问不显示常用文件夹",
                "22、快速访问不显示最近使用的文件",
                "23、资源管理器崩溃时自动重启",
                "24、桌面显示此电脑",
                "25、桌面显示回收站",
                "26、隐藏桌面上的“了解此图片”图标",
                "27、微软拼音默认为英文输入",
                "28、关闭微软拼音云计算",
                "10、记事本启用自动换行",
                "11、记事本始终显示状态栏",

                // 性能优化设置
                "1、优化进程数量",
                "2、不允许在「开始」菜单显示建议",
                "3、不要在应用商店中查找关联应用",
                "4、关闭商店应用推广",
                "5、禁止应用商店自动下载和安装更新",
                "6、关闭锁屏时的Windows聚焦推广",
                "7、关闭“使用Windows时获取技巧和建议”",
                "8、禁止自动安装推荐的应用程序",
                "9、关闭游戏录制工具",
                "10、关闭多嘴的小娜",
                "11、“运行”对话框不要显示历史记录",
                "12、隐藏「开始」菜单中的“推荐”",
                "13、隐藏「开始」菜单历史记录中推荐的网站",
                "17、禁用远程修改注册表",
                "18、禁用诊断服务",
                "21、禁用错误报告",
                "22、禁用家庭组",
                "23、禁用客户体验改善计划",
                "24、禁用NTFS链接跟踪服务",
                "25、禁止自动维护计划",
                "32、优化处理器性能",
                "33、加快预读能力改善速度",
                "34、禁止系统自动生成错误报告",
                "37、关闭程序兼容性助手",
                "40、优化Windows Search和小娜的设置",
                "41、关闭广告 ID",
                "42、禁用磁盘空间不足警告",
                "43、去除搜索页面信息流和热搜",
                "45、开启GPU硬件加速",

                // 安全设置
                "6、关闭SmartScreen应用筛选器",
                "7、关闭打开程序的安全警告",

                // Edge优化设置
                "1、不要显示“首次运行”欢迎页面",
                "2、Edge浏览器关闭后禁止继续运行后台应用",
                "3、禁用启动增强",
                "4、阻止必应搜索结果中的所有广告",
                "5、从新标签页中隐藏默认的热门站点",
                "6、隐藏Edge浏览器边栏",
                "7、关闭Edge浏览器停止支持旧系统的通知",
                "8、不要发送任何诊断数据",
                "9、禁用标签页性能检测器",
                "10、禁用新选项卡页面上的微软资讯内容",
                "11、禁用个性化广告和体验",

                // 系统设置
                "6、将磁盘错误检查的等待时间缩短到五秒",
                "10、根据语言设置隐藏字体",
                "11、允许字体作为快捷方式安装",

                // 更新设置
                "1、自动安装无需重启的更新",
                "2、更新挂起时若有用户登录则不自动重启计算机",
                "3、Windows更新不包括驱动程序",
                "5、Windows更新不包括恶意软件删除工具",
                "7、不要显示“新版本记事本已可用”提示",

                // 隐私设置
                "1、禁用页面预测功能",
                "2、禁用SMS路由器服务",
                "3、禁用活动收集",
                "4、禁用应用启动跟踪",
                "5、禁用广告标识符",
                "6、禁用应用访问文件系统",
                "7、禁用应用访问文档",
                "8、禁用应用访问日历",
                "9、禁用应用访问联系人",
                "10、禁用网站语言跟踪",
                "11、禁用Windows欢迎体验",
                "12、禁用反馈频率",
                "13、禁用诊断数据收集",
                "19、禁用搜索历史",
                "20、禁用赞助商应用安装",
                "21、禁用自动连接热点",
                "31、禁用定向广告",
                "32、禁用Wi-Fi感知"
            );

            tree1.Invalidate();
            ExportSelectedToTree2();
            label2.Text = "已选择基本优化项目（安全推荐）";
        }

        // 深度优化 - 包含高级优化（有些可能影响安全性）
        private void button2_Click(object sender, EventArgs e)
        {
            // 先取消所有勾选
            foreach (var category in tree1.Items)
            {
                category.Checked = false;
                foreach (var item in category.Sub)
                {
                    item.Checked = false;
                }
            }

            // 选择深度优化项目（包含高级选项）
            SelectItemsByTag(
                // 包含所有基本优化项目
                "5、提高前台程序的显示速度",
                "6、不要显示窗口出现和消失动画",
                "8、打开资源管理器时显示此电脑",
                "9、总是从内存中卸载无用的DLL",
                "12、禁止跟踪损坏的快捷方式",
                "13、优化Windows文件列表刷新策略",
                "14、显示已知文件类型的扩展名",
                "15、不要保留“最近打开的文件”历史记录",
                "16、退出时清除“最近打开的文件”历史记录",
                "17、创建快捷方式时不添加“快捷方式”字样",
                "18、禁止自动播放",
                "21、快速访问不显示常用文件夹",
                "22、快速访问不显示最近使用的文件",
                "23、资源管理器崩溃时自动重启",
                "24、桌面显示此电脑",
                "25、桌面显示回收站",
                "26、隐藏桌面上的“了解此图片”图标",
                "27、微软拼音默认为英文输入",
                "28、关闭微软拼音云计算",
                "10、记事本启用自动换行",
                "11、记事本始终显示状态栏",

                // 性能优化设置（包含高级选项）
                "1、优化进程数量",
                "2、不允许在「开始」菜单显示建议",
                "3、不要在应用商店中查找关联应用",
                "4、关闭商店应用推广",
                "5、禁止应用商店自动下载和安装更新",
                "6、关闭锁屏时的Windows聚焦推广",
                "7、关闭“使用Windows时获取技巧和建议”",
                "8、禁止自动安装推荐的应用程序",
                "9、关闭游戏录制工具",
                "10、关闭多嘴的小娜",
                "11、“运行”对话框不要显示历史记录",
                "12、隐藏「开始」菜单中的“推荐”",
                "13、隐藏「开始」菜单历史记录中推荐的网站",
                "16、关闭远程协助",           // 高级选项
                "17、禁用远程修改注册表",
                "18、禁用诊断服务",
                "19、禁用SysMain",            // 高级选项
                "20、禁用Windows Search",     // 高级选项
                "21、禁用错误报告",
                "22、禁用家庭组",
                "23、禁用客户体验改善计划",
                "24、禁用NTFS链接跟踪服务",
                "25、禁止自动维护计划",
                "26、启用大系统缓存以提高性能", // 高级选项
                "27、禁止系统内核与驱动程序分页到硬盘", // 高级选项
                "28、增加文件管理系统缓存以提高性能", // 高级选项
                "29、启用高性能电源计划",
                "30、禁用处理器的幽灵和熔断补丁以提高性能", // 高级选项（有风险）
                "31、禁用保留的存储",         // 高级选项
                "32、优化处理器性能",
                "33、加快预读能力改善速度",
                "34、禁止系统自动生成错误报告",
                "35、禁用高精度事件定时器（HPET）",
                "36、关闭系统自动调试功能，提高系统运行速度",
                "37、关闭程序兼容性助手",
                "38、启用自动完成设备设置",
                "39、关闭Exploit Protection（乱序内存）", // 高级选项（有风险）
                "40、优化Windows Search和小娜的设置",
                "41、关闭广告 ID",
                "42、禁用磁盘空间不足警告",
                "43、去除搜索页面信息流和热搜",
                "44、关闭TSX漏洞补丁",        // 高级选项
                "45、开启GPU硬件加速",

                // 安全设置（包含高级选项）
                "1、将用户账号控制（UAC）调整为从不通知", // 高级选项（降低安全性）
                "6、关闭SmartScreen应用筛选器",
                "7、关闭打开程序的安全警告",
                "8、关闭防火墙",             // 高级选项（严重降低安全性）
                "9、关闭内存完整",           // 高级选项
                "10、关闭虚拟化安全性",       // 高级选项

                // Edge优化设置
                "1、不要显示“首次运行”欢迎页面",
                "2、Edge浏览器关闭后禁止继续运行后台应用",
                "3、禁用启动增强",
                "4、阻止必应搜索结果中的所有广告",
                "5、从新标签页中隐藏默认的热门站点",
                "6、隐藏Edge浏览器边栏",
                "7、关闭Edge浏览器停止支持旧系统的通知",
                "8、不要发送任何诊断数据",
                "9、禁用标签页性能检测器",
                "10、禁用新选项卡页面上的微软资讯内容",
                "11、禁用个性化广告和体验",
                "12、禁用不安全的下载警告",   // 高级选项

                // 系统设置
                "6、将磁盘错误检查的等待时间缩短到五秒",
                "9、关闭系统还原",           // 高级选项
                "10、根据语言设置隐藏字体",
                "11、允许字体作为快捷方式安装",

                // 更新设置
                "1、自动安装无需重启的更新",
                "2、更新挂起时若有用户登录则不自动重启计算机",
                "3、Windows更新不包括驱动程序",
                "4、禁止Win10/11进行大版本更新", // 高级选项
                "5、Windows更新不包括恶意软件删除工具",
                "6、从不检查系统更新",       // 高级选项（有风险）
                "7、不要显示“新版本记事本已可用”提示",

                // 隐私设置
                "1、禁用页面预测功能",
                "2、禁用SMS路由器服务",
                "3、禁用活动收集",
                "4、禁用应用启动跟踪",
                "5、禁用广告标识符",
                "6、禁用应用访问文件系统",
                "7、禁用应用访问文档",
                "8、禁用应用访问日历",
                "9、禁用应用访问联系人",
                "10、禁用网站语言跟踪",
                "11、禁用Windows欢迎体验",
                "12、禁用反馈频率",
                "13、禁用诊断数据收集",
                "14、禁用写作习惯跟踪",
                "15、禁用设置应用建议",
                "16、禁用Bing搜索结果",
                "17、禁用通讯录收集",
                "18、禁用键入文本收集",
                "19、禁用搜索历史",
                "20、禁用赞助商应用安装",
                "21、禁用自动连接热点",
                "22、禁用输入数据个性化",
                "23、禁用键入见解",
                "24、禁用预安装应用",
                "25、禁用.NET遥测",
                "26、禁用PowerShell遥测",
                "27、禁用遥测服务",
                "28、禁用语音激活(Cortana)",
                "29、禁用位置服务",
                "30、启用剪切板历史记录",
                "31、禁用定向广告",
                "32、禁用Wi-Fi感知",
                "33、禁用步骤记录器",
                "34、禁用写入调试信息"
            );

            tree1.Invalidate();
            ExportSelectedToTree2();
            label2.Text = "已选择深度优化项目\n（包含高级选项，请注意风险）";
        }

        private void SelectItemsByTag(params string[] tagNames)
        {
            foreach (var category in tree1.Items)
            {
                foreach (var item in category.Sub)
                {
                    // 检查当前项的tag是否在指定列表中
                    if (item.Tag != null && tagNames.Contains(item.Tag.ToString()))
                    {
                        item.Checked = true;
                        category.Checked = true; // 确保父分类也被选中（可选）
                    }
                }
            }
            tree1.Invalidate();
        }

        private void ExportSelectedToTree2()
        {
            tree2.Items.Clear(); // 清空tree2现有内容

            // 遍历tree1的所有分类
            foreach (var category in tree1.Items)
            {
                // 获取该分类下所有勾选的项目
                var selectedItems = category.Sub.Where(item => item.Checked).ToList();

                if (selectedItems.Count > 0)
                {
                    // 在tree2中创建对应的分类节点
                    var newCategory = new TreeItem(category.Text) { Tag = category.Tag };

                    // 将勾选的项目添加到新分类
                    foreach (var selectedItem in selectedItems)
                    {
                        newCategory.Sub.Add(new TreeItem(selectedItem.Text)
                        {
                            Tag = selectedItem.Tag,
                            Checked = true
                        });
                    }

                    tree2.Items.Add(newCategory);
                }
            }

            tree2.ExpandAll(); // 展开所有节点
        }

        private DateTime lastClickTime = DateTime.MinValue;

        // 简化的点击处理方法 - 只在必要时刷新
        private void Tree1_MouseClick_Simple(object sender, MouseEventArgs e)
        {
            try
            {
                TreeCType type;
                var hitNode = tree1.HitTest(e.X, e.Y, out type);

                if (hitNode != null)
                {
                    // 检查是否是子项目（不是分类节点）
                    bool isSubItem = true;
                    foreach (var category in tree1.Items)
                    {
                        if (category == hitNode)
                        {
                            isSubItem = false;
                            break;
                        }
                    }

                    if (isSubItem)
                    {
                        // 点击子项目文字，立即显示详细说明（不受防抖限制）
                        UpdateLabelBasedOnTag(hitNode.Tag?.ToString());
                    }
                    else
                    {
                        // 点击分类节点
                        label2.Text = "这是分类节点，请选择具体项目";
                    }

                    // 如果是复选框点击，使用防抖逻辑延迟刷新tree2
                    if (type.ToString().Contains("Check")) // 尝试判断是否是复选框
                    {
                        // 防止频繁点击
                        if ((DateTime.Now - lastClickTime).TotalMilliseconds < 500)
                            return;

                        lastClickTime = DateTime.Now;

                        System.Threading.Tasks.Task.Delay(200).ContinueWith(_ =>
                        {
                            this.Invoke(new Action(() => ExportSelectedToTree2()));
                        });
                    }
                }
            }
            catch
            {
                // 如果API调用失败，使用简单更新
                TrySimpleLabelUpdate();
            }
        }

        private void TrySimpleLabelUpdate()
        {
            try
            {
                // 尝试获取当前选中的项目
                var selectedItem = FindFirstSelectedItem();
                if (selectedItem != null)
                {
                    UpdateLabelBasedOnTag(selectedItem.Tag?.ToString());
                }
                else
                {
                    label2.Text = "请选择具体优化项目";
                }
            }
            catch
            {
                label2.Text = "选择已更新";
            }
        }
        private TreeItem FindFirstSelectedItem()
        {
            foreach (var category in tree1.Items)
            {
                foreach (var item in category.Sub)
                {
                    if (item.Checked)
                    {
                        return item;
                    }
                }
            }
            return null;
        }

        // 根据tag更新label的文字说明
        private void UpdateLabelBasedOnTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                label2.Text = "请选择具体优化项目";
                return;
            }

            // 添加特殊情况的处理
            if (tag == "全选")
            {
                label2.Text = "已选择所有优化项目";
                return;
            }

            switch (tag)
            {
                // 外观/资源管理器
                case "1、隐藏任务栏搜索框":
                    label2.Text = "隐藏任务栏搜索框可以节省任务栏空间，让界面更简洁（22H2及以下系统有效，手动隐藏就行）";
                    break;
                case "2、隐藏“任务视图”按钮":
                    label2.Text = "隐藏任务视图按钮，简化任务栏布局";
                    break;
                case "3、始终在任务栏显示所有图标和通知":
                    label2.Text = "确保所有程序图标和通知都显示在任务栏，方便快速访问";
                    break;
                case "4、任务栏窗口被占满时合并":
                    label2.Text = "当任务栏空间不足时自动合并相似窗口，节省空间";
                    break;
                case "5、提高前台程序的显示速度":
                    label2.Text = "优化前台程序的显示性能，减少卡顿现象\n（by 闰月优葡）";
                    break;
                case "6、不要显示窗口出现和消失动画":
                    label2.Text = "禁用窗口动画效果，加快窗口切换速度\n（by 闰月优葡）";
                    break;
                case "7、使「开始」菜单、任务栏、操作中心透明":
                    label2.Text = "启用透明效果，提升界面美观度";
                    break;
                case "8、打开资源管理器时显示此电脑":
                    label2.Text = "资源管理器默认显示此电脑而不是快速访问";
                    break;
                case "9、总是从内存中卸载无用的DLL":
                    label2.Text = "及时清理内存中不再使用的DLL文件，释放内存资源\n（by 闰月优葡）";
                    break;
                case "10、记事本启用自动换行":
                    label2.Text = "为记事本启用自动换行功能\n（by 一叶微风TM）";
                    break;
                case "11、记事本始终显示状态栏":
                    label2.Text = "让记事本始终显示状态栏信息\n（by 一叶微风TM）";
                    break;
                case "12、禁止跟踪损坏的快捷方式":
                    label2.Text = "防止系统自动搜索和修复损坏的快捷方式\n（by 闰月优葡）";
                    break;
                case "13、优化Windows文件列表刷新策略":
                    label2.Text = "改进文件列表刷新机制，提升资源管理器响应速度\n（by 闰月优葡）";
                    break;
                case "14、显示已知文件类型的扩展名":
                    label2.Text = "始终显示文件扩展名，方便识别文件类型";
                    break;
                case "15、不要保留“最近打开的文件”历史记录":
                    label2.Text = "禁用最近文件历史记录功能，保护隐私\n（by 闰月优葡 &amp; 组策略编辑器）";
                    break;
                case "16、退出时清除“最近打开的文件”历史记录":
                    label2.Text = "每次退出时自动清理文件使用历史\n（by 闰月优葡 &amp; 组策略编辑器）";
                    break;
                case "17、创建快捷方式时不添加“快捷方式”字样":
                    label2.Text = "创建快捷方式时不再自动添加“快捷方式”文字\n（by 518516.net）";
                    break;
                case "18、禁止自动播放":
                    label2.Text = "禁用自动播放功能，防止U盘病毒自动运行\n（by Rambin）";
                    break;
                case "19、在单独的进程中打开文件夹窗口":
                    label2.Text = "每个文件夹窗口使用独立进程，提高稳定性\n（by 一叶微风TM）";
                    break;
                case "20、标题栏显示完整路径":
                    label2.Text = "在资源管理器标题栏显示完整文件路径\n（by 原罪）";
                    break;
                case "21、快速访问不显示常用文件夹":
                    label2.Text = "禁用快速访问中的常用文件夹显示\n（by 溯汐潮）";
                    break;
                case "22、快速访问不显示最近使用的文件":
                    label2.Text = "禁用快速访问中的最近文件显示\n（by 溯汐潮）";
                    break;
                case "23、资源管理器崩溃时自动重启":
                    label2.Text = "资源管理器崩溃后自动重新启动，提高系统稳定性\n（by 闰月优葡）";
                    break;
                case "24、桌面显示此电脑":
                    label2.Text = "在桌面显示此电脑图标，方便快速访问";
                    break;
                case "25、桌面显示回收站":
                    label2.Text = "在桌面显示回收站图标";
                    break;
                case "26、隐藏桌面上的“了解此图片”图标":
                    label2.Text = "隐藏桌面上的无用图标，保持桌面整洁\n（by 闰月优葡）";
                    break;
                case "27、微软拼音默认为英文输入":
                    label2.Text = "设置微软拼音输入法默认英文状态\n（by Rambin）";
                    break;
                case "28、关闭微软拼音云计算":
                    label2.Text = "禁用微软拼音的云输入功能，保护隐私\n（by Rambin）";
                    break;
                case "29、去除本地磁盘重复显示":
                    label2.Text = "通常插入移动磁盘资源管理器会重复显示盘符，若不习惯可以去除\n（感谢群友Dongle的建议）";
                    break;

                // 性能优化设置
                case "1、优化进程数量":
                    label2.Text = "Windows服务主机进程优化，合并进程项目，减少内存占用";
                    break;
                case "2、不允许在「开始」菜单显示建议":
                    label2.Text = "彻底禁用开始菜单的建议功能\n（by powerxing04）";
                    break;
                case "3、不要在应用商店中查找关联应用":
                    label2.Text = "禁止系统在应用商店中搜索关联应用\n（by Asoft）";
                    break;
                case "4、关闭商店应用推广":
                    label2.Text = "禁用应用商店的推广和广告内容\n（by 、Cloud.）";
                    break;
                case "5、禁止应用商店自动下载和安装更新":
                    label2.Text = "防止应用商店自动更新应用程序\n（by 闰月优葡）";
                    break;
                case "6、关闭锁屏时的Windows聚焦推广":
                    label2.Text = "禁用锁屏界面上的Windows聚焦广告\n（by 、Cloud.）";
                    break;
                case "7、关闭“使用Windows时获取技巧和建议”":
                    label2.Text = "关闭系统提示和建议功能\n（by 、Cloud.）";
                    break;
                case "8、禁止自动安装推荐的应用程序":
                    label2.Text = "防止系统自动安装推荐的应用\n（by IT之家）";
                    break;
                case "9、关闭游戏录制工具":
                    label2.Text = "禁用游戏栏和游戏录制功能\n（by 、Cloud.）";
                    break;
                case "10、关闭多嘴的小娜":
                    label2.Text = "完全禁用Cortana语音助手\n（by 朽木）";
                    break;
                case "11、“运行”对话框不要显示历史记录":
                    label2.Text = "清除运行对话框的历史记录\n（by 闰月优葡）";
                    break;
                case "12、隐藏「开始」菜单中的“推荐”":
                    label2.Text = "隐藏开始菜单的推荐区域\n（by 闰月优葡）";
                    break;
                case "13、隐藏「开始」菜单历史记录中推荐的网站":
                    label2.Text = "清除开始菜单中的网站推荐\n（by 闰月优葡）";
                    break;
                case "14、加快关机速度":
                    label2.Text = "优化关机流程，减少关机时间\n（注意：可能会导致部分游戏反作弊无法正常运行）\n（by 闰月优葡）";
                    break;
                case "15、缩短关闭服务等待时间":
                    label2.Text = "减少服务关闭的等待时间，加快关机速度\n（注意：可能会导致部分游戏反作弊无法正常运行）\n（by 闰月优葡）";
                    break;
                case "16、关闭远程协助":
                    label2.Text = "禁用远程协助功能，增强安全性\n（by 原罪）";
                    break;
                case "17、禁用远程修改注册表":
                    label2.Text = "防止远程修改注册表，提高安全性\n（by 一叶微风TM）";
                    break;
                case "18、禁用诊断服务":
                    label2.Text = "禁用系统诊断服务，减少资源占用";
                    break;
                case "19、禁用SysMain":
                    label2.Text = "禁用SysMain服务\n（原SuperFetch），减少磁盘占用\n（by 闰月优葡）";
                    break;
                case "20、禁用Windows Search":
                    label2.Text = "禁用Windows搜索服务，提升性能但影响搜索功能";
                    break;
                case "21、禁用错误报告":
                    label2.Text = "禁用Windows错误报告功能";
                    break;
                case "22、禁用家庭组":
                    label2.Text = "禁用家庭组功能（Windows 10）";
                    break;
                case "23、禁用客户体验改善计划":
                    label2.Text = "退出客户体验改善计划，减少数据上传\n（by Windows 10优化辅助工具）";
                    break;
                case "24、禁用NTFS链接跟踪服务":
                    label2.Text = "禁用NTFS链接跟踪，提升性能\n（by 某宅）";
                    break;
                case "25、禁止自动维护计划":
                    label2.Text = "禁用系统自动维护任务\n（by Lux ferre）";
                    break;
                case "26、启用大系统缓存以提高性能":
                    label2.Text = "启用大系统缓存，提升系统性能但增加内存占用";
                    break;
                case "27、禁止系统内核与驱动程序分页到硬盘":
                    label2.Text = "防止系统内核分页到硬盘，提高响应速度";
                    break;
                case "28、增加文件管理系统缓存以提高性能":
                    label2.Text = "增大文件系统缓存，提升文件操作性能";
                    break;
                case "29、启用高性能电源计划":
                    label2.Text = "有利于提高性能，但会增加功耗";
                    break;
                case "30、禁用处理器的幽灵和熔断补丁以提高性能":
                    label2.Text = "禁用安全补丁以提升性能（降低安全性）";
                    break;
                case "31、禁用保留的存储":
                    label2.Text = "释放系统保留的存储空间\n（by 闰月优葡）";
                    break;
                case "32、优化处理器性能":
                    label2.Text = "优化进程优先度，利于降低游戏延迟。\n若low帧不稳定或硬件“不趁手”，还原此项";
                    break;
                case "33、加快预读能力改善速度":
                    label2.Text = "优化预读功能，提升程序启动速度";
                    break;
                case "34、禁止系统自动生成错误报告":
                    label2.Text = "禁用系统错误报告生成";
                    break;
                case "35、禁用高精度事件定时器（HPET）":
                    label2.Text = "可提升游戏性能和系统响应速度\n有些应用需要HPET，若出现问题还原";
                    break;
                case "36、关闭系统自动调试功能，提高系统运行速度":
                    label2.Text = "禁用系统调试功能，提升性能";
                    break;
                case "37、关闭程序兼容性助手":
                    label2.Text = "禁用程序兼容性助手服务";
                    break;
                case "38、启用自动完成设备设置":
                    label2.Text = "设置自动完成便于带密码的用户预先加载进去，减少输入密码后加载时间";
                    break;
                case "39、关闭Exploit Protection（乱序内存）":
                    label2.Text = "禁用漏洞利用保护\n（可能降低安全性）";
                    break;
                case "40、优化Windows Search和小娜的设置":
                    label2.Text = "优化搜索和语音助手设置";
                    break;
                case "41、关闭广告 ID":
                    label2.Text = "禁用广告标识符，保护隐私";
                    break;
                case "42、禁用磁盘空间不足警告":
                    label2.Text = "当磁盘空间不足时不再弹出警告提示，避免游戏过程中被警告打断";
                    break;
                case "43、去除搜索页面信息流和热搜":
                    label2.Text = "清理搜索页面的推荐内容";
                    break;
                case "44、关闭TSX漏洞补丁":
                    label2.Text = "禁用TSX漏洞补丁以提升性能";
                    break;
                case "45、开启GPU硬件加速":
                    label2.Text = "启用GPU硬件加速，提升图形性能";
                    break;

                // 安全设置
                case "1、将用户账号控制（UAC）调整为从不通知":
                    label2.Text = "完全禁用UAC提示\n（降低安全性）";
                    break;
                case "2、用于内置管理员账户的管理审批模式":
                    label2.Text = "配置管理员账户审批模式\n（by 坏坏小生）";
                    break;
                case "3、以管理审批模式运行所有管理员":
                    label2.Text = "为所有管理员启用审批模式\n（by 闰月优葡）";
                    break;
                case "4、仅提升安全路径下的UIAccess程序":
                    label2.Text = "限制UIAccess程序提升权限\n（by 闰月优葡）";
                    break;
                case "5、允许UIAccess程序在非安全桌面上提升":
                    label2.Text = "放宽UIAccess程序权限限制\n（by 闰月优葡）";
                    break;
                case "6、关闭SmartScreen应用筛选器":
                    label2.Text = "禁用SmartScreen应用筛选功能\n（by Windows 10优化辅助工具）";
                    break;
                case "7、关闭打开程序的安全警告":
                    label2.Text = "禁用未知程序运行警告\n（by 莫失莫忘）";
                    break;
                case "8、关闭防火墙":
                    label2.Text = "禁用Windows防火墙\n（严重降低安全性）\n（by 一叶微风TM）";
                    break;
                case "9、关闭内存完整":
                    label2.Text = "禁用内存完整性保护\n（注意：可能会导致部分游戏反作弊无法正常运行）";
                    break;
                case "10、关闭虚拟化安全性":
                    label2.Text = "禁用虚拟化安全功能\n（注意：可能会导致部分游戏反作弊无法正常运行）";
                    break;

                // Edge优化设置
                case "1、不要显示“首次运行”欢迎页面":
                    label2.Text = "跳过Edge浏览器首次运行欢迎页\n（by IT之家）";
                    break;
                case "2、Edge浏览器关闭后禁止继续运行后台应用":
                    label2.Text = "防止Edge在关闭后继续后台运行\n（by 闰月优葡 &amp; Edge配置百科）";
                    break;
                case "3、禁用启动增强":
                    label2.Text = "禁用Edge自启动功能\n（by 闰月优葡 &amp; Edge配置百科）";
                    break;
                case "4、阻止必应搜索结果中的所有广告":
                    label2.Text = "屏蔽必应搜索中的广告内容\n（by 闰月优葡 &amp; Edge配置百科）";
                    break;
                case "5、从新标签页中隐藏默认的热门站点":
                    label2.Text = "隐藏新标签页的推荐网站\n（by 闰月优葡）";
                    break;
                case "6、隐藏Edge浏览器边栏":
                    label2.Text = "禁用Edge侧边栏功能\n（by 闰月优葡）";
                    break;
                case "7、关闭Edge浏览器停止支持旧系统的通知":
                    label2.Text = "禁用Edge版本过期提示\n（by 闰月优葡）";
                    break;
                case "8、不要发送任何诊断数据":
                    label2.Text = "禁止Edge发送诊断数据\n（by 闰月优葡）";
                    break;
                case "9、禁用标签页性能检测器":
                    label2.Text = "禁用Edge标签页性能监控\n（by 闰月优葡 &amp; Edge配置百科）";
                    break;
                case "10、禁用新选项卡页面上的微软资讯内容":
                    label2.Text = "移除新标签页的新闻推荐\n（by 闰月优葡 &amp; Edge配置百科）";
                    break;
                case "11、禁用个性化广告和体验":
                    label2.Text = "禁用Edge个性化广告\n（by 闰月优葡）";
                    break;
                case "12、禁用不安全的下载警告":
                    label2.Text = "关闭下载安全警告\n（by 闰月优葡 &amp; Edge配置百科）";
                    break;

                // 系统设置
                case "1、关闭休眠":
                    label2.Text = "禁用系统休眠功能，释放磁盘空间\n（by powerxing04）";
                    break;
                case "2、弹出USB磁盘后彻底断开其电源":
                    label2.Text = "安全移除USB设备后完全断电\n（by 闰月优葡）";
                    break;
                case "3、不要将VHD动态文件扩展到最大以节省磁盘空间":
                    label2.Text = "优化VHD动态磁盘空间使用";
                    break;
                case "4、蓝屏时自动重启":
                    label2.Text = "系统蓝屏后自动重新启动\n（by 原罪）";
                    break;
                case "5、关闭系统自动调试功能":
                    label2.Text = "禁用系统自动调试功能\n（by 闰月优葡）";
                    break;
                case "6、将磁盘错误检查的等待时间缩短到五秒":
                    label2.Text = "减少磁盘错误检查等待时间\n（by 闰月优葡）";
                    break;
                case "7、设备安装禁止创建系统还原点":
                    label2.Text = "安装设备驱动时不创建还原点\n（by 闰月优葡）";
                    break;
                case "8、MSI类软件安装禁止创建系统还原点":
                    label2.Text = "安装MSI程序时不创建还原点\n（by 闰月优葡）";
                    break;
                case "9、关闭系统还原":
                    label2.Text = "完全禁用系统还原功能\n（by 闰月优葡）";
                    break;
                case "10、根据语言设置隐藏字体":
                    label2.Text = "按语言设置隐藏不常用字体\n（by 闰月优葡）";
                    break;
                case "11、允许字体作为快捷方式安装":
                    label2.Text = "允许以快捷方式安装字体节省空间\n（by 闰月优葡）";
                    break;
                case "12、崩溃时不写入调试信息":
                    label2.Text = "系统崩溃时不生成调试文件";
                    break;
                case "13、禁用账户登录日志报告":
                    label2.Text = "禁用登录日志记录\n（by 溯汐潮）";
                    break;
                case "14、禁用WfpDiag.ETL日志":
                    label2.Text = "禁用防火墙诊断日志\n（by powerxing04）";
                    break;

                // 更新设置
                case "1、自动安装无需重启的更新":
                    label2.Text = "自动安装不需要重启的更新\n（by Rambin）";
                    break;
                case "2、更新挂起时若有用户登录则不自动重启计算机":
                    label2.Text = "用户登录时不自动重启安装更新\n（by Rambin）";
                    break;
                case "3、Windows更新不包括驱动程序":
                    label2.Text = "禁止通过Windows更新安装驱动";
                    break;
                case "4、禁止Win10/11进行大版本更新":
                    label2.Text = "阻止功能更新，只接收安全更新\n（by 闰月优葡）";
                    break;
                case "5、Windows更新不包括恶意软件删除工具":
                    label2.Text = "不通过更新安装恶意软件工具\n（by Winaero）";
                    break;
                case "6、从不检查系统更新":
                    label2.Text = "完全禁用Windows更新检查";
                    break;
                case "7、不要显示“新版本记事本已可用”提示":
                    label2.Text = "禁用记事本更新提示\n（by 闰月优葡）";
                    break;

                // 隐私设置
                case "1、禁用页面预测功能":
                    label2.Text = "禁用浏览器页面预加载";
                    break;
                case "2、禁用SMS路由器服务":
                    label2.Text = "关闭系统内置的短信路由服务，该服务主要用于移动设备管理。需要连接手机或使用远程设备管理功能，请勿禁用此服务";
                    break;
                case "3、禁用活动收集":
                    label2.Text = "禁止收集用户活动数据";
                    break;
                case "4、禁用应用启动跟踪":
                    label2.Text = "禁用应用启动次数跟踪";
                    break;
                case "5、禁用广告标识符":
                    label2.Text = "禁用广告ID跟踪";
                    break;
                case "6、禁用应用访问文件系统":
                    label2.Text = "限制应用访问文件系统";
                    break;
                case "7、禁用应用访问文档":
                    label2.Text = "限制应用访问文档库";
                    break;
                case "8、禁用应用访问日历":
                    label2.Text = "限制应用访问日历";
                    break;
                case "9、禁用应用访问联系人":
                    label2.Text = "限制应用访问联系人";
                    break;
                case "10、禁用网站语言跟踪":
                    label2.Text = "禁止跟踪网站语言偏好";
                    break;
                case "11、禁用Windows欢迎体验":
                    label2.Text = "禁用首次登录的欢迎体验";
                    break;
                case "12、禁用反馈频率":
                    label2.Text = "禁用系统反馈功能";
                    break;
                case "13、禁用诊断数据收集":
                    label2.Text = "彻底禁用诊断数据收集";
                    break;
                case "14、禁用写作习惯跟踪":
                    label2.Text = "禁用输入习惯跟踪";
                    break;
                case "15、禁用设置应用建议":
                    label2.Text = "禁用设置中的应用推荐";
                    break;
                case "16、禁用Bing搜索结果":
                    label2.Text = "禁用Bing搜索集成";
                    break;
                case "17、禁用通讯录收集":
                    label2.Text = "禁止收集通讯录信息";
                    break;
                case "18、禁用键入文本收集":
                    label2.Text = "禁止收集输入内容";
                    break;
                case "19、禁用搜索历史":
                    label2.Text = "清除搜索历史记录";
                    break;
                case "20、禁用赞助商应用安装":
                    label2.Text = "防止安装赞助商应用";
                    break;
                case "21、禁用自动连接热点":
                    label2.Text = "禁用自动连接开放热点";
                    break;
                case "22、禁用输入数据个性化":
                    label2.Text = "禁用输入个性化功能";
                    break;
                case "23、禁用键入见解":
                    label2.Text = "禁用输入预测功能";
                    break;
                case "24、禁用预安装应用":
                    label2.Text = "禁用系统预装应用";
                    break;
                case "25、禁用.NET遥测":
                    label2.Text = "禁用.NET框架遥测";
                    break;
                case "26、禁用PowerShell遥测":
                    label2.Text = "禁用PowerShell遥测数据";
                    break;
                case "27、禁用遥测服务":
                    label2.Text = "彻底禁用系统遥测服务";
                    break;
                case "28、禁用语音激活(Cortana)":
                    label2.Text = "禁用Cortana语音激活";
                    break;
                case "29、禁用位置服务":
                    label2.Text = "禁用地理位置服务";
                    break;
                case "30、启用剪切板历史记录":
                    label2.Text = "Win+V保存剪切板历史记录，便于复制粘贴\n（by 闰月优葡）";
                    break;
                case "31、禁用定向广告":
                    label2.Text = "禁用个性化广告";
                    break;
                case "32、禁用Wi-Fi感知":
                    label2.Text = "禁用Wi-Fi感知功能";
                    break;
                case "33、禁用步骤记录器":
                    label2.Text = "禁用问题步骤记录器";
                    break;
                case "34、禁用写入调试信息":
                    label2.Text = "禁用调试信息写入";
                    break;

                default:
                    label2.Text = $"已选择: {tag}";
                    break;
            }
        }

        // 导入配置按钮点击事件
        private void button5_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                // 设置默认路径为Config文件夹
                string ConfigFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

                // 确保Config文件夹存在
                if (!Directory.Exists(ConfigFolder))
                {
                    Directory.CreateDirectory(ConfigFolder);
                }

                openFileDialog.InitialDirectory = ConfigFolder;
                openFileDialog.Filter = "配置文件 (*.ini)|*.ini|所有文件 (*.*)|*.*";
                openFileDialog.Title = "选择优化配置文件";
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        LoadConfigFromFile(openFileDialog.FileName);
                        ExportSelectedToTree2(); // 更新tree2显示
                        label2.Text = $"已从 {Path.GetFileName(openFileDialog.FileName)} 导入配置";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导入配置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        // 导出配置按钮点击事件
        private void button6_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                // 设置默认路径为Config文件夹
                string ConfigFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

                // 确保Config文件夹存在
                if (!Directory.Exists(ConfigFolder))
                {
                    Directory.CreateDirectory(ConfigFolder);
                }

                saveFileDialog.InitialDirectory = ConfigFolder;
                string fileName = $"ZyperWin++{DateTime.Now:yyyyMMddHHmmss}.ini";
                saveFileDialog.FileName = fileName;
                saveFileDialog.Filter = "配置文件 (*.ini)|*.ini|所有文件 (*.*)|*.*";
                saveFileDialog.Title = "保存优化配置";
                saveFileDialog.RestoreDirectory = true;

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        SaveConfigToFile(saveFileDialog.FileName);
                        label2.Text = $"配置已保存到 {Path.GetFileName(saveFileDialog.FileName)}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出配置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        // 从文件加载配置
        private void LoadConfigFromFile(string filePath)
        {
            var config = new Dictionary<string, bool>();

            // 读取INI文件
            foreach (string line in File.ReadAllLines(filePath))
            {
                if (line.Contains("=") && !line.StartsWith("[") && !string.IsNullOrWhiteSpace(line))
                {
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        bool value = parts[1].Trim() == "1";
                        config[key] = value;
                    }
                }
            }

            // 先处理所有子项目
            foreach (var category in tree1.Items)
            {
                foreach (var item in category.Sub)
                {
                    if (item.Tag != null && config.ContainsKey(item.Tag.ToString()))
                    {
                        item.Checked = config[item.Tag.ToString()];
                    }
                    else
                    {
                        item.Checked = false; // 配置中不存在的项目取消选中
                    }
                }
            }

            // 然后根据子项目状态更新分类节点状态
            UpdateCategoryCheckStates();

            tree1.Invalidate(); // 刷新显示
            ExportSelectedToTree2(); // 更新tree2
        }

        // 保存配置到文件
        private void SaveConfigToFile(string filePath)
        {
            var lines = new List<string>();
            lines.Add("[ZyperWin优化配置]");
            lines.Add($"生成时间={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add("");

            // 收集所有选中状态
            foreach (var category in tree1.Items)
            {
                // 保存分类状态
                lines.Add($"{category.Tag}={(category.Checked ? "1" : "0")}");

                foreach (var item in category.Sub)
                {
                    lines.Add($"{item.Tag}={(item.Checked ? "1" : "0")}");
                }
                lines.Add(""); // 空行分隔不同分类
            }

            // 写入文件
            File.WriteAllLines(filePath, lines);
        }

        private async void button7_Click(object sender, EventArgs e)
        {
            // 检查是否有选中的项目
            bool hasSelectedItems = false;
            int selectedCount = 0;
            int alreadyOptimizedCount = 0;
            bool actuallyOptimizedExplorerItem = false;

            // 检查是否包含需要重启资源管理器的项目
            foreach (var category in tree1.Items)
            {
                // 检查是否是外观/资源管理器分类
                bool isExplorerCategory = explorerCategories.Contains(category.Tag?.ToString()) ||
                                         category.Text.Contains("外观") ||
                                         category.Text.Contains("资源管理器");

                foreach (var item in category.Sub)
                {
                    if (item.Checked && item.Tag != null)
                    {
                        hasSelectedItems = true;
                        selectedCount++;

                        string itemTag = item.Tag.ToString();
                        bool isAlreadyOptimized = optimizationStatus.ContainsKey(itemTag) && optimizationStatus[itemTag];

                        // 如果是外观分类且不是已优化的项目，标记需要重启
                        if (isExplorerCategory && !isAlreadyOptimized)
                        {
                            actuallyOptimizedExplorerItem = true;
                        }

                        // 检查是否已经优化
                        if (isAlreadyOptimized)
                        {
                            alreadyOptimizedCount++;
                        }
                    }
                }
            }

            if (!hasSelectedItems)
            {
                MessageBox.Show("请先选择要优化的项目", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 显示选中的项目数量，包括已优化的
            string confirmMessage = $"确定要执行优化操作吗？\n共选择了 {selectedCount} 个项目";

            if (alreadyOptimizedCount > 0)
            {
                confirmMessage += $"\n其中 {alreadyOptimizedCount} 个项目已经优化过，将自动跳过";
                confirmMessage += $"\n实际将优化 {selectedCount - alreadyOptimizedCount} 个项目";
            }

            if (actuallyOptimizedExplorerItem)
            {
                confirmMessage += "\n优化完成后将重启资源管理器。";
            }

            // 确认对话框
            DialogResult result = MessageBox.Show(confirmMessage,
                "C DiskGlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                try
                {
                    // 冻结所有控件
                    SetControlsEnabled(false);

                    // 异步执行优化
                    await PerformOptimizationWithProgress(false);

                    // 关键修改：优化完成后立即刷新检测状态
                    CheckAllOptimizationStatus();

                    // 修正：只有真正优化了外观项目才重启
                    if (actuallyOptimizedExplorerItem)
                    {
                        RestartExplorer();
                    }

                    // 保存配置到文件
                    SaveConfigToConfigFolder();

                    string completionMessage = $"优化完成！\n共优化了 {selectedCount - alreadyOptimizedCount} 个项目";

                    if (alreadyOptimizedCount > 0)
                    {
                        completionMessage += $"\n跳过 {alreadyOptimizedCount} 个已优化项目";
                    }

                    if (actuallyOptimizedExplorerItem)
                    {
                        completionMessage += "\n资源管理器已重启";
                    }

                    completionMessage += "\n已优化的项目已显示(已优化)符号";

                    OperationLogger.Info("系统优化", string.Format("优化 {0} 个项目，跳过 {1} 个", selectedCount - alreadyOptimizedCount, alreadyOptimizedCount));
                    MessageBox.Show(completionMessage, "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    // 出错时也刷新状态
                    CheckAllOptimizationStatus();
                    OperationLogger.Error("系统优化", ex.Message);
                    MessageBox.Show($"优化过程中出现错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    // 恢复所有控件
                    SetControlsEnabled(true);
                }
            }
        }

        private async void button8_Click(object sender, EventArgs e)
        {
            // 检查是否有选中的项目
            bool hasSelectedItems = false;
            int selectedCount = 0;
            bool actuallyRestoredExplorerItem = false;

            foreach (var category in tree1.Items)
            {
                // 检查是否是外观/资源管理器分类
                bool isExplorerCategory = explorerCategories.Contains(category.Tag?.ToString()) ||
                                         category.Text.Contains("外观") ||
                                         category.Text.Contains("资源管理器");

                foreach (var item in category.Sub)
                {
                    if (item.Checked)
                    {
                        hasSelectedItems = true;
                        selectedCount++;
                        // 如果选中了外观/资源管理器分类的项目，标记需要重启
                        if (isExplorerCategory)
                        {
                            actuallyRestoredExplorerItem = true;
                        }
                    }
                }
            }

            if (!hasSelectedItems)
            {
                MessageBox.Show("请先选择要还原的项目", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 确认对话框
            string confirmMessage = $"确定要执行还原操作吗？\n共选择了 {selectedCount} 个项目";

            if (actuallyRestoredExplorerItem)
            {
                confirmMessage += "\n还原完成后将重启资源管理器。";
            }

            DialogResult result = MessageBox.Show(confirmMessage,
                "C DiskGlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                try
                {
                    // 冻结所有控件
                    SetControlsEnabled(false);

                    // 异步执行还原
                    await PerformOptimizationWithProgress(true);

                    // 关键修改：还原完成后立即刷新检测状态
                    CheckAllOptimizationStatus();
                    // 修正：只有还原了外观项目才重启
                    if (actuallyRestoredExplorerItem)
                    {
                        RestartExplorer();
                    }

                    string completionMessage = $"还原完成！\n共还原了 {selectedCount} 个项目\n状态已更新";

                    if (actuallyRestoredExplorerItem)
                    {
                        completionMessage += "\n资源管理器已重启";
                    }

                    OperationLogger.Info("系统优化", "还原 " + selectedCount + " 个项目");
                    MessageBox.Show(completionMessage,
                        "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    // 出错时也刷新状态
                    CheckAllOptimizationStatus();
                    OperationLogger.Error("系统优化", "还原失败：" + ex.Message);
                    MessageBox.Show($"还原过程中出现错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    // 恢复所有控件
                    SetControlsEnabled(true);
                }
            }
        }

        public static int JournaledOptimizationCount()
        {
            return ReliableOptimizationExecutor.JournalCount;
        }

        public async Task<int> RestoreJournaledOptimizationsAsync()
        {
            if (ReliableOptimizationExecutor.JournalCount == 0) return 0;

            SetControlsEnabled(false);
            try
            {
                OptimizationExecutionResult result = await Task.Run(ReliableOptimizationExecutor.RestoreAllRecorded);
                CheckAllOptimizationStatus();
                if (!result.Success) throw new InvalidOperationException(result.Message);
                OperationLogger.Info("系统优化", result.Message);
                return result.AffectedCount;
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        // 冻结或恢复所有控件
        private void SetControlsEnabled(bool enabled)
        {
            // 冻结/恢复按钮
            button1.Enabled = enabled;
            button2.Enabled = enabled;
            button3.Enabled = enabled;
            button4.Enabled = enabled;
            button5.Enabled = enabled;
            button6.Enabled = enabled;
            button7.Enabled = enabled;
            button8.Enabled = enabled;
            button9.Enabled = enabled;
            button10.Enabled = enabled;

            // 冻结/恢复树形控件
            tree1.Enabled = enabled;
            tree2.Enabled = enabled;

            // 冻结/恢复主窗口菜单
            var mainWindow = this.ParentForm as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.SetMenuEnabled(enabled);
            }

            // 强制刷新界面
            this.Refresh();
        }

        // 增强的ExecuteCommand方法
        private void ExecuteCommand(XElement command)
        {
            string commandName = command.Name.LocalName;
            Console.WriteLine($"准备执行命令: {commandName}");

            try
            {
                switch (commandName)
                {
                    case "RegWrite":
                        ExecuteRegWrite(command);
                        break;
                    case "RegDelete":
                        ExecuteRegDelete(command);
                        break;
                    case "RegMove":
                        ExecuteRegMove(command);
                        break;
                    case "SetServiceStart":
                        ExecuteSetServiceStartEnhanced(command);
                        break;
                    case "ExplorerNotify":
                        ExecuteExplorerNotify(command);
                        break;
                    case "PowerCfg":  // 新增PowerCfg命令支持
                        ExecutePowerCfg(command);
                        break;
                    default:
                        Console.WriteLine($"未知命令: {commandName}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行命令 {commandName} 失败: {ex.Message}");
                Console.WriteLine($"命令详情: {command}");
            }
        }

        // 新增PowerCfg命令执行方法
        private void ExecutePowerCfg(XElement command)
        {
            string type = command.Attribute("Type")?.Value;
            string scheme = command.Attribute("Scheme")?.Value;

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(scheme))
            {
                Console.WriteLine("PowerCfg: 缺少Type或Scheme参数");
                return;
            }

            try
            {
                string powerCfgCommand = "";

                if (type.ToLower() == "setactive")
                {
                    powerCfgCommand = $"powercfg -setactive {scheme}";
                }
                else
                {
                    Console.WriteLine($"不支持的PowerCfg类型: {type}");
                    return;
                }

                Console.WriteLine($"执行PowerCfg命令: {powerCfgCommand}");

                // 使用现有的命令行执行方法
                ExecuteCommandLine(powerCfgCommand);

                Console.WriteLine($"PowerCfg命令完成: {powerCfgCommand}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PowerCfg命令执行失败: {ex.Message}");
            }
        }

        // 检查快捷方式文字优化状态
        private bool CheckShortcutTextOptimization()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("Link");
                        if (value is byte[] bytes && bytes.Length >= 4)
                        {
                            // Link值为00 00 00 00表示已优化（不显示"快捷方式"文字）
                            bool isOptimized = bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x00 && bytes[3] == 0x00;
                            Console.WriteLine($"快捷方式文字优化检查: {BitConverter.ToString(bytes)}, 结果: {isOptimized}");
                            return isOptimized;
                        }
                    }
                }
                Console.WriteLine("快捷方式文字优化检查: 未找到Link值，认为未优化");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查快捷方式文字优化失败: {ex.Message}");
                return false;
            }
        }

        // 检查乱序内存优化状态
        private bool CheckExploitProtectionStatus()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"System\ControlSet001\Control\Session Manager\kernel"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("MitigationOptions");
                        if (value is byte[] bytes && bytes.Length >= 16)
                        {
                            // 根据实际的优化值来判断
                            // 这里需要根据你的具体优化配置来调整
                            bool isOptimized = bytes[0] == 0x22 && bytes[1] == 0x22 && bytes[2] == 0x22;
                            Console.WriteLine($"乱序内存优化检查: {BitConverter.ToString(bytes)}, 结果: {isOptimized}");
                            return isOptimized;
                        }
                    }
                }
                Console.WriteLine("乱序内存优化检查: 未找到MitigationOptions值，认为未优化");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查乱序内存优化失败: {ex.Message}");
                return false;
            }
        }

        // 增强的服务设置方法
        private void ExecuteSetServiceStartEnhanced(XElement command)
        {
            string name = command.Attribute("Name")?.Value;
            string startType = command.Attribute("Type")?.Value;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(startType))
            {
                Console.WriteLine("SetServiceStart: 缺少Name或Type参数");
                return;
            }

            try
            {
                // 直接通过注册表修改服务启动类型
                string serviceKeyPath = $@"SYSTEM\CurrentControlSet\services\{name}";

                using (RegistryKey serviceKey = Registry.LocalMachine.OpenSubKey(serviceKeyPath, true))
                {
                    if (serviceKey != null)
                    {
                        int startValue = Convert.ToInt32(startType);
                        serviceKey.SetValue("Start", startValue, RegistryValueKind.DWord);
                        Console.WriteLine($"服务启动类型设置成功: {name} -> {startType}");

                        // 如果设置为禁用(4)，尝试停止服务
                        if (startType == "4")
                        {
                            StopService(name);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"无法打开服务注册表键: {serviceKeyPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"服务设置失败: {ex.Message}");
            }
        }
        private void StopService(string serviceName)
        {
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = "net";
                    process.StartInfo.Arguments = $"stop {serviceName}";
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);

                    if (process.ExitCode == 0)
                    {
                        Console.WriteLine($"服务停止成功: {serviceName}");
                    }
                    else
                    {
                        Console.WriteLine($"服务停止失败: {serviceName}, 退出代码: {process.ExitCode}");
                        Console.WriteLine($"输出: {output}");
                        Console.WriteLine($"错误: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止服务失败: {serviceName}, 错误: {ex.Message}");
            }
        }

        // 修正的注册表写入方法
        private void ExecuteRegWrite(XElement command)
        {
            string key = command.Attribute("Key")?.Value;
            string valueName = command.Attribute("Value")?.Value;
            string type = command.Attribute("Type")?.Value;
            string data = command.Attribute("Data")?.Value;

            Console.WriteLine($"RegWrite参数: Key={key}, Value={valueName}, Type={type}, Data={data}");

            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine("RegWrite: 缺少Key参数");
                return;
            }

            try
            {
                RegistryKey registryKey = GetRegistryKey(key, true);
                if (registryKey != null)
                {
                    RegistryValueKind valueKind = GetRegistryValueKind(type);
                    object valueData = ConvertRegistryDataEnhanced(data, valueKind);

                    if (string.IsNullOrEmpty(valueName) || valueName == "")
                    {
                        registryKey.SetValue("", valueData, valueKind);
                        Console.WriteLine($"RegWrite成功: Key={key}, Value=(默认)");
                    }
                    else
                    {
                        registryKey.SetValue(valueName, valueData, valueKind);
                        Console.WriteLine($"RegWrite成功: Key={key}, Value={valueName}");
                    }

                    registryKey.Close();
                }
                else
                {
                    Console.WriteLine($"无法打开注册表键: {key}");

                    // 如果是HKEY_USERS\DEFAULT且权限不足，尝试使用命令行方式
                    if (key.StartsWith("HKEY_USERS\\DEFAULT", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"尝试使用命令行方式设置HKEY_USERS\\DEFAULT");
                        ExecuteRegistryCommand(key, valueName, type, data, false);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"权限不足，跳过注册表项: {key}, 错误: {ex.Message}");

                // 如果是HKEY_USERS\DEFAULT，尝试使用命令行方式
                if (key.StartsWith("HKEY_USERS\\DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"尝试使用命令行方式设置HKEY_USERS\\DEFAULT");
                    ExecuteRegistryCommand(key, valueName, type, data, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RegWrite失败: {ex.Message}");
            }
        }

        private void ExecuteRegDelete(XElement command)
        {
            string key = command.Attribute("Key")?.Value;
            string valueName = command.Attribute("Value")?.Value;

            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine("RegDelete: 缺少Key参数");
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(valueName))
                {
                    DeleteRegistryKey(key);
                }
                else
                {
                    RegistryKey registryKey = GetRegistryKey(key, true); // 改为可写
                    if (registryKey != null)
                    {
                        // 检查值是否存在
                        string[] valueNames = registryKey.GetValueNames();
                        if (valueNames.Contains(valueName))
                        {
                            registryKey.DeleteValue(valueName, false);
                            Console.WriteLine($"RegDelete成功: Key={key}, Value={valueName}");
                        }
                        else
                        {
                            Console.WriteLine($"RegDelete跳过: Key={key}, Value={valueName} 不存在");
                        }
                        registryKey.Close();
                    }
                    else
                    {
                        Console.WriteLine($"无法打开注册表键: {key}");

                        // 如果是HKEY_USERS\DEFAULT，尝试使用命令行方式
                        if (key.StartsWith("HKEY_USERS\\DEFAULT", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"尝试使用命令行方式删除HKEY_USERS\\DEFAULT");
                            ExecuteRegistryCommand(key, valueName, null, null, true);
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"RegDelete权限不足: Key={key}, Value={valueName}, 错误: {ex.Message}");

                // 如果是HKEY_USERS\DEFAULT，尝试使用命令行方式
                if (key.StartsWith("HKEY_USERS\\DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"尝试使用命令行方式删除HKEY_USERS\\DEFAULT");
                    ExecuteRegistryCommand(key, valueName, null, null, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RegDelete失败: {ex.Message}");
            }
        }

        // 使用reg命令执行注册表操作（需要管理员权限）
        private void ExecuteRegistryCommand(string key, string valueName, string type, string data, bool isDelete)
        {
            try
            {
                string regCommand;

                if (isDelete)
                {
                    // 删除注册表值
                    regCommand = $"/c reg delete \"{ConvertRegistryPath(key)}\" /v \"{valueName}\" /f";
                }
                else
                {
                    // 添加注册表值
                    string regDataType = ConvertToRegType(type);
                    regCommand = $"/c reg add \"{ConvertRegistryPath(key)}\" /v \"{valueName}\" /t {regDataType} /d \"{data}\" /f";
                }

                Console.WriteLine($"执行注册表命令: {regCommand}");

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = regCommand,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true, // 需要为true以请求UAC提升
                    Verb = "runas" // 请求管理员权限
                };

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();
                    process.WaitForExit(10000); // 10秒超时

                    if (process.ExitCode == 0)
                    {
                        Console.WriteLine($"注册表命令执行成功: {regCommand}");
                    }
                    else
                    {
                        Console.WriteLine($"注册表命令执行失败: {regCommand}, 退出代码: {process.ExitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行注册表命令失败: {ex.Message}");
            }
        }

        // 转换注册表路径格式
        private string ConvertRegistryPath(string fullPath)
        {
            return fullPath.Replace("HKEY_CURRENT_USER", "HKCU")
                          .Replace("HKEY_LOCAL_MACHINE", "HKLM")
                          .Replace("HKEY_USERS", "HKU")
                          .Replace("HKEY_CLASSES_ROOT", "HKCR");
        }

        // 转换数据类型
        private string ConvertToRegType(string type)
        {
            switch (type?.ToLower())
            {
                case "reg_dword": return "REG_DWORD";
                case "reg_sz": return "REG_SZ";
                case "reg_binary": return "REG_BINARY";
                case "reg_qword": return "REG_QWORD";
                case "reg_multi_sz": return "REG_MULTI_SZ";
                case "reg_expand_sz": return "REG_EXPAND_SZ";
                default: return "REG_SZ";
            }
        }

        // 修改命令执行逻辑
        private void ProcessXmlCommands(XElement configElement, bool isRestore, string itemTag)
        {
            Console.WriteLine($"=== 处理项目: {itemTag} ===");
            Console.WriteLine($"模式: {(isRestore ? "还原" : "优化")}");

            // 特殊项目处理 - 既执行命令行又执行XML配置
            if (!isRestore)
            {
                // 优化操作
                switch (itemTag)
                {
                    case "10、关闭虚拟化安全性":
                        ExecuteCommandLine("bcdedit /set hypervisorlaunchtype off >nul 2>&1");
                        // 继续执行XML配置
                        break;
                    case "31、禁用保留的存储":
                        ExecuteCommandLine("DISM.exe /Online /Set-ReservedStorageState /State:Disabled >nul 2>&1");
                        // 继续执行XML配置
                        break;
                    default:
                        // 其他项目直接执行XML配置
                        break;
                }
            }
            else
            {
                // 还原操作
                switch (itemTag)
                {
                    case "10、关闭虚拟化安全性":
                        ExecuteCommandLine("bcdedit /set hypervisorlaunchtype auto >nul 2>&1");
                        // 继续执行XML配置
                        break;
                    case "31、禁用保留的存储":
                        ExecuteCommandLine("DISM.exe /Online /Set-ReservedStorageState /State:Enabled >nul 2>&1");
                        // 继续执行XML配置
                        break;
                    default:
                        // 其他项目直接执行XML配置
                        break;
                }
            }

            // 标准项目处理（包括特殊项目的XML配置）
            XElement commandParent = isRestore ?
                configElement.Element("Restore") :
                configElement.Element("Optimize");

            if (commandParent == null)
            {
                Console.WriteLine($"未找到{(isRestore ? "还原" : "优化")}配置");
                return;
            }

            foreach (var command in commandParent.Elements())
            {
                Console.WriteLine($"执行命令: {command.Name.LocalName}");
                ExecuteCommand(command);
                Console.WriteLine($"命令完成: {command.Name.LocalName}");
            }

            Console.WriteLine($"=== 项目完成: {itemTag} ===\n");
        }

        private void ExecuteCommandLine(string command)
        {
            try
            {
                Console.WriteLine($"执行命令行: {command}");

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{command}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit(30000); // 30秒超时

                    if (process.ExitCode == 0)
                    {
                        Console.WriteLine($"命令行执行成功: {command}");
                        if (!string.IsNullOrEmpty(output))
                            Console.WriteLine($"输出: {output}");
                    }
                    else
                    {
                        Console.WriteLine($"命令行执行失败: {command}, 退出代码: {process.ExitCode}");
                        if (!string.IsNullOrEmpty(output))
                            Console.WriteLine($"输出: {output}");
                        if (!string.IsNullOrEmpty(error))
                            Console.WriteLine($"错误: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"执行命令行失败: {command}, 错误: {ex.Message}");
            }
        }

        // 检查进程数量优化状态
        private bool CheckProcessOptimizationStatus()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("SvcHostSplitThresholdInKB");
                        if (value != null)
                        {
                            // 检查是否为 -1 (4294967295)
                            int intValue = Convert.ToInt32(value);
                            bool isOptimized = intValue == -1;
                            Console.WriteLine($"进程数量优化检查: {intValue}, 结果: {isOptimized}");
                            return isOptimized;
                        }
                    }
                }
                Console.WriteLine("进程数量优化检查: 未找到SvcHostSplitThresholdInKB值，认为未优化");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查进程数量优化失败: {ex.Message}");
                return false;
            }
        }

        // 增强的注册表键获取方法
        private RegistryKey GetRegistryKey(string fullPath, bool writable)
        {
            try
            {
                Console.WriteLine($"解析注册表路径: {fullPath}");

                string[] pathParts = fullPath.Split('\\');
                if (pathParts.Length < 2)
                {
                    Console.WriteLine($"无效的注册表路径: {fullPath}");
                    return null;
                }

                // 特殊处理HKEY_USERS\DEFAULT路径
                if (pathParts[0].Equals("HKEY_USERS", StringComparison.OrdinalIgnoreCase) &&
                    pathParts[1].Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    return HandleDefaultUserKey(pathParts, writable);
                }

                RegistryKey baseKey = GetBaseKey(pathParts[0]);
                if (baseKey == null) return null;

                string subKeyPath = string.Join("\\", pathParts, 1, pathParts.Length - 1);

                Console.WriteLine($"打开注册表: {baseKey.Name}\\{subKeyPath}");

                return writable ?
                    baseKey.CreateSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree) :
                    baseKey.OpenSubKey(subKeyPath, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取注册表键失败: {ex.Message}");
                return null;
            }
        }

        // 处理HKEY_USERS\DEFAULT路径
        private RegistryKey HandleDefaultUserKey(string[] pathParts, bool writable)
        {
            try
            {
                // HKEY_USERS\.DEFAULT 对应默认用户配置
                using (RegistryKey usersKey = Registry.Users)
                {
                    string subKeyPath = string.Join("\\", pathParts, 2, pathParts.Length - 2);

                    // 尝试打开.DEFAULT用户配置
                    RegistryKey defaultUserKey = null;
                    try
                    {
                        defaultUserKey = writable ?
                            usersKey.OpenSubKey(".DEFAULT", true) :
                            usersKey.OpenSubKey(".DEFAULT", false);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine("权限不足，无法访问HKEY_USERS\\.DEFAULT");
                        return null;
                    }

                    if (defaultUserKey == null)
                    {
                        Console.WriteLine("无法打开.DEFAULT用户配置");
                        return null;
                    }

                    if (string.IsNullOrEmpty(subKeyPath))
                    {
                        return defaultUserKey;
                    }

                    Console.WriteLine($"打开默认用户注册表: {defaultUserKey.Name}\\{subKeyPath}");

                    try
                    {
                        return writable ?
                            defaultUserKey.CreateSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree) :
                            defaultUserKey.OpenSubKey(subKeyPath, false);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Console.WriteLine($"权限不足，无法访问HKEY_USERS\\.DEFAULT\\{subKeyPath}: {ex.Message}");
                        defaultUserKey.Close();
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理默认用户注册表失败: {ex.Message}");
                return null;
            }
        }

        // 获取注册表根键
        private RegistryKey GetBaseKey(string rootKeyName)
        {
            switch (rootKeyName.ToUpper())
            {
                case "HKEY_CURRENT_USER":
                case "HKCU":
                    return Registry.CurrentUser;
                case "HKEY_LOCAL_MACHINE":
                case "HKLM":
                    return Registry.LocalMachine;
                case "HKEY_CLASSES_ROOT":
                case "HKCR":
                    return Registry.ClassesRoot;
                case "HKEY_USERS":
                    return Registry.Users;
                case "HKEY_CURRENT_CONFIG":
                    return Registry.CurrentConfig;
                default:
                    Console.WriteLine($"不支持的注册表根键: {rootKeyName}");
                    return null;
            }
        }

        // 获取注册表值类型
        private RegistryValueKind GetRegistryValueKind(string type)
        {
            if (string.IsNullOrEmpty(type))
                return RegistryValueKind.String;

            switch (type.ToLower())
            {
                case "string":
                case "reg_sz":
                    return RegistryValueKind.String;
                case "expandstring":
                case "reg_expand_sz":
                    return RegistryValueKind.ExpandString;
                case "binary":
                case "reg_binary":
                    return RegistryValueKind.Binary;
                case "dword":
                case "reg_dword":
                    return RegistryValueKind.DWord;
                case "qword":
                case "reg_qword":
                    return RegistryValueKind.QWord;
                case "multistring":
                case "reg_multi_sz":
                    return RegistryValueKind.MultiString;
                case "none":
                    return RegistryValueKind.None;
                default:
                    return RegistryValueKind.String;
            }
        }

        // 转换注册表数据
        private object ConvertRegistryDataEnhanced(string data, RegistryValueKind valueKind)
        {
            // 处理空值情况
            if (string.IsNullOrEmpty(data))
            {
                return GetDefaultValueForType(valueKind);
            }

            try
            {
                switch (valueKind)
                {
                    case RegistryValueKind.DWord:
                        // 修复：处理大数值（4294967295）
                        if (data == "4294967295")
                        {
                            return -1; // 4294967295 在DWORD中表示为 -1
                        }
                        if (uint.TryParse(data, out uint dwordValue))
                        {
                            // 将uint转换为int（带符号）
                            return unchecked((int)dwordValue);
                        }
                        return Convert.ToInt32(data);

                    case RegistryValueKind.QWord:
                        if (ulong.TryParse(data, out ulong qwordValue))
                        {
                            return unchecked((long)qwordValue);
                        }
                        return Convert.ToInt64(data);

                    case RegistryValueKind.Binary:
                        // 修复二进制数据转换
                        return ConvertBinaryDataEnhanced(data);

                    case RegistryValueKind.MultiString:
                        return data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                    case RegistryValueKind.ExpandString:
                    case RegistryValueKind.String:
                    default:
                        return data;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"数据转换失败: {data} -> {valueKind}, 错误: {ex.Message}");
                return GetDefaultValueForType(valueKind);
            }
        }

        private byte[] ConvertBinaryDataEnhanced(string data)
        {
            try
            {
                // 移除所有空格
                data = data.Replace(" ", "");

                if (string.IsNullOrEmpty(data))
                    return new byte[0];

                // 支持多种格式：
                // "22,22,22,00,00,02,00,00,00,02,00,00,00,00,00,00"
                // "22-22-22-00-00-02-00-00-00-02-00-00-00-00-00-00"
                // "22222200000200000002000000000000"

                if (data.Contains("-"))
                {
                    return data.Split('-')
                              .Where(b => !string.IsNullOrEmpty(b))
                              .Select(b => Convert.ToByte(b, 16))
                              .ToArray();
                }
                else if (data.Contains(","))
                {
                    return data.Split(',')
                              .Where(b => !string.IsNullOrEmpty(b.Trim()))
                              .Select(b => Convert.ToByte(b.Trim(), 16))
                              .ToArray();
                }
                else
                {
                    // 处理连续十六进制字符串
                    // 确保长度为偶数
                    if (data.Length % 2 != 0)
                    {
                        data = "0" + data;
                    }

                    var bytes = new List<byte>();
                    for (int i = 0; i < data.Length; i += 2)
                    {
                        string byteString = data.Substring(i, 2);
                        bytes.Add(Convert.ToByte(byteString, 16));
                    }
                    return bytes.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"二进制数据转换失败: {data}, 错误: {ex.Message}");
                return new byte[0];
            }
        }

        private object GetDefaultValueForType(RegistryValueKind valueKind)
        {
            switch (valueKind)
            {
                case RegistryValueKind.DWord: return 0;
                case RegistryValueKind.QWord: return 0L;
                case RegistryValueKind.Binary: return new byte[0];
                case RegistryValueKind.MultiString: return new string[0];
                case RegistryValueKind.ExpandString:
                case RegistryValueKind.String:
                default: return "";
            }
        }

        // 删除整个注册表键
        private void DeleteRegistryKey(string fullPath)
        {
            try
            {
                string[] pathParts = fullPath.Split('\\');
                if (pathParts.Length < 2) return;

                RegistryKey baseKey = GetBaseKey(pathParts[0]);
                if (baseKey == null) return;

                string subKeyPath = string.Join("\\", pathParts, 1, pathParts.Length - 1);

                Console.WriteLine($"删除注册表键: {fullPath}");

                baseKey.DeleteSubKeyTree(subKeyPath, false);
                Console.WriteLine($"删除注册表键成功: {fullPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除注册表键失败: {ex.Message}");
            }
        }

        // 执行注册表移动命令
        private void ExecuteRegMove(XElement command)
        {
            string key = command.Attribute("Key")?.Value;
            string newKey = command.Attribute("NewKey")?.Value;

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(newKey))
            {
                Console.WriteLine("RegMove: 缺少Key或NewKey参数");
                return;
            }

            Console.WriteLine($"RegMove: {key} -> {newKey}");

            try
            {
                // 实现注册表移动：通过复制和删除的方式
                bool moveSuccess = MoveRegistryKey(key, newKey);

                if (moveSuccess)
                {
                    Console.WriteLine($"RegMove成功: {key} -> {newKey}");
                }
                else
                {
                    Console.WriteLine($"RegMove失败: {key} -> {newKey}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RegMove失败: {ex.Message}");
            }
        }

        private bool MoveRegistryKey(string sourceKey, string destinationKey)
        {
            try
            {
                Console.WriteLine($"开始移动注册表键: {sourceKey} -> {destinationKey}");

                // 1. 首先检查源键是否存在
                RegistryKey sourceRegistryKey = GetRegistryKey(sourceKey, false);
                if (sourceRegistryKey == null)
                {
                    Console.WriteLine($"源注册表键不存在: {sourceKey}");
                    return false;
                }

                // 2. 创建目标键
                RegistryKey destinationRegistryKey = GetRegistryKey(destinationKey, true);
                if (destinationRegistryKey == null)
                {
                    Console.WriteLine($"无法创建目标注册表键: {destinationKey}");
                    return false;
                }

                // 3. 复制所有值
                CopyRegistryValues(sourceRegistryKey, destinationRegistryKey);

                // 4. 复制所有子键
                CopyRegistrySubKeys(sourceRegistryKey, destinationRegistryKey);

                // 5. 关闭注册表键
                sourceRegistryKey.Close();
                destinationRegistryKey.Close();

                // 6. 删除源键
                DeleteRegistryKey(sourceKey);

                Console.WriteLine($"注册表键移动完成: {sourceKey} -> {destinationKey}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"移动注册表键失败: {ex.Message}");
                return false;
            }
        }

        private void CopyRegistryValues(RegistryKey source, RegistryKey destination)
        {
            try
            {
                // 获取源键的所有值名称
                string[] valueNames = source.GetValueNames();

                Console.WriteLine($"复制 {valueNames.Length} 个注册表值");

                foreach (string valueName in valueNames)
                {
                    try
                    {
                        object value = source.GetValue(valueName);
                        RegistryValueKind valueKind = source.GetValueKind(valueName);

                        destination.SetValue(valueName, value, valueKind);

                        Console.WriteLine($"复制值: {valueName} = {value} ({valueKind})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"复制值失败 {valueName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"复制注册表值失败: {ex.Message}");
            }
        }

        private void CopyRegistrySubKeys(RegistryKey source, RegistryKey destination)
        {
            try
            {
                // 获取所有子键名称
                string[] subKeyNames = source.GetSubKeyNames();

                Console.WriteLine($"复制 {subKeyNames.Length} 个子键");

                foreach (string subKeyName in subKeyNames)
                {
                    try
                    {
                        using (RegistryKey sourceSubKey = source.OpenSubKey(subKeyName, false))
                        {
                            if (sourceSubKey != null)
                            {
                                using (RegistryKey destSubKey = destination.CreateSubKey(subKeyName))
                                {
                                    if (destSubKey != null)
                                    {
                                        // 递归复制子键的内容
                                        CopyRegistryValues(sourceSubKey, destSubKey);
                                        CopyRegistrySubKeys(sourceSubKey, destSubKey);

                                        Console.WriteLine($"复制子键: {subKeyName}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"复制子键失败 {subKeyName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"复制注册表子键失败: {ex.Message}");
            }
        }


        // 执行资源管理器通知命令（实际实现）
        private void ExecuteExplorerNotify(XElement command)
        {
            string type = command.Attribute("Type")?.Value;
            string cmd = command.Attribute("Cmd")?.Value;
            string skipError = command.Attribute("SkipError")?.Value;

            try
            {
                if (type?.ToLower() == "cmd" && !string.IsNullOrEmpty(cmd))
                {
                    // 真正执行CMD命令
                    Console.WriteLine($"执行CMD命令: {cmd}");

                    // 使用Process直接执行命令
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "cmd.exe";
                        process.StartInfo.Arguments = $"/c \"{cmd}\"";
                        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        process.StartInfo.CreateNoWindow = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;

                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit(30000);

                        if (process.ExitCode == 0)
                        {
                            Console.WriteLine($"CMD命令执行成功: {cmd}");
                            if (!string.IsNullOrEmpty(output))
                                Console.WriteLine($"输出: {output}");
                        }
                        else
                        {
                            Console.WriteLine($"CMD命令执行失败: {cmd}, 退出代码: {process.ExitCode}");
                            if (!string.IsNullOrEmpty(output))
                                Console.WriteLine($"输出: {output}");
                            if (!string.IsNullOrEmpty(error))
                                Console.WriteLine($"错误: {error}");
                        }
                    }
                }
                else
                {
                    // 原有的资源管理器通知逻辑
                    IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
                    const int WM_SETTINGCHANGE = 0x001A;
                    IntPtr result = IntPtr.Zero;

                    NativeMethods.SendMessageTimeout(
                        HWND_BROADCAST,
                        WM_SETTINGCHANGE,
                        IntPtr.Zero,
                        "Environment",
                        0,
                        1000,
                        out result);

                    Console.WriteLine($"资源管理器通知已发送: {type} - {cmd}");
                }
            }
            catch (Exception ex)
            {
                // 如果设置了SkipError，则忽略特定错误
                if (!string.IsNullOrEmpty(skipError) && skipError == "2")
                {
                    Console.WriteLine($"ExplorerNotify执行失败但已忽略: {ex.Message}");
                }
                else
                {
                    Console.WriteLine($"ExplorerNotify执行失败: {ex.Message}");
                }
            }
        }

        // 添加NativeMethods类
        internal static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            public static extern IntPtr SendMessageTimeout(
                IntPtr hWnd,
                uint Msg,
                IntPtr wParam,
                string lParam,
                uint fuFlags,
                uint uTimeout,
                out IntPtr lpdwResult);
        }

        private void RestartExplorer()
        {
            try
            {
                // 获取当前桌面窗口句柄
                IntPtr desktopHandle = GetDesktopWindow();

                // 结束explorer进程
                foreach (Process process in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch
                    {
                        // 忽略无法终止的进程
                    }
                }

                // 等待一段时间确保进程完全结束
                System.Threading.Thread.Sleep(2000);

                // 启动新的explorer进程
                ProcessStartInfo startInfo = new ProcessStartInfo("explorer.exe");

            }
            catch (Exception ex)
            {
                throw new Exception("重启资源管理器失败: " + ex.Message);
            }
        }

        // 保存配置到Config文件夹
        private void SaveConfigToConfigFolder()
        {
            try
            {
                string ConfigFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

                // 确保Config文件夹存在
                if (!Directory.Exists(ConfigFolder))
                {
                    Directory.CreateDirectory(ConfigFolder);
                }

                string fileName = $"ZyperWin++{DateTime.Now:yyyyMMddHHmmss}.ini";
                string filePath = Path.Combine(ConfigFolder, fileName);

                SaveConfigToFile(filePath);

                label2.Text = $"配置已保存到 {fileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CheckAllOptimizationStatus()
        {
            // 保存用户当前的选择状态
            var userSelections = new Dictionary<string, bool>();
            foreach (var category in tree1.Items)
            {
                foreach (var item in category.Sub)
                {
                    if (item.Tag != null)
                    {
                        userSelections[item.Tag.ToString()] = item.Checked;
                    }
                }
            }

            optimizationStatus.Clear();

            foreach (var category in tree1.Items)
            {
                foreach (var item in category.Sub)
                {
                    if (item.Tag != null)
                    {
                        string itemTag = item.Tag.ToString();
                        bool isOptimized = CheckOptimizationStatus(itemTag);

                        optimizationStatus[itemTag] = isOptimized;

                        string originalText = GetOriginalText(item.Text);

                        if (isOptimized)
                        {
                            if (!item.Text.StartsWith("(已优化)"))
                            {
                                item.Text = "(已优化) " + originalText;
                            }
                            // 已优化的项目保持选中状态
                            item.Checked = true;
                        }
                        else
                        {
                            item.Text = originalText;
                            // 未优化的项目恢复用户之前的选择状态
                            if (userSelections.ContainsKey(itemTag))
                            {
                                item.Checked = userSelections[itemTag];
                            }
                            else
                            {
                                item.Checked = false;
                            }
                        }
                    }
                }
            }

            UpdateCategoryCheckStates();
            ExportSelectedToTree2();
        }

        // 增强的注册表值检查
        private bool CheckRegistryValueEnhanced(string keyPath, string valueName, string expectedValue)
        {
            try
            {
                RegistryKey baseKey = null;
                string actualPath = keyPath;

                // 解析注册表根键
                if (keyPath.StartsWith("HKEY_CURRENT_USER\\"))
                {
                    baseKey = Registry.CurrentUser;
                    actualPath = keyPath.Substring(18);
                }
                else if (keyPath.StartsWith("HKEY_LOCAL_MACHINE\\"))
                {
                    baseKey = Registry.LocalMachine;
                    actualPath = keyPath.Substring(19);
                }
                else if (keyPath.StartsWith("HKEY_USERS\\"))
                {
                    baseKey = Registry.Users;
                    actualPath = keyPath.Substring(11);
                }
                else if (keyPath.StartsWith("HKEY_CLASSES_ROOT\\"))
                {
                    baseKey = Registry.ClassesRoot;
                    actualPath = keyPath.Substring(18);
                }

                if (baseKey != null)
                {
                    using (RegistryKey key = baseKey.OpenSubKey(actualPath))
                    {
                        if (key != null)
                        {
                            object value = key.GetValue(valueName);
                            if (value != null)
                            {
                                // 对于DWORD值，转换为字符串进行比较
                                string currentValue = value.ToString();
                                bool result = currentValue == expectedValue;

                                Console.WriteLine($"注册表检查: {keyPath}\\{valueName} = {currentValue}, 期望: {expectedValue}, 结果: {result}");
                                return result;
                            }
                            else
                            {
                                Console.WriteLine($"注册表值不存在: {keyPath}\\{valueName}");
                                // 如果值不存在，但期望值是"4"（禁用），也认为是已优化
                                if (expectedValue == "4")
                                {
                                    Console.WriteLine($"值不存在但期望禁用，认为是已优化");
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"注册表键不存在: {keyPath}");
                            // 如果键不存在，但期望值是"4"（禁用），也认为是已优化
                            if (expectedValue == "4")
                            {
                                Console.WriteLine($"键不存在但期望禁用，认为是已优化");
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查注册表失败: {ex.Message}");
            }

            return false;
        }

        private string GetOriginalText(string currentText)
        {
            if (currentText.StartsWith("(已优化) "))
            {
                return currentText.Substring("(已优化) ".Length);
            }
            if (currentText.StartsWith("(优化)")) // 处理可能的其他前缀
            {
                return currentText.Substring("(优化)".Length);
            }
            return currentText;
        }

        private bool CheckRegistryKeyExists(string keyPath)
        {
            try
            {
                RegistryKey key = GetRegistryKey(keyPath, false);
                if (key != null)
                {
                    key.Close();
                    return false; // 键存在，说明未优化（需要删除）
                }
                return true; // 键不存在，说明已优化
            }
            catch
            {
                return false;
            }
        }
        // 检查单个优化项目的状态
        private bool CheckOptimizationStatus(string itemTag)
        {
            if (xmlDoc == null) return false;

            var itemElement = xmlDoc.Descendants("Item")
                .FirstOrDefault(e => e.Attribute("name")?.Value == itemTag);
            return ReliableOptimizationExecutor.VerifyItem(itemElement, itemTag, out _);
        }

        private bool CheckAutoChkTimeout()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AutoChkTimeOut");
                        if (value != null)
                        {
                            // 检查是否为5秒（值为5）
                            int timeout = Convert.ToInt32(value);
                            bool isOptimized = timeout == 5;
                            Console.WriteLine($"磁盘错误检查等待时间: {timeout}秒, 结果: {isOptimized}");
                            return isOptimized;
                        }
                    }
                }
                Console.WriteLine("磁盘错误检查等待时间: 未找到AutoChkTimeOut值，认为未优化");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查磁盘错误检查等待时间失败: {ex.Message}");
                return false;
            }
        }

        private bool CheckServiceStatus(string serviceName, string expectedStartType)
        {
            try
            {
                string serviceKeyPath = $@"SYSTEM\CurrentControlSet\services\{serviceName}";

                using (RegistryKey serviceKey = Registry.LocalMachine.OpenSubKey(serviceKeyPath))
                {
                    if (serviceKey != null)
                    {
                        object startValue = serviceKey.GetValue("Start");
                        if (startValue != null)
                        {
                            int currentStartType = Convert.ToInt32(startValue);
                            int expectedType = Convert.ToInt32(expectedStartType);
                            bool isOptimized = currentStartType == expectedType;

                            Console.WriteLine($"服务状态检查: {serviceName} = {currentStartType}, 期望: {expectedType}, 结果: {isOptimized}");
                            return isOptimized;
                        }
                    }
                }
                Console.WriteLine($"服务状态检查: {serviceName} 未找到Start值");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查服务状态失败 {serviceName}: {ex.Message}");
                return false;
            }
        }

        // 更新分类节点的选中状态
        private void UpdateCategoryCheckStates()
        {
            foreach (var category in tree1.Items)
            {
                bool allChecked = true;
                bool anyChecked = false;

                foreach (var item in category.Sub)
                {
                    if (item.Checked)
                        anyChecked = true;
                    else
                        allChecked = false;
                }

                // 设置分类节点的选中状态
                if (allChecked && category.Sub.Count > 0)
                    category.Checked = true;
                else if (anyChecked)
                    category.Checked = true; // 或者保持 indeterminate 状态
                else
                    category.Checked = false;
            }
        }

        // 优化或还原完成后刷新状态
        private async Task PerformOptimizationWithProgress(bool isRestore)
        {
            Console.WriteLine($"开始执行{(isRestore ? "还原" : "优化")}操作...");

            if (xmlDoc == null)
            {
                throw new Exception("配置文件未加载");
            }

            // 收集所有选中的项目
            var selectedItems = new List<(string category, string itemTag, bool alreadyOptimized)>();
            foreach (var category in tree1.Items)
            {
                foreach (var item in category.Sub)
                {
                    if (item.Checked && item.Tag != null)
                    {
                        string itemTag = item.Tag.ToString();
                        bool isAlreadyOptimized = optimizationStatus.ContainsKey(itemTag) && optimizationStatus[itemTag];

                        // 还原操作不管是否优化过都执行
                        // 优化操作跳过已优化的项目
                        if (!isRestore && isAlreadyOptimized)
                        {
                            Console.WriteLine($"跳过已优化的项目: {itemTag}");
                            continue;
                        }

                        selectedItems.Add((category.Text, itemTag, isAlreadyOptimized));
                    }
                }
            }

            if (selectedItems.Count == 0)
            {
                if (!isRestore)
                {
                    MessageBox.Show("所有选中的项目都已经优化过了，无需重复操作。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("没有找到需要还原的项目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (isRestore)
            {
                IDictionary<string, DateTime> snapshotTimes = ReliableOptimizationExecutor.LatestSnapshotTimes();
                selectedItems = selectedItems
                    .OrderByDescending(value => snapshotTimes.TryGetValue(value.itemTag, out DateTime createdAt) ? createdAt : DateTime.MinValue)
                    .ToList();
            }

            try
            {
                // ... 进度显示代码 ...

                int processedCount = 0;

                // 使用异步方式逐步执行
                for (int i = 0; i < selectedItems.Count; i++)
                {
                    var (categoryName, itemTag, alreadyOptimized) = selectedItems[i];
                    processedCount++;

                    // 在UI线程上更新状态显示
                    this.Invoke(new Action(() => {
                        int progressPercentage = (int)((double)processedCount / selectedItems.Count * 100);
                        string statusText = $"{(isRestore ? "还原" : "优化")}中... {processedCount}/{selectedItems.Count} ({progressPercentage}%)";
                        label2.Text = statusText;
                        this.Refresh();
                        Application.DoEvents();
                    }));

                    Console.WriteLine($"处理项目: {itemTag} ({processedCount}/{selectedItems.Count})");

                    // 查找XML配置
                    var configElement = xmlDoc.Descendants("Item")
                        .FirstOrDefault(e => e.Attribute("name")?.Value == itemTag);

                    if (configElement != null)
                    {
                        Console.WriteLine($"找到XML配置，开始{(isRestore ? "还原" : "优化")}...");
                        OptimizationExecutionResult result = isRestore
                            ? ReliableOptimizationExecutor.RestoreLatest(itemTag)
                            : ReliableOptimizationExecutor.Apply(configElement, itemTag);
                        if (!result.Success) throw new InvalidOperationException(itemTag + "：" + result.Message);
                        Console.WriteLine($"项目 {itemTag} 处理完成");
                    }
                    else
                    {
                        throw new InvalidDataException("未找到系统优化规则：" + itemTag);
                    }

                    // 添加延迟，让用户能看到进度变化
                    await Task.Delay(100);
                }

                // ... 完成处理 ...
            }
            catch (Exception ex)
            {
                Console.WriteLine($"操作过程中出现错误: {ex.Message}");
                throw;
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            MessageBox.Show("该扩展入口未包含在当前项目中。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void button10_Click(object sender, EventArgs e)
        {
            MessageBox.Show("该扩展入口未包含在当前项目中。", "C DiskGlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
