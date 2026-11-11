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
    /// 台账上的「已经应用到哪一档」能不能参与比值。只有正数才行。
    /// <para>
    /// <b>为什么这一条也要判</b>：<see cref="ScaleStep"/> 是除法（目标 ÷ 已应用）。台账上若是 0，
    /// 步长算出无穷大，<c>Control.Scale</c> 拿到一个非有限数——那不是「缩放没生效」，是整棵子树的
    /// 边界变成 NaN 之后再也回不来，且<b>不会有任何一条既有断言报红</b>（真值表只喂正数）。
    /// 台账由 <see cref="DpiScale.Apply"/> 写下，正常路径只可能是正数；这一条挡的是「有人写了个 0 进来」
    /// 那一类，而不是在猜一个真实的档位。
    /// </para>
    /// </summary>
    public static bool IsUsableFactor(float factor) => factor > 0f;

    /// <summary>
    /// 一次 DPI 变更要落的三种结果：控件树的缩放步长、客户区的绝对尺寸、以及台账要推进到的那一档。
    /// 四个数一起给，是因为它们在 <see cref="DpiScale.RescaleOnDpiChanged"/> 里必须同进同出——
    /// 只给出其中一个、另外几个在窗体里另算，就是这一族最初那两处漂移的由来。
    /// </summary>
    /// <param name="Step">控件树的相对缩放步长（<see cref="ScaleStep"/>）。</param>
    /// <param name="ClientWidth">客户区宽度（设计宽 × 目标因子，<see cref="ScaledExtent"/>）。</param>
    /// <param name="ClientHeight">客户区高度（同上）。</param>
    /// <param name="AppliedFactor">这次要推进到的目标因子。</param>
    /// <remarks>
    /// 写成引用型 record 而不是 <c>record struct</c>：<see cref="PlanRescale"/> 用 <c>null</c> 表示
    /// 「这一条消息不动布局」，而 <c>struct?</c> 在调用侧要写 <c>plan.Value.Step</c> 才能取到字段——
    /// 那一层 <c>.Value</c> 正是「有人顺手在窗体里再算一遍」的入口。引用型 record 让
    /// <c>if (plan is null) return;</c> 之后直接 <c>plan.Step</c>，窗体那一侧只剩三个动作。
    /// 每次 DPI 变更只分配一个对象，与一次整棵控件树重排相比可以忽略。
    /// </remarks>
    public sealed record DpiRescalePlan(
        float Step,
        int ClientWidth,
        int ClientHeight,
        float AppliedFactor);

    /// <summary>
    /// 一次 DPI 变更的<b>全部判断与全部算术</b>：该不该重算、按几倍缩放控件树、客户区多大、
    /// 台账推进到哪一档。回 <c>null</c> = 这一条消息不动任何布局。
    /// <para>
    /// <b>为什么这些判断要在一处</b>：第一百六十二轮的溢出与第一百七十五轮那两档挂账，缺的从来不是
    /// 「会不会算」，而是<b>每一处调用都自己判一遍</b>——四处判断各写一份时，改一处漏一处不会有东西报红，
    /// 而漏掉的那一处在真机上表现为「拖到副屏没重算」这种没人量得到的样子。抽到这里之后，
    /// 窗体那一侧只剩三个动作（缩放控件树、设客户区、推进台账），判断只有一份，真值表在 Linux 上跑的就是它。
    /// </para>
    /// <para>
    /// <b>五类输入都回得出来</b>（真值表见 <c>ApifoxHelper.Tests/DpiScaleTests.cs</c>）：
    /// 新 DPI 高于当前 / 低于当前 / 等于当前 / DPI 为 0 或负数 / 台账因子为 0 或负数。
    /// 后两类与「等于当前」一律回 <c>null</c>——<b>不动布局</b>，也不抛异常。这条「不抛」是判据本体：
    /// 变更消息是系统发过来的，处理里抛出去等于把一次缩放变成一次未处理异常。
    /// </para>
    /// </summary>
    public static DpiRescalePlan? PlanRescale(int newDpi, float appliedFactor, int designWidth, int designHeight)
    {
        if (!IsUsableDpi(newDpi)) return null;
        if (!IsUsableFactor(appliedFactor)) return null;
        var target = FromScreenDpi(newDpi);
        if (!ShouldRescale(appliedFactor, target)) return null;
        return new DpiRescalePlan(
            ScaleStep(appliedFactor, target),
            ScaledExtent(designWidth, target),
            ScaledExtent(designHeight, target),
            target);
    }

    /// <summary>
    /// 按因子取客户区的一条边长。远离零取整：<c>(int)</c> 的截断会让每一档都系统性偏小半个像素，
    /// 而这一屏的判据正是「放得下 / 放不下」，差的那半像素落在错误一侧时，读数就与真机不一致。
    /// </summary>
    public static int ScaledExtent(int designExtent, float factor) =>
        (int)Math.Round(designExtent * factor, MidpointRounding.AwayFromZero);
}
