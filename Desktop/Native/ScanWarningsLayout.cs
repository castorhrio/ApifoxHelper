namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「扫描警告」这一屏竖直方向的一份预算（不依赖 WinForms，与测试工程共享编译）。
/// <para>
/// <b>为什么要有这个文件</b>（第二百五十二轮，wine 96 档实测）：这一屏此前窗体高写死 720×480，
/// 而它要同时放三块——说明行（这一屏在讲「哪些位置被降级处理了」）、警告清单（填满剩余）、
/// 按钮行。把三块按产品自己的停靠方式量出来：说明行 <b>25px</b>（标签 17 + 下外边距 8）、
/// 按钮行 <b>60px</b>，合计 <b>85px</b>；内容网格自己的上下内边距 <b>24px</b>；
/// 警告清单宿主 <b>365px</b>，清单本体 <b>355px</b>——扣掉宿主镀铬 10px 之后
/// <b>装得下 16 行</b>中文警告文本。
/// </para>
/// <para>
/// <b>这一屏的主内容是那份降级清单</b>：用户在这里看的是「哪些位置被降级处理了、为什么」，
/// 装不下就等于「降级原因看不见」。所以这一屏的容量口径是<b>「警告清单装得下几行」</b>，
/// 与 <see cref="ManualDraftLayout"/> 那一屏的「输入区装得下几行」<b>不是同一个口径</b>：
/// 那一屏是<b>输入</b>（用户手写说明），这一屏是<b>只读预览</b>（降级条目）。
/// 也与 <see cref="EndpointTraceLayout"/> 那一屏的「接口清单装得下几行」不同——那一屏的清单带表头，
/// 行高是 <c>ListViewItem.Bounds.Height</c>；这一屏是纯文本，行高是 <c>TextBox</c> 的一行文字高。
/// </para>
/// <para>
/// <b>这一处的口径</b>：竖直方向不再按「窗体给多少像素」写，而是按<b>「警告清单要给几行看」</b>给，
/// 像素由同一份行高与同一份非行高开销算出；窗体高由「固定两块 + 网格内边距 + 宿主外边距 +
/// 清单（按行数算）+ 宿主镀铬」推出来，不再是一个独立字面量。改行数，窗体高跟着动。
/// </para>
/// <para>
/// <b>这一处判到什么、不判什么</b>：判得到「警告清单给到几行」；判不到真机上这几行的观感、
/// 也判不到窗口被屏幕尺寸截断时这几块怎么退让（wine 125% / 200% 档那一跑窗体是被屏幕截过的，
/// 读数只记不判）——那一些随 G-D5 真机复核。
/// </para>
/// <para>
/// <b>这一文件只管竖直轴</b>：这一屏的宽度（<see cref="DialogWidth"/>）不在这里给，
/// 窗体宽仍是 <c>ScanWarningsDialog</c> 就地写的 720。
/// </para>
/// </summary>
public static class ScanWarningsLayout
{
    /// <summary>
    /// 警告清单要给看的行数 = <b>16</b>。判据：这一屏的主内容就是那份降级清单
    /// （扫描警告 / 未解析符号 / 外部类型三类条目），装不下就等于「降级原因看不见」。
    /// 冒烟段 <c>RunScanWarningsShown</c> 按这个数判「这一刻装得下几行」。
    /// <para>
    /// <b>16 是实测读数，不是新定的目标</b>：改算式之前那一屏（窗体高 480）量到的容量是
    /// 「清单本体 355px ÷ 行高 21px = 16.9 行」，<b>整行只有 16 行</b>，余下 19px 是减出来的零头。
    /// 本轮把「16 行」变成「算式的一个输入」，窗体高随之由 480 变成 <b>461</b>
    /// （那 19px 零头不是预算——与第二百五十轮接口追踪明细那一屏「640 → 639」同一取舍：
    /// 少的那几像素是余数，不是预算）。要更多行就改这个数，窗体高跟着长；
    /// 不要回头去改 <see cref="DialogHeight"/> 那个读数。
    /// </para>
    /// </summary>
    public const int MainRows = 16;

    /// <summary>
    /// 96 档实测行高（<c>TextBox</c> 一行文字占的高）。判据：行数都按这一个数换算。
    /// <para>
    /// <b>它与 <see cref="ManualDraftLayout.LineHeight"/> 是同一个口径</b>（两处都是
    /// <c>TextBox</c> 的一行文字高，同在 96 档量到 21px），但<b>与
    /// <see cref="EndpointTraceLayout.RowHeight"/> 数值相同、口径不同</b>——那一处是
    /// <c>ListViewItem.Bounds.Height</c>（带表头的清单行）。真机复核时三个数要分别量，
    /// 不要因为数值相等就认为可以跨屏复用。
    /// </para>
    /// </summary>
    public const int LineHeight = 21;

    /// <summary>
    /// 文本框自己那一圈 = <b>0px</b>（实测：只读预览框走 <c>Ui.Hairline</c>，
    /// <c>BorderStyle</c> 已被去掉，框高 355px 与可视高 355px 逐字相等）。
    /// <para>
    /// <b>它必须单独记一笔</b>：<see cref="ManualDraftLayout.TextBoxPadding"/> 那一处是 <b>1px</b>
    /// （那边是带边框的输入框），两处数值不同不是抄错——一个是输入框、一个是只读预览框。
    /// 把它并进 <see cref="ListHostChrome"/> 会让那个数同时表示两件事，
    /// 下一轮改镀铬时没人知道该动多少。
    /// </para>
    /// </summary>
    public const int TextBoxPadding = 0;

