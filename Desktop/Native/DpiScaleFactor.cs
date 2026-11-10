namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 缩放因子的算式（DESIGN.md §6「DPI 适配」）。不依赖 WinForms，与测试工程共享编译。
/// <para>
/// <b>为什么单独一份</b>：因子本身是纯算术（屏幕 DPI ÷ 设计基准），而它的调用点
/// <see cref="DpiScale"/> 满是 GDI 句柄与窗体属性，在 Linux 上编译不进来、也就没有一条断言能钉住它。
/// 第一百六十二轮量到的 ≈200% 档溢出，根因之一是「窗体不随缩放放大」；把算式与缩放动作分开之后，
/// 算式这一半在 Linux 有真值表，动作那一半由 <c>DpiScaleTests</c> 的行首正则钉住接线。
/// </para>
/// <para>
/// <b>量不到怎么办</b>：DPI 取不到（0 或负）时因子按 1 处理 = 不缩放，与修复前同一行为。
/// 不猜一个数把布局撑坏——猜错的因子会同时放大所有边距与列宽，比不缩放更难诊断。
/// </para>
/// </summary>
internal static class DpiScaleFactor
{
    /// <summary>设计基准 DPI：DESIGN.md §3 的字号档与 §4 的间距档都按 96dpi 逻辑像素记录。</summary>
    public const float DesignDpi = 96f;

    /// <summary>系统字体缩放比 = 屏幕 DPI / 96。96→1、120→1.25、192→2；量不到 DPI 时回 1。</summary>
    public static float FromScreenDpi(int dpi) => dpi <= 0 ? 1f : dpi / DesignDpi;

    /// <summary>
    /// 要不要真的执行一次缩放：只在放大（因子 &gt; 1）时。
    /// <para>
    /// 因子为 1 时 <c>Control.Scale</c> 在数学上是恒等，但它仍会走一遍整棵子树的布局重算。
    /// 跳过它，「量不到 DPI 就与修复前同一行为」这句话才是真的成立，而不是只是看起来一样。
    /// </para>
    /// </summary>
    public static bool ShouldScale(float factor) => factor > 1f;
}
