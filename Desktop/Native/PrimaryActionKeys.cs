namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 对话框「主动作」的键盘入口（Ctrl+Enter）口径（不依赖 WinForms、与测试工程单一编译）。
/// <para>
/// <b>补的是哪个缺口</b>：三个审阅对话框（注释候选 / 响应契约 / 差异确认）的主动作是
/// 「接受并下一条」「确认」「确认纳入」——逐条清队列时这一个动作要按几十次。它今天只有两条路：
/// 鼠标点，或 Tab 到那颗按钮再按空格/回车。而这三个对话框的 <c>AcceptButton</c> 是「关闭」
/// （<c>Enter</c> 在对话框里是「确认/关闭」的既有语义，DESIGN.md §6「菜单/快捷键」那一行），
/// 于是「键盘上按一下就走下一条」这件事在整只壳里<b>没有入口</b>：审阅五十条候选要按五十次 Tab。
/// </para>
/// <para>
/// <b>为什么是 Ctrl+Enter</b>：Windows 上「对话框的主动作」在 <c>Enter</c> 已被占用时用
/// <c>Ctrl+Enter</c>（Outlook 发送、资源管理器属性页确定都是这一个手势）。它不与本仓已有的任何手势
/// 相撞——<c>Enter</c> 归对话框的确认/关闭（<see cref="ListKeys.IsEnterGesture"/> 只管列表行与筛选框）、
/// <c>Ctrl+C</c> 归列表复制（<see cref="ListCopy.IsCopyGesture"/>）、<c>Ctrl+F</c> 归查找
/// （<see cref="ShellShortcuts.IsFindGesture"/>）、<c>Ctrl+A</c> 归复选清单全选——四只手势各自只认自己的修饰键组合。
/// </para>
/// <para>
/// <b>只判定，不执行</b>：认键与「这一次该不该动作」在这里，点按钮由 <see cref="DpiAwareForm"/> 一处承担
/// （<c>ProcessCmdKey</c>），三个对话框只登记「本屏的主动作是哪一颗按钮」，不各写一套按键判定。
/// </para>
/// </summary>
public static class PrimaryActionKeys
{
    /// <summary>
    /// 裸 Ctrl+Enter 判定：调用方已确认按下的是 Enter，这里只看修饰键。只认 Ctrl——带 Shift/Alt 的组合
    /// 不属本口径、留给控件自身，不吞键也不给既有组合键发明新语义（与 <see cref="ListCopy.IsCopyGesture"/>、
    /// <see cref="ListKeys.IsEnterGesture"/>、<see cref="ShellShortcuts.IsFindGesture"/> 同一纪律）。
    /// </summary>
    public static bool IsPrimaryActionGesture(bool shift, bool control, bool alt) => control && !shift && !alt;

    /// <summary>
    /// 这一次按键该不该真的点下去：手势成立、按钮可用、按钮在屏幕上，三条都成立才动作。
    /// <para>
    /// <b>不可用时不吞键</b>（返回 false，交回基类）：主动作在「没有选中行」「读取失败」「作业运行中」
    /// 这些态下是禁用的，此时按 Ctrl+Enter 什么都不该发生——但也不该把这一次按键吃掉，
    /// 那会把「什么都没发生」说成做了什么（与 <see cref="Ui.TryFocusFilter"/> 对停用框的取舍同一纪律）。
    /// </para>
    /// <para>
    /// <b>不可见时同样不动作</b>：读取失败态里主动作按钮是藏起来的（那一屏的唯一入口是「重试」），
    /// 藏着的按钮被点一下等于替用户做了一件屏幕上没有的事。
    /// </para>
    /// </summary>
    public static bool ShouldRun(bool gesture, bool enabled, bool visible) => gesture && enabled && visible;
}