    /// <summary>
    /// 警告清单宿主比清单本体多出来的那一截 = <b>10px</b>
    /// （实测：宿主 365px 对清单 355px；即宿主自己的下内边距 8px + 发丝边 2px）。
    /// 判据：窗体高按宿主算，行数按清单本体算，两者之间差的就是这一笔。
    /// <para>
    /// <b>它与 <see cref="EndpointTraceLayout.ListHostChrome"/> 数值相同（都是 10px）但来历不同</b>：
    /// 那一处是「下内边距 8px + 发丝边 2px」，这一处也是「下内边距 8px + 发丝边 2px」——
    /// 形状一样纯属同用一套镀铬（<c>Ui.Hairline</c>）的巧合，两处仍要分别量、分别记。
    /// </para>
    /// </summary>
    public const int ListHostChrome = 10;

    /// <summary>
    /// 警告清单宿主在内容网格里的上下外边距 = <b>6px</b>
    /// （实测：网格第 2 行 371px 对宿主自身高 365px；差的是 WinForms 给表格单元控件的默认
    /// <c>Margin(3,3,3,3)</c>）。<b>它是框架默认值，代码里没有一处能设它</b>。
    /// <para>
    /// <b>它必须单独记一笔</b>：漏掉它，窗体高会少 6px，警告清单少装得下约四分之一行——
    /// 而算式那一条照样绿（两边一起少）。改这一屏的网格边距时，红的是常量不是算式。
    /// </para>
    /// </summary>
    public const int ListHostMargin = 6;

    // ---------- 固定两块（第二百五十二轮具名）----------

    /// <summary>
    /// 顶部说明行（「扫描中对这些位置降级处理：…」那一句）的高 = <b>25px</b>
    /// （96 档实测：标签本体 17px + 它自己的下外边距 8px）。
    /// <b>它是 <c>AutoSize</c>，本文件给的是读数不是设定值</b>——这一行的高度由字体与内容决定。
    /// <para>
    /// <b>它跟着文字长度与窗体宽走</b>：这一句在 720px 宽的可用区里是单行，
    /// 改文案或改窗体宽，它都会变——变了要回来重量并回写这个常量，
    /// 别让算式里的窗体高悄悄不等于真实排版。
    /// </para>
    /// </summary>
    public const int GuidanceRowHeight = 25;

    /// <summary>
    /// 底部按钮行（「关闭」）的高 = <b>60px</b>（96 档实测）。
    /// <b>它是 <c>AutoSize</c>，本文件给的是读数不是设定值</b>
    /// （按钮行的高度由按钮高 + 上下内边距决定，代码里没有一处能「设」它）。
    /// </summary>
    public const int ButtonRowHeight = 60;

    /// <summary>
    /// 警告清单之外那两块固定区合计 = <b>85px</b>（96 档实测：说明行 25 + 按钮行 60）。
    /// <para>
    /// <b>它是两块的和，不是一个独立读数</b>：改任一块常量，这个和与窗体高一起动；
    /// 冒烟段再把<b>实测的两块</b>与这个和逐字对账，两边任一方单独改都被对面问住。
    /// </para>
    /// </summary>
    public const int FixedPanesHeight = GuidanceRowHeight + ButtonRowHeight;

    /// <summary>
    /// 内容网格自己的上下内边距 = <b>24px</b>（<c>Padding = new Padding(Xl, Lg, Xl, Sm)</c>，
    /// 纵向即 <c>Lg</c> 16 + <c>Sm</c> 8）。<b>它单独记一笔</b>：它不是一块屏，是内容区自己的一圈留白，
    /// 折进 <see cref="FixedPanesHeight"/> 会让那个数同时表示「两块屏」与「一圈留白」两件事。
    /// </summary>
    public const int GridPaddingHeight = 24;

    /// <summary>警告清单要给 <paramref name="rows"/> 行时，文本框本体占的高：<c>行数 × 行高 + 文本框自己那一圈</c>。</summary>
    public static int TextHeight(int rows) => rows * LineHeight + TextBoxPadding;

    /// <summary>警告清单要给 <paramref name="rows"/> 行时，警告清单宿主占的高（宿主比文本框多 <see cref="ListHostChrome"/>）。</summary>
    public static int ListHostHeight(int rows) => TextHeight(rows) + ListHostChrome;

    /// <summary>
    /// 这一屏的窗体高：<c>固定两块 + 内容网格内边距 + 宿主外边距 + 警告清单（按行数算）</c>。
    /// 实测口径（16 行）：85 + 24 + 6 + 336 + 10 = 461。
    /// <para>
    /// <b>为什么窗体高不许再写字面量</b>（第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测
    /// 那一屏、第二百四十八轮在差异检查结果那一屏、第二百五十轮在接口追踪明细那一屏、
    /// 第二百五十一轮在手工填写说明那一屏上量到的同一课）：字面量与「放不放得下」之间没有算式绑住，
    /// 改行数或改任一块开销，两边不会一起动，而门在原来那一点上照样全绿。行数收成参数，为的是
    /// 「改行数窗体高跟不跟着变」这句话能在不动产品常量的前提下判。
    /// </para>
    /// </summary>
    /// <param name="rows">警告清单要给看的行数。</param>
    public static int DialogHeightFor(int rows) =>
        FixedPanesHeight + GridPaddingHeight + ListHostMargin + ListHostHeight(rows);

    /// <summary>这一屏的窗体宽 = <b>720</b>（96 档设计宽，本轮未动）。</summary>
    public const int DialogWidth = 720;

    /// <summary>这一屏的窗体高。<b>值由 <see cref="DialogHeightFor(int)"/> 给</b>（461）。</summary>
    public static readonly int DialogHeight = DialogHeightFor(MainRows);
}
