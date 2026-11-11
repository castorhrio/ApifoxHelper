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
/// <b>量不到怎么办</b>：分两个时刻，口径不同。
/// <b>创建时</b>（<see cref="FromScreenDpi"/> 吃屏幕 DC）：DPI 取不到（0 或负）时因子按 1 处理 = 不缩放，
/// 与修复前同一行为；不猜一个数把布局撑坏——猜错的因子会同时放大所有边距与列宽，比不缩放更难诊断。
/// <b>变更时</b>（第一百七十五轮起，<see cref="IsUsableDpi"/> 挡在门口）：取不到就<b>整次重算都不做</b>，
/// 而不是回落到 1——变更事件里的「读不出 DPI」与创建时的「系统本来就是 96」是两件事，
/// 把前者当成后者，等于把一屏已经放大的布局缩回设计尺寸，那是把一种不确定性换成另一种更明显的破坏。
/// </para>
/// <para>
/// <b>变更期为什么用比值而不是目标因子</b>（<see cref="ScaleStep"/>）：<c>Control.Scale</c> 乘的是
/// <b>此刻</b>的控件边界，而此刻那份布局已经是按 <c>applied</c> 放大过的。直接乘 <c>target</c> 会让
/// 「100% → 200% → 100% → 200%」四次之后停在 4 倍上；乘 <c>target / applied</c> 才落在同一条布局轨道上。
/// 客户区尺寸另走 <see cref="ScaledExtent"/> 的<b>绝对</b>口径，理由见 <c>DpiScale</c> 的变更处理。
/// </para>
/// </summary>
internal static class DpiScaleFactor
{
    /// <summary>设计基准 DPI：DESIGN.md §3 的字号档与 §4 的间距档都按 96dpi 逻辑像素记录。</summary>
    public const float DesignDpi = 96f;

    /// <summary>
    /// 因子差到此之内不算「缩放变了」：真实档位（1 / 1.25 / 1.5 / 1.75 / 2 / 2.5）之间的最小间隔远大于它，
    /// 而一次缩放要重排整棵子树——不该为渲染看不出的差付这笔钱。
    /// </summary>
    public const float FactorTolerance = 0.001f;

    /// <summary>系统字体缩放比 = 屏幕 DPI / 96。96→1、120→1.25、192→2；量不到 DPI 时回 1。</summary>
    public static float FromScreenDpi(int dpi) => dpi <= 0 ? 1f : dpi / DesignDpi;

    /// <summary>
    /// 这个 DPI 读数能不能驱动一次重算：只有正数才算量到了（DPI 变更消息带来的新 DPI 就填在这里）。
    /// </summary>
    public static bool IsUsableDpi(int dpi) => dpi > 0;

    /// <summary>
    /// 从 DPI 变更消息的 <c>wParam</c> 取<b>新</b> DPI：高位字是窗口进入的那块屏的 DPI，
    /// 低位字是它离开的那块屏的 DPI（两个数在「运行中改系统缩放」那一种情况下相同）。
    /// <para>
    /// <b>为什么读消息而不读 <c>DeviceDpi</c> / <c>GetDpiForWindow</c></b>：第一百七十五轮在 wine 里
    /// 把这条消息发给一个真实窗体，窗体过程收到的 <c>wParam</c> 是 12583008（高 192、低 96），
    /// 而同一次里 <c>DeviceDpi</c> 与 <c>GetDpiForWindow</c> 都回 96、框架事件的参数也回 96——
    /// 消息里那个数才是「要去的那一块屏」，屏上的数在本机（与第一百六十二、一百六十三轮同一）会滞后。
    /// 取错一侧的后果是不重算（因子算回 1），不是算错，但那条挂账就永远闭不掉。
    /// </para>
    /// </summary>
    public static int NewDpiFromWParam(long wParam) => (int)(unchecked((uint)wParam) >> 16);

    /// <summary>
    /// 要不要真的执行一次缩放：只在放大（因子 &gt; 1）时。
    /// <para>
    /// 因子为 1 时 <c>Control.Scale</c> 在数学上是恒等，但它仍会走一遍整棵子树的布局重算。
    /// 跳过它，「量不到 DPI 就与修复前同一行为」这句话才是真的成立，而不是只是看起来一样。
    /// </para>
    /// </summary>
    public static bool ShouldScale(float factor) => factor > 1f;

    /// <summary>
    /// 要不要跟着新 DPI 重算：<b>与已经应用过的那一份比</b>，不同才动。
    /// <para>
    /// 与 <see cref="ShouldScale"/> 的区别是这里<b>允许缩小</b>：200% 拖回 100% 时必须把已经放开的
    /// 边距与列宽收回去。「只放大不缩小」是创建期的口径（那时候缩小只可能来自一个读不到的 DPI，不可信）；
    /// 变更期的每一步都有明确的来与去，收回去是恢复，不是猜。
    /// </para>
    /// </summary>
    public static bool ShouldRescale(float appliedFactor, float targetFactor) =>
        Math.Abs(targetFactor - appliedFactor) > FactorTolerance;

    /// <summary>相对缩放步长：把「按 applied 放大过的那一份布局」换到 target，乘的是比值不是目标因子。</summary>
    public static float ScaleStep(float appliedFactor, float targetFactor) => targetFactor / appliedFactor;

    /// <summary>
    /// 按因子取客户区的一条边长。远离零取整：<c>(int)</c> 的截断会让每一档都系统性偏小半个像素，
    /// 而这一屏的判据正是「放得下 / 放不下」，差的那半像素落在错误一侧时，读数就与真机不一致。
    /// </summary>
    public static int ScaledExtent(int designExtent, float factor) =>
        (int)Math.Round(designExtent * factor, MidpointRounding.AwayFromZero);
}
