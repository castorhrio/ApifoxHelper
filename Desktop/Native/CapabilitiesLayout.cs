namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「连接与写入探测」这一屏竖直方向的一份预算（不依赖 WinForms，与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>（第二百四十七轮，wine 96 档实测）：这一屏此前窗体高写死 780×620，
/// 而它要同时放四块——连接检查（四行徽标）、写入兼容探测（三个输入 + 确认 + 动作行 + 两段说明）、
/// 结果区（填满剩余）、状态行。把前三块按产品自己的停靠方式量出来：连接检查 <b>309px</b>、
/// 探测区 <b>196px</b>、状态行 <b>33px</b>，合计 538px；620 减掉这 538 只剩 <b>82px</b> 给结果区，
/// 结果清单本体 <b>39px</b>——<b>装得下 1 行</b>。而服务层 <c>ProbeAsync</c> 一次探测登记
/// <b>7 条</b>检查（读取测试接口 / 分支隔离 / 原样写回说明 / 临时修改说明 / 恢复说明 /
/// 整段写回参数请求体响应 / 导出概览与详情的引用一致），跑完七项要滚动才看得全。
/// 这一屏存在的理由正是「写入前先探测、逐项看结果」，而那一行一行在真实窗口里看不见。
/// </para>
/// <para>
/// <b>此前为什么量不到</b>：冒烟器每一段都只「建句柄 + <c>CreateControl</c> + <c>PerformLayout</c>」，
/// 从不显示窗体；而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。
/// 第二百四十六轮把这件事登记成表（<see cref="MeasuredScreens"/>），这一屏标的是「未量」；
/// 本轮补上 <c>RunCapabilitiesShown</c> 真的 <c>Show()</c> 出来，第一次量到上面那三个数。
/// </para>
/// <para>
/// <b>这一处的口径</b>：竖直方向不再按「窗体给多少像素」写，而是按<b>「结果区要给几行看」</b>给，
/// 像素由同一份行高与同一份非行高开销算出；窗体高由「固定三块 + 结果区」推出来，不再是一个独立字面量。
/// 这样「挤没了结果区」这件事在算式里当场可见，改行数或改任一块开销，窗体高跟着动。
/// 行高与开销都是本轮实测（清单行高 21px；结果区开销 43px 由「结果区 82 对清单本体 39」反推，
/// 即说明行 + 内边距 + 发丝边），不是设计稿里的数——设计稿只说「放得下」。
/// </para>
/// <para>
/// <b>这一处判到什么、不判什么</b>：判得到「结果区给到几行」；判不到真机上这些行的观感、
/// 也判不到窗口被屏幕尺寸截断时这几块怎么退让（wine 125% / 200% 档那一跑窗体是被屏幕截过的，
/// 读数只记不判，与列宽口径同一取舍）——那一些随 G-D5 真机复核。
/// </para>
/// <para>
/// <b>这一文件只管竖直轴</b>（与 <see cref="ListColumnWidths"/> 各管一半）：这一屏的列宽
/// （检查项 200 / 结果 70 / 详情 440）全部在 <c>CapabilitiesDialog</c> 就地声明、由
/// <c>ListColumnWidthsTests</c> 判，这里一个宽度数都不给。
/// </para>
/// </summary>
public static class CapabilitiesLayout
{
    /// <summary>
    /// 结果区要给看的行数 = 服务层一次探测登记的检查条数（<c>Sync/CompatService.ProbeAsync</c> 成功路径上
    /// 那七处 <c>Check</c>）。<b>这个数不许在这里单独维护</b>：<c>CapabilitiesLayoutTests</c> 拿
    /// <c>ProbeAsync</c> 源码里出现的检查名两侧对账，服务层多一条检查而这里没跟，门当场红。
    /// </summary>
    public const int ProbeRows = 7;

    /// <summary>96 档实测行高（<c>ListViewItem.Bounds.Height</c>）。判据：行数都按这一个数换算。</summary>
    public const int RowHeight = 21;

    /// <summary>
    /// 结果区的非行高开销：说明行（「尚未运行写入探测…」那一行）+ 内边距 + 发丝边 = <b>43px</b>
    /// （本轮实测：结果区 82px 对清单本体 39px）。探测跑完之后说明行换成结论行，高度同档，开销不变。
    /// </summary>
    public const int ResultPaneChrome = 43;

    /// <summary>
    /// 清单表头占的高 = <b>24px</b>（本轮实测：清单可视高 147px 时第 7 行底边 171px，171 − 7 × 21 = 24）。
    /// <b>它必须单独记一笔</b>：第一版把结果区按「7 × 21 + 43」给到 190px，清单本体正好 147px，
    /// 而滚动条照样出现——因为表头先吃掉 24px，留给行的只有 123px（5 行）。
    /// 「行数 × 行高」与「装得下」之间差的正是这一笔，把它并进 <see cref="ResultPaneChrome"/> 会让
    /// 那个数同时表示两件事（说明行 + 表头），下一轮改说明行时没人知道该动多少。
    /// </summary>
    public const int ColumnHeaderHeight = 24;

    /// <summary>
    /// 结果区之外那三块固定区合计：连接检查 309 + 写入兼容探测 196 + 状态行 33 = <b>538px</b>（96 档实测）。
    /// 这三块都是 <c>AutoSize</c>，高度由内容与字号决定，所以这里记的是<b>读数</b>而不是算式；
    /// 内容一长它就漂，漂了由冒烟段「四块停靠区高度加起来等于客户区」当场点名。
    /// </summary>
    public const int FixedPanesHeight = 538;

    /// <summary>结果区要给 <paramref name="rows"/> 行时占的高：<c>行数 × 行高 + 表头 + 说明行与内边距</c>。</summary>
    public static int ResultPaneHeight(int rows) => rows * RowHeight + ColumnHeaderHeight + ResultPaneChrome;

    /// <summary>
    /// 这一屏的窗体高：<c>固定三块 + 结果区（按行数算）</c>。实测口径：538 + 7 × 21 + 24 + 43 = 752。
    /// <para>
    /// <b>为什么窗体高不许再写字面量</b>（第二百四十四轮在同步预览那一屏上量到的同一课）：
    /// 字面量与「放不放得下」之间没有算式绑住，改行数或改任一块开销，两边不会一起动，
    /// 而门在原来那一点上照样全绿。行数收成参数，为的是「改行数窗体高跟不跟着变」这句话
    /// 能在不动产品常量的前提下判。
    /// </para>
    /// </summary>
    public static int DialogHeightFor(int rows) => FixedPanesHeight + ResultPaneHeight(rows);

    /// <summary>这一屏的窗体高（宽不变，780）。<b>值由 <see cref="DialogHeightFor(int)"/> 给</b>。</summary>
    public static readonly int DialogHeight = DialogHeightFor(ProbeRows);
}
