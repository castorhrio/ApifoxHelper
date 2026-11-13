namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「手工填写说明」这一屏竖直方向的一份预算（不依赖 WinForms，与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>（第二百五十一轮，wine 96 档实测）：这一屏此前窗体高写死 560×400，
/// 而它要同时放五块——位置行、说明行、说明输入区（填满剩余）、状态行、按钮行。
/// 实测量到：底部两块 状态行 <b>33px</b> + 按钮行 <b>64px</b>，顶部两行 位置行 <b>20px</b> +
/// 说明行 <b>46px</b>，内容网格上下内边距 <b>24px</b>，输入区宿主 <b>213px</b>——
/// 扣掉输入框自己那一圈与宿主镀铬之后，<b>装得下 10 行</b>中文说明。
/// </para>
/// <para>
/// <b>这一屏的主内容是输入区，不是清单</b>：用户在这里手写一段中文说明（方法用途、参数含义、
/// 响应字段含义），一次写三五句是常态。所以这一屏的容量口径是「输入区装得下几行」，
/// 与 <see cref="EndpointTraceLayout"/> 那一屏的「清单装得下几行」<b>不是同一个口径，也不能跨屏抄</b>。
/// </para>
/// <para>
/// <b>这一处的口径</b>：竖直方向不再按「窗体给多少像素」写，而是按<b>「输入区要给几行看」</b>给，
/// 像素由同一份行高与同一份非行高开销算出；窗体高由「固定四块 + 网格内边距 + 输入区」推出来，
/// 不再是一个独立字面量。改行数，窗体高跟着动。
/// </para>
/// <para>
/// <b>这一处判到什么、不判什么</b>：判得到「输入区给到几行」；判不到真机上这几行的观感、
/// 也判不到窗口被屏幕尺寸截断时这几块怎么退让（wine 125% / 200% 档那一跑窗体是被屏幕截过的，
/// 读数只记不判）——那一些随 G-D5 真机复核。
/// </para>
/// <para>
/// <b>这一文件只管竖直轴</b>：这一屏的宽度（<see cref="DialogWidth"/>）不在这里给，
/// 窗体宽仍是 <c>ManualDraftDialog</c> 就地写的 560。
/// </para>
/// </summary>
public static class ManualDraftLayout
{
    /// <summary>
    /// 输入区要给看的行数 = <b>10</b>。判据：这一屏的主内容就是这个多行输入区，
    /// 装不下就等于「写说明时看不见自己写了什么」。冒烟段 <c>RunManualDraftShown</c> 按这个数判
    /// 「这一刻装得下几行」。
    /// <para>
    /// <b>10 是实测读数，不是新定的目标</b>：这一屏此前窗体高写死 400，那一跑量到的容量正好是 10 行。
    /// 本轮把「10 行」从「400 减出来的余数」变成「算式的一个输入」，窗体高随之由算式给出（仍是 400——
    /// 这次换算式不改变窗口大小，改的是「那个数从哪来」）。
    /// 要更多行就改这个数，窗体高跟着长；不要回头去改 <see cref="DialogHeight"/> 那个读数。
    /// </para>
    /// </summary>
    public const int MainRows = 10;

    /// <summary>96 档实测的输入框行高（<c>TextBox</c> 一行文字占的高）。判据：行数都按这一个数换算。</summary>
    public const int LineHeight = 21;

    /// <summary>
    /// 输入框自己那一圈 = <b>1px</b>（实测：输入框可视高 211px，10 行 × 21px = 210px，差这 1px）。
    /// <b>它必须单独记一笔</b>：「行数 × 行高」与「输入框多高」之间差的正是这一笔，
    /// 把它并进 <see cref="FieldBoxPadding"/> 会让那个数同时表示两件事，下一轮改镀铬时没人知道该动多少。
    /// </summary>
    public const int TextBoxPadding = 1;

    /// <summary>
    /// 宿主镀铬比输入框多出来的那一截 = <b>2px</b>（实测：输入区宿主 213px 对输入框 211px，
    /// 即 <c>Ui.Field</c> 那层 1px 描边的上下各 1px）。判据：窗体高按宿主算，行数按输入框算，
    /// 两者之间差的就是这一笔。
    /// </summary>
    public const int FieldBoxPadding = 2;

    // ---------- 固定四块（第二百五十一轮具名）----------

    /// <summary>
    /// 顶部位置行（「方法名（summary）」那一句）的高 = <b>20px</b>（96 档实测，<c>AutoSize</c>，
    /// 代码里没有一处能「设」它——它由字体与内容决定）。
    /// </summary>
    public const int PositionRowHeight = 20;

