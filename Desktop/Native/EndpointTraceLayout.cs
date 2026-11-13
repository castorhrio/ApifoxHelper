namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「接口追踪明细」这一屏竖直方向的一份预算（不依赖 WinForms，与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>（第二百五十轮，wine 96 档实测）：这一屏此前窗体高写死 920×640，
/// 而它要同时放五块——顶部两行（标题行 + 筛选行）、接口清单（填满剩余）、选中接口的追踪明细、
/// 按钮行。把四块固定区按产品自己的停靠方式量出来：顶部 <b>43 + 31 = 74px</b>（标题行 + 筛选行）、
/// 底部 <b>219 + 60 = 279px</b>（追踪明细 + 按钮行），合计 <b>353px</b>；640 减掉这 353 只剩
/// <b>287px</b> 给接口清单宿主，清单本体 <b>277px</b>——扣掉表头 24px 之后<b>装得下 12 行</b>。
/// 改算式之后窗体高 639，实跑量到的是宿主 <b>286px</b>、清单本体 <b>276px</b>，同样装得下 12 行
/// （少的那 1px 是原来 640 减出来的余数，不是预算）。
/// 固定区占了整屏的 <b>55%</b>，是三张「未量」屏里最高的一屏（另两屏：手工填写说明 97/400 = 24%、
/// 扫描警告 60/480 = 12.5%），主内容（接口清单）只剩 43%。
/// </para>
/// <para>
/// <b>为什么挑这一屏</b>（简报 P0-1 的「先量再挑」）：三屏都量过之后，按「主内容要给几行」排，
/// 这一屏 12 行、手工填写说明 10 行、扫描警告 16 行——<b>没有一屏掉到个位数</b>，所以判据不能照抄
/// 「只剩个位数行」那一句（简报也写了「以实跑为准，不硬套」）。挑它的理由是另外三条，都指向同一屏：
/// ① 固定区占屏比最高（55%），主内容被挤得最狠；② 它是三屏里唯一有<b>就地写死的定高</b>（追踪明细
/// 那个 190px 的文本框）与<b>裸窗体高</b>（640）的一屏，另两屏的固定区高度全是从内容派生的；
/// ③ 它的主内容是一张带表头的清单，与 <see cref="CheckResultsLayout"/> 同一形状，收口方式能逐条对上。
/// </para>
/// <para>
/// <b>此前为什么量不到</b>：冒烟器每一段都只「建句柄 + <c>CreateControl</c> + <c>PerformLayout</c>」，
/// 从不显示窗体；而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。
/// 第二百四十六轮把这件事登记成表（<see cref="MeasuredScreens"/>），这一屏标的是「未量」；
/// 第二百五十轮补上 <c>RunEndpointTraceShown</c> 真的 <c>Show()</c> 出来，第一次量到上面那几个数。
/// </para>
/// <para>
/// <b>这一处的口径</b>：竖直方向不再按「窗体给多少像素」写，而是按<b>「接口清单要给几行看」</b>给，
/// 像素由同一份行高与同一份非行高开销算出；窗体高由「固定四块 + 接口清单 + 宿主多出来的那一截」推出来，
/// 不再是一个独立字面量。这样「挤没了接口清单」这件事在算式里当场可见，改行数或改任一块开销，
/// 窗体高跟着动。行高与开销都是实测（清单行高 21px、表头 24px、清单宿主比清单本体多 10px），
/// 不是设计稿里的数——设计稿只说「放得下」。
/// </para>
/// <para>
/// <b>这一处判到什么、不判什么</b>：判得到「接口清单给到几行」；判不到真机上这些行的观感、
/// 也判不到窗口被屏幕尺寸截断时这几块怎么退让（wine 125% / 200% 档那一跑窗体是被屏幕截过的，
/// 读数只记不判，与列宽口径同一取舍）——那一些随 G-D5 真机复核。
/// </para>
/// <para>
/// <b>这一文件只管竖直轴</b>（与 <see cref="ListColumnWidths"/> 各管一半）：这一屏的列宽
/// （接口 280 / 读取方法 90 / 未解析调用 96 / 外部调用 88 / 预算 64）全部在 <c>EndpointTraceDialog</c>
/// 就地声明，这里一个宽度数都不给。
/// </para>
/// </summary>
public static class EndpointTraceLayout
{
    /// <summary>
    /// 接口清单要给看的行数 = <b>12</b>。判据：这一屏的主内容就是这张清单（真实项目上几十上百个接口），
    /// 装不下就等于「追踪明细看不见」。冒烟段 <c>RunEndpointTraceShown</c> 按这个数判「这一刻装得下几行」。
    /// <para>
    /// <b>12 是实测读数，不是新定的目标</b>：改算式之前那一屏（窗体高 640）量到的容量正好是 12 行，
    /// 本轮把「12 行」从「640 减出来的余数」变成「算式的一个输入」，窗体高随之从 640 变成 639
    /// （12 × 21 + 24 + 10 + 353 = 639，比原来少 1px——原来那 1px 是余数，不是预算）。
    /// 要更多行就改这个数，窗体高跟着长；不要回头去改 <see cref="DialogHeight"/> 那个读数。
    /// </para>
    /// </summary>
    public const int MainRows = 12;

