namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 原生壳键盘快捷键的口径（方案 2.1 原生工作流要素「键盘快捷键」的落地；设计验收 D12「快捷键可用」）。
/// 手势只取 Windows 惯例：Ctrl+数字切换页面（浏览器与 IDE 的页签惯例）、F5 刷新当前页（与两页
/// 已有的「刷新」按钮同一口径）、F1 打开帮助（本工具帮助菜单唯一入口是「关于」）、Ctrl+F 聚焦
/// 当前页列表的筛选框（Windows 的「在列表中查找」惯例，落在两页工具栏的筛选框上——列表变长后
/// 不必先用鼠标点进框）。不给「退出」与「打开资料目录/日志目录」发明手势：退出沿用系统 Alt+F4，
/// 目录项保留菜单助记符（Alt+F 后 D/L）。
/// 手势以可读字符串表达、由壳经 KeysConverter 接到菜单 ShortcutKeys（模态对话框打开期间主窗体
/// 菜单不路由，壳级快捷键不会穿透对话框）；动作以稳定 id 表达、由壳分发到既有入口，本文件不执行动作。
/// </summary>
public static class ShellShortcuts
{
    /// <summary>快捷键条目：Action 是壳内动作 id，Gesture 是绑定与菜单显示的手势文字。</summary>
    public sealed record Entry(string Action, string Gesture);

    public static readonly Entry Projects = new("projects", "Ctrl+1");
    public static readonly Entry Tasks = new("tasks", "Ctrl+2");
    public static readonly Entry Refresh = new("refresh", "F5");
    public static readonly Entry Find = new("find", "Ctrl+F");
    public static readonly Entry About = new("about", "F1");

    /// <summary>壳级快捷键全集；新增快捷键必须在此登记，界面代码不就地写手势。</summary>
    public static readonly IReadOnlyList<Entry> All = new[] { Projects, Tasks, Refresh, Find, About };

    public static string? GestureOf(string action) =>
        All.FirstOrDefault(entry => entry.Action == action)?.Gesture;

    /// <summary>壳级快捷键不得占用的编辑键：文本框等控件的常规操作，壳不能抢。</summary>
    public static readonly IReadOnlyList<string> ReservedEditingKeys = new[]
        { "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+A", "Ctrl+Z", "Ctrl+Y" };
}
