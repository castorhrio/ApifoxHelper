namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 两个主页面列表的按键口径（不依赖 WinForms、与测试工程单一编译）。补的是 Windows 列表里
/// 本就存在的两个手势：在筛选框输入完按 Enter 走到筛选结果的第一行（搜索框到结果的跳转——
/// Ctrl+F 把焦点交给框，Enter 把选中交给行），以及在列表行上按 Enter 打开该行（资源管理器
/// Enter 打开选中项，与双击同一动作）。
/// 本文件只判定按键与目标行，不执行动作、不触碰控件：动作分别由各页的 FocusFirstResult 与
/// Ui.ActivateItem 承担。对话框的 Enter 保持「确认」既有语义（DESIGN.md §6「菜单/快捷键」行），
/// 不走这里。
/// </summary>
public static class ListKeys
{
    /// <summary>
    /// 裸 Enter 判定：调用方已确认按下的是 Enter，这里只看修饰键。带 Ctrl/Alt/Shift 的组合
    /// 不属于本口径、留给控件自身——不吞键，也不给既有组合键发明新语义。
    /// </summary>
    public static bool IsEnterGesture(bool shift, bool control, bool alt) => !shift && !control && !alt;

    /// <summary>
    /// 筛选框按 Enter 后焦点与选中的去向：显示序第一行，即筛选与排序之后的可见顺序（恒为 0），
    /// 不是数据源的第一行。零行返回 null——筛到没有匹配时空态的下一步是「清空筛选」按钮，
    /// 不把焦点丢进空列表。
    /// </summary>
    public static int? FirstResultIndex(int displayedCount) => displayedCount > 0 ? 0 : null;
}