    /// <summary>
    /// 顶部说明行（「纯文本说明，不用 XML 标签…」那一句）的高 = <b>46px</b>（96 档实测，<c>AutoSize</c>）。
    /// <para>
    /// <b>它跟着文字长度与窗体宽走</b>：这一句在 560px 宽的可用区里折成两行，
    /// 所以 46 = 两行文字 + 上下外边距。改文案或改窗体宽，它都会变——
    /// 变了要回来重量并回写这个常量，别让算式里的窗体高悄悄不等于真实排版。
    /// </para>
    /// </summary>
    public const int GuidanceRowHeight = 46;

    /// <summary>
    /// 底部状态行的高 = <b>33px</b>（96 档实测：状态文字 29px + 上下留白 4px）。
    /// <b>它是 <c>AutoSize</c>，本文件给的是读数不是设定值</b>。
    /// </summary>
    public const int StatusRowHeight = 33;

    /// <summary>
    /// 底部按钮行（「添加为候选」+「取消」）的高 = <b>64px</b>（96 档实测）。<b>它是 <c>AutoSize</c>，
    /// 本文件给的是读数不是设定值</b>（按钮行的高度由按钮高 + 上下内边距决定，代码里没有一处能「设」它）。
    /// </summary>
    public const int ButtonRowHeight = 64;

    /// <summary>
    /// 输入区之外那四块固定区合计 = <b>163px</b>（96 档实测：位置行 20 + 说明行 46 + 状态行 33 + 按钮行 64）。
    /// <para>
    /// <b>它是四块的和，不是一个独立读数</b>：改任一块常量，这个和与窗体高一起动；
    /// 冒烟段再把<b>实测的四块</b>与这个和逐字对账，两边任一方单独改都被对面问住。
    /// </para>
    /// </summary>
    public const int FixedPanesHeight =
        PositionRowHeight + GuidanceRowHeight + StatusRowHeight + ButtonRowHeight;

    /// <summary>
    /// 内容网格自己的上下内边距 = <b>24px</b>（<c>Padding = new Padding(Xl, Lg, Xl, Sm)</c>，
    /// 纵向即 <c>Lg</c> 16 + <c>Sm</c> 8）。<b>它单独记一笔</b>：它不是一块屏，是内容区自己的一圈留白，
    /// 折进 <see cref="FixedPanesHeight"/> 会让那个数同时表示「四块屏」与「一圈留白」两件事。
    /// </summary>
    public const int GridPaddingHeight = 24;

    /// <summary>输入区要给 <paramref name="rows"/> 行时，输入框本体占的高：<c>行数 × 行高 + 输入框自己那一圈</c>。</summary>
    public static int InputTextHeight(int rows) => rows * LineHeight + TextBoxPadding;

    /// <summary>输入区要给 <paramref name="rows"/> 行时，输入区宿主占的高（宿主镀铬比输入框多 <see cref="FieldBoxPadding"/>）。</summary>
    public static int InputAreaHeight(int rows) => InputTextHeight(rows) + FieldBoxPadding;

    /// <summary>
    /// 这一屏的窗体高：<c>固定四块 + 内容网格内边距 + 输入区（按行数算）</c>。
    /// 实测口径（10 行）：163 + 24 + 210 + 1 + 2 = 400。
    /// <para>
    /// <b>为什么窗体高不许再写字面量</b>（第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测
    /// 那一屏、第二百四十八轮在差异检查结果那一屏、第二百五十轮在接口追踪明细那一屏上量到的同一课）：
    /// 字面量与「放不放得下」之间没有算式绑住，改行数或改任一块开销，两边不会一起动，
    /// 而门在原来那一点上照样全绿。行数收成参数，为的是「改行数窗体高跟不跟着变」这句话
    /// 能在不动产品常量的前提下判。
    /// </para>
    /// </summary>
    /// <param name="rows">输入区要给看的行数。</param>
    public static int DialogHeightFor(int rows) =>
        FixedPanesHeight + GridPaddingHeight + InputAreaHeight(rows);

    /// <summary>这一屏的窗体宽 = <b>560</b>（96 档设计宽，本轮未动）。</summary>
    public const int DialogWidth = 560;

    /// <summary>这一屏的窗体高。<b>值由 <see cref="DialogHeightFor(int)"/> 给</b>（400）。</summary>
    public static readonly int DialogHeight = DialogHeightFor(MainRows);
}
