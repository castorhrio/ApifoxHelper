using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 对话框的 DPI 缩放（DESIGN.md §6「DPI 适配」）。
/// <para>
/// <b>为什么要有这个文件</b>：全部对话框的 <c>ClientSize</c> 是固定的 96dpi 逻辑像素，
/// 而窗体没有 <c>AutoScaleDimensions</c>——WinForms 的 <c>AutoScaleMode.Font</c> 在
/// <c>AutoScaleDimensions</c> 为空时**什么都不做**（缩放因子 = 当前字体度量 / 设计基准，
/// 基准为空即不缩放）。于是系统把字体按缩放放大、对话框宽度一动不动：wine 实测 ≈200% 档
/// 说明行文字 1021px 挤进 988px 的可用宽度（第一百六十二轮量到），按钮、列宽、字段网格
/// 同一根因。这不是文案太长，是窗体不随缩放放大。
/// </para>
/// <para>
/// <b>为什么不用 <c>AutoScaleMode.Font</c> + <c>AutoScaleDimensions</c></b>（第一百六十三轮
/// 先按这条路做过，wine 实测否掉）：那条路在 wine 下**自我抵消**。实测（wine 10.0、
/// <c>LogPixels=192</c>）：设 <c>AutoScaleDimensions</c> 时窗体按 2 倍放大到 1608×957，
/// 但**建句柄那一刻 WinForms 又按 0.5 倍缩回去**（804×453）——因为
/// <c>CurrentAutoScaleDimensions</c> 在句柄创建前后量到的不是同一个数：建句柄前取屏幕 DC
/// （192dpi，量到 16×34），建句柄后取窗口 DC（wine 的窗口 DC 恒报 96dpi，量到 8×17），
/// 缩放因子 = 当前 / 基准 于是从 2 变成 0.5。净效果等于没缩放，200% 档照旧放不下。
/// 这不是「wine 的 DeviceDpi 恒报 96」那一条（第一百六十二轮已记），是**窗口 DC 与屏幕 DC
/// 报不同 DPI**，任何单一 <c>AutoScaleDimensions</c> 值都无法同时满足前后两次量测。
/// </para>
/// <para>
/// <b>所以怎么做</b>：缩放因子自己算（屏幕 DC 的 <c>GetDeviceCaps(LOGPIXELSY) / 96</c>，
/// 即系统字体缩放比），缩放动作仍交给框架的 <c>Control.Scale</c>——那正是 WinForms 自动缩放
/// 内部用的同一个原语（<c>PerformAutoScale</c> → <c>ScaleControl</c>），只是由我们决定
/// 「什么时候、按几倍」：<c>AutoScaleMode = None</c> 让框架不再自己动手，构造末尾按因子
/// 显式缩放一次。这样 wine 与真机走同一条路、同一个数，200% 档在 wine 下可验证。
/// </para>
/// <para>
/// <b>为什么 <c>Scale</c> 不缩放字体是对的</b>：DPI 翻倍时字号（磅）不变、像素尺寸自然翻倍，
/// 要放大的是**布局**（边距、控件边界、列宽）。实测 <c>Scale(2,2)</c> 后窗体客户区
/// 1020×740 → 2040×1480（被屏幕最大窗口尺寸截到 1604×953），标签位置 16,16 → 32,32，
/// 而字体仍是 9pt / 29px——正是要的效果。
/// </para>
/// <para>
/// <b>量不到怎么办</b>：屏幕 DPI 取不到时因子按 1 处理（= 不缩放），与修复前同一行为，
/// 不猜一个数把布局撑坏。
/// </para>
/// </summary>
internal static class DpiScale
{
    /// <summary>设计基准 DPI：DESIGN.md §3 的字号档与 §4 的间距档都按 96dpi 逻辑像素记录。</summary>
    private const float DesignDpi = 96f;

    /// <summary><c>GetDeviceCaps</c> 的 <c>LOGPIXELSY</c>：屏幕 DC 的纵向 DPI。</summary>
    private const int LogPixelsY = 90;

    /// <summary>
    /// 给对话框装上 DPI 缩放：设计尺寸按 96dpi 逻辑像素给，窗体随系统字体缩放等比放大。
    /// <para>
    /// 字体与缩放基准取同一份（<see cref="UiTokens.Body"/>）：窗体字体必须与设计基准一致，
    /// 否则 100% 档就会凭空缩放。故本方法一并把窗体字体设成它，调用点不必再自己保证两处一致。
    /// </para>
    /// </summary>
    public static void Apply(Form form, Size designSize)
    {
        form.Font = UiTokens.Body.Value;
        // 关掉框架自己的自动缩放：见类注释——wine 下它按窗口 DC 重算基准，会把刚放大的窗体缩回去。
        // 缩放由本方法末尾显式做一次，wine 与真机因此走同一条路。
        form.AutoScaleMode = AutoScaleMode.None;
        form.ClientSize = designSize;
        var factor = ScaleFactor();
        if (factor > 1f) form.Scale(new SizeF(factor, factor));
    }

    /// <summary>
    /// 系统字体缩放比 = 屏幕 DPI / 96。取不到 DPI 时回 1（不缩放）。
    /// <para>
    /// 不用 <c>Control.DeviceDpi</c>：wine 下它恒报 96，而字体确实按 <c>LogPixels</c> 放大
    /// （第一百六十二轮实测），拿它当缩放比会让因子恒为 1。
    /// </para>
    /// </summary>
    public static float ScaleFactor()
    {
        var dpi = ScreenDpi();
        return dpi <= 0 ? 1f : dpi / DesignDpi;
    }

    /// <summary>
    /// 屏幕 DC 的纵向 DPI（<c>GetDeviceCaps(LOGPIXELSY)</c>）。取不到时回 0。
    /// </summary>
    public static int ScreenDpi()
    {
        using var screen = Graphics.FromHwnd(IntPtr.Zero);
        var hdc = screen.GetHdc();
        try
        {
            return GetDeviceCaps(hdc, LogPixelsY);
        }
        finally
        {
            screen.ReleaseHdc(hdc);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);
}
