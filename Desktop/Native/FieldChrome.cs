using System.Drawing;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 输入框描边的口径（docs/DESIGN.md §6「输入框」）：常态 1px Line 发丝边，聚焦描边 Accent。
/// 这里只做判定，描边由 Ui.Field 工厂的面板在 WinForms 侧绘制；
/// 颜色以 System.Drawing.Color 的结构相等比较，本文件不依赖 WinForms，与测试工程共享编译。
/// </summary>
public static class FieldChrome
{
    /// <summary>输入框描边色：聚焦 Accent，常态 Line（校验失败描边 BadText 待字段定位接入，见 DESIGN.md §8）。</summary>
    public static Color Border(bool focused, Color line, Color accent) => focused ? accent : line;
}
