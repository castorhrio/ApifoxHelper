using System.Drawing;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 列表行形态的口径（docs/DESIGN.md §6「列表行」）：选中行底 Bg + 左侧 3px Accent 标记，
/// 不用系统高亮蓝；逐子项设置的语义色（Muted/Ok/Warn/Bad 的「颜色+文字」双通道）保留，
/// 未设置语义色的文字一律正文色。这里只做判定，绘制由 Ui.List 工厂在 WinForms 侧执行；
/// 颜色以 System.Drawing.Color 的结构相等比较，本文件不依赖 WinForms，与测试工程共享编译。
/// </summary>
public static class ListRows
{
    /// <summary>选中行左侧强调色标记宽度（DESIGN.md §6「左侧 3px Accent 标记」）。</summary>
    public const int MarkerWidth = 3;

    /// <summary>
    /// 子项文字色：子项或所在项显式设置过语义色时保留（状态双通道不能因自绘丢失）；
    /// 未设置的（含沿控件回退出的正文色）一律正文色。子项色优先于所在项色。
    /// </summary>
    public static Color TextColor(Color subItemColor, Color itemColor, Color bodyColor, IReadOnlySet<Color> semanticColors)
    {
        if (semanticColors.Contains(subItemColor)) return subItemColor;
        if (semanticColors.Contains(itemColor)) return itemColor;
        return bodyColor;
    }
}