    /// <summary>96 档实测行高（<c>ListViewItem.Bounds.Height</c>）。判据：行数都按这一个数换算。</summary>
    public const int RowHeight = 21;

    /// <summary>
    /// 清单表头占的高 = <b>24px</b>（实测：清单可视高 276px 时第 16 行底边 360px，360 − 16 × 21 = 24；
    /// 改之前窗体高 640 那一跑是「可视高 277px、第 12 行底边 276px」，276 − 12 × 21 同样是 24）。
    /// <b>它必须单独记一笔</b>：「行数 × 行高」与「装得下」之间差的正是这一笔，
    /// 把它并进 <see cref="ListHostChrome"/> 会让那个数同时表示两件事（表头 + 内边距），
    /// 下一轮改内边距时没人知道该动多少。
    /// </summary>
    public const int ColumnHeaderHeight = 24;

    /// <summary>
    /// 接口清单宿主比清单本体多出来的那一截 = <b>10px</b>（实测：宿主 286px 对清单 276px；
    /// 即下内边距 8px + 发丝边 2px）。判据：窗体高按宿主算，行数按清单本体算，两者之间差的就是这一笔。
    /// </summary>
    public const int ListHostChrome = 10;

    // ---------- 固定四块：顶部两行 + 底部两块（第二百五十轮具名，简报 P0-1）----------

    /// <summary>
    /// 顶部标题行（「调用链追踪按方法预算收手」那句说明 + 计数行）的高 = <b>43px</b>（96 档实测）。
    /// <b>它是 <c>AutoSize</c>，本文件给的是读数不是设定值</b>——这一行的高度由字体与内容决定，
    /// 代码里没有一处能「设」它。收成常量是为了让 <see cref="FixedPanesHeight"/> 有一个可加的加数，
    /// 而不是让算式里出现一个来历不明的 353。
    /// <para>
    /// <b>43 与 31 这两个数按内容认名，不按控件顺序认</b>：两块都是 <c>TableLayoutPanel</c>、
    /// 都停靠在顶部，按类型与顺序分不开。冒烟段按内容认（带「关键词」标签与输入框的那一行是筛选行），
    /// 量到的是<b>标题行 43px、筛选行 31px</b>——与 <see cref="CheckResultsLayout"/> 那一屏正好相反
    /// （那一屏标题行 31、筛选行 39），所以这两个数不能跨屏抄。
    /// </para>
    /// </summary>
    public const int HeadRowHeight = 43;

    /// <summary>
    /// 顶部筛选行（「关键词」标签 + 关键词框）的高 = <b>31px</b>（96 档实测，<c>AutoSize</c>，
    /// 同 <see cref="HeadRowHeight"/>）。这一屏没有筛选下拉，走 <c>Ui.FilterRow</c> 只有关键词框的那一支。
    /// </summary>
    public const int FilterRowHeight = 31;

    /// <summary>
    /// 底部「选中接口的追踪明细」块里那个文本框的定高 = <b>190px</b>（96 档实测）。
    /// <b>这一块是定高，由本文件给</b>：<c>EndpointTraceDialog</c> 的 <c>AddBoxRow</c> 取这个常量，
    /// 不再就地写 190。它是这一屏唯一一个「能设」的高度，也是本轮收口的主要对象。
    /// </summary>
    public const int DetailBoxHeight = 190;

