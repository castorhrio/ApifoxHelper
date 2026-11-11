namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 提示对话框里「复制路径」的口径（不依赖 WinForms、与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>：提示对话框（<see cref="NoticeDialog"/>）的等宽行装的是路径——
/// 「关于」里的资料目录、打开目录失败时那句「可以在「文件」菜单里重试，或<b>手动打开上面的路径</b>」。
/// 而那一行是 <c>Label</c>：不可选中、不可复制，说明行指着的下一步（把路径拿去资源管理器里打开、
/// 或贴给同事）在界面上没有落点，用户只能照着屏幕一个字一个字敲。列表那一侧的文字出口在第五十二轮
/// 由 Ctrl+C 补上，状态行那一侧由「查看完整错误」补上，提示对话框这一侧是同一族里剩下的最后一处。
/// </para>
/// <para>
/// <b>为什么是按钮而不是把标签换成可选中文本框</b>：标签换成只读文本框要改这一屏的布局
/// （等宽行从自动折行的标签变成固定高度的框），而本环境没有 Windows 图形子系统、改完量不到；
/// 按钮挂在既有的按钮行里（右对齐流式布局，与「关闭」同一行），布局风险最小，且键盘可达（Tab 到它、空格按下）。
/// </para>
/// <para>
/// <b>三个标签字数相同（4/4/4）</b>：按钮是 <c>AutoSize</c> 的，文字一变宽度就变，右对齐行里
/// 「关闭」会跟着左右跳——复制成功/失败是终态，不该让旁边的按钮挪位。三个标签取同一字数，
/// 宽度因此不变（与 §6「停止/危险：位置尺寸与常态一致，防按钮跳位」同一取舍）。
/// </para>
/// </summary>
public static class NoticeCopy
{
    /// <summary>复制按钮的文字（全仓唯一落点：界面不就地拼写）。</summary>
    public const string Label = "复制路径";

    /// <summary>复制成功后的按钮文字：说结果，不说机制。</summary>
    public const string CopiedLabel = "复制成功";

    /// <summary>复制失败后的按钮文字：剪贴板被其他进程占用时如实说没复制成，不谎称已复制。</summary>
    public const string FailedLabel = "复制失败";

    /// <summary>
    /// 这一次要交给剪贴板的文字：调用方给的路径原样返回（不裁剪、不改写——粘出去的就是屏幕上那一行），
    /// 没有给、或给的是空白时返回 <c>null</c>，此时<b>不出现复制按钮</b>：
    /// 一颗点了什么也不会发生的按钮，与 §7「禁用不得移除下一步」同侧——入口要么指向用户拿得到的东西，
    /// 要么不该出现（与 <c>StatusLineText</c>「放得下就不给『查看完整错误』」同一判据）。
    /// </summary>
    public static string? Target(string? copyText) =>
        string.IsNullOrWhiteSpace(copyText) ? null : copyText;
}