    /// <summary>
    /// 追踪明细块里那行说明（「选中接口的追踪明细：读取的方法、未解析的调用、外部调用与预算」）的高
    /// = <b>17px</b>（96 档实测，9pt 说明字单行 <c>AutoSize</c>，代码里没有一处能设它）。
    /// </summary>
    public const int DetailCaptionHeight = 17;

    /// <summary>
    /// 追踪明细块自己的上下内边距 = <b>12px</b>（<c>Padding = new Padding(Lg, Xs, Lg, Sm)</c>，
    /// 纵向即 <c>Xs</c> 4 + <c>Sm</c> 8）。<b>它单独记一笔</b>：折进 <see cref="DetailBoxHeight"/>
    /// 会让那个数同时表示「文本框多高」与「这一块多高」，下一轮改内边距时没人知道该动多少。
    /// </summary>
    public const int DetailPanePadding = 12;

    /// <summary>
    /// 底部「选中接口的追踪明细」块的高 = <b>219px</b>（96 档实测），由三笔加出来：
    /// 说明行 17 + 文本框 190 + 上下内边距 12。
    /// <para>
    /// <b>第二百五十轮起它是加出来的，不再是一个独立读数</b>：改文本框高度或改内边距，
    /// 这个块与 <see cref="FixedPanesHeight"/>、<see cref="DialogHeight"/> 一起动。
    /// </para>
    /// </summary>
    public const int DetailPaneHeight = DetailBoxHeight + DetailCaptionHeight + DetailPanePadding;

    /// <summary>
    /// 底部按钮行（「查看代码证据」+「关闭」）的高 = <b>60px</b>（96 档实测）。<b>它是 <c>AutoSize</c>，
    /// 本文件给的是读数不是设定值</b>（按钮行的高度由按钮高 + 上下内边距决定，代码里没有一处能「设」它）。
    /// </summary>
    public const int ButtonRowHeight = 60;

    /// <summary>
    /// 接口清单之外那四块固定区合计 = <b>353px</b>（96 档实测：顶部标题行 43 + 筛选行 31 + 底部 219 + 60）。
    /// <para>
    /// <b>它是四块的和，不是一个独立读数</b>：改任一块常量，这个和与窗体高一起动；
    /// 冒烟段再把<b>实测的顶部两行 + 底部两块</b>与这个和逐字对账，两边任一方单独改都被对面问住。
    /// </para>
    /// <para>
    /// <b>四块里三块是 <c>AutoSize</c></b>（标题行、筛选行、按钮行）：它们的常量是读数，代码里没有一处能设它们。
    /// 这三块若在真机上长高（字体度量不同），冒烟段的对账会当场点名——那时要动的是常量，不是算式。
    /// </para>
    /// </summary>
    public const int FixedPanesHeight =
        HeadRowHeight + FilterRowHeight + DetailPaneHeight + ButtonRowHeight;

    /// <summary>接口清单要给 <paramref name="rows"/> 行时占的高：<c>行数 × 行高 + 表头</c>。</summary>
    public static int MainListHeight(int rows) => rows * RowHeight + ColumnHeaderHeight;

    /// <summary>
    /// 这一屏的窗体高：<c>固定四块 + 接口清单（按行数算）+ 清单宿主多出来的那一截</c>。
    /// 实测口径：353 + 12 × 21 + 24 + 10 = 639。
    /// <para>
    /// <b>为什么窗体高不许再写字面量</b>（第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测
    /// 那一屏、第二百四十八轮在差异检查结果那一屏上量到的同一课）：字面量与「放不放得下」之间没有算式绑住，
    /// 改行数或改任一块开销，两边不会一起动，而门在原来那一点上照样全绿。行数收成参数，为的是
    /// 「改行数窗体高跟不跟着变」这句话能在不动产品常量的前提下判。
    /// </para>
    /// </summary>
    /// <param name="rows">接口清单要给看的行数。</param>
    public static int DialogHeightFor(int rows) =>
        FixedPanesHeight + MainListHeight(rows) + ListHostChrome;

    /// <summary>这一屏的窗体宽 = <b>920</b>（96 档设计宽，本轮未动；列宽合计 618px 对清单可视宽 888px）。</summary>
    public const int DialogWidth = 920;

    /// <summary>这一屏的窗体高。<b>值由 <see cref="DialogHeightFor(int)"/> 给</b>（639）。</summary>
    public static readonly int DialogHeight = DialogHeightFor(MainRows);
}
