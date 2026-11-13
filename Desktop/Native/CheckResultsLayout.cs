namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「差异检查结果」这一屏竖直方向的一份预算（不依赖 WinForms，与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>（第二百四十八轮，wine 96 档实测）：这一屏此前窗体高写死 1000×720，
/// 而它要同时放七块——标题行、筛选行（顶部两行），接口清单（填满剩余），问题清单、条目详情、
/// 模型清单、按钮行（底部四块）。把六块固定区按产品自己的停靠方式量出来：顶部 <b>31 + 39 = 70px</b>（标题行 + 筛选行）、
/// 底部 <b>124 + 168 + 148 + 60 = 500px</b>，合计 <b>570px</b>；720 减掉这 570 只剩 <b>150px</b> 给接口清单宿主，
/// 清单本体 <b>140px</b>——扣掉表头 24px 之后<b>装得下 5 行</b>。而这一屏的主内容正是那张接口清单
/// （真实项目上几十上百个接口），5 行等于「检查结果看不见」。
/// </para>
/// <para>
/// <b>此前为什么量不到</b>：冒烟器每一段都只「建句柄 + <c>CreateControl</c> + <c>PerformLayout</c>」，
/// 从不显示窗体；而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。
/// 第二百四十六轮把这件事登记成表（<see cref="MeasuredScreens"/>），这一屏标的是「未量」；
/// 第二百四十八轮补上 <c>RunCheckResultsShown</c> 真的 <c>Show()</c> 出来，第一次量到上面那几个数。
/// </para>
/// <para>
/// <b>这一处的口径</b>：竖直方向不再按「窗体给多少像素」写，而是按<b>「接口清单要给几行看」</b>给，
/// 像素由同一份行高与同一份非行高开销算出；窗体高由「固定六块 + 接口清单」推出来，不再是一个独立字面量。
/// 这样「挤没了接口清单」这件事在算式里当场可见，改行数或改任一块开销，窗体高跟着动。
/// 行高与开销都是实测（清单行高 21px、表头 24px、清单宿主比清单本体多 10px），不是设计稿里的数——
/// 设计稿只说「放得下」。
/// </para>
/// <para>
/// <b>第二百四十九轮补的那一层（简报 P0-1）</b>：上一轮把窗体高从字面量改成算式，但算式里的
/// <see cref="FixedPanesHeight"/> 仍是<b>一个 96 档读数 570</b>，而底部四块的高度
/// （124 / 168 / 148 / 60）与顶部两行（39 / 31）<b>分散在 <c>CheckResultsDialog.cs</c> 里就地写死</b>，
/// 与 570 之间没有算式绑住。风险形态：改任一块底部高度 → 570 不跟着动 →
/// <c>DialogHeight_IsTheFormulaAtTheMeasuredRowCount</c> 照样绿（它只判 <c>DialogHeightFor(8) == 772</c>），
/// 只有冒烟段「七块停靠区高度加起来等于客户区」能当场抓。本轮把六块高度具名成常量、
/// <see cref="FixedPanesHeight"/> 改成它们的和，并让冒烟段把<b>实测的顶部两行 + 底部四块</b>
/// 与这个和逐字对账——改任一块而不改常量，那一跑当场点名。
/// </para>
/// <para>
/// <b>这一处判到什么、不判什么</b>：判得到「接口清单给到几行」；判不到真机上这些行的观感、
/// 也判不到窗口被屏幕尺寸截断时这几块怎么退让（wine 125% / 200% 档那一跑窗体是被屏幕截过的，
/// 读数只记不判，与列宽口径同一取舍）——那一些随 G-D5 真机复核。
/// </para>
/// <para>
/// <b>「检查之后任务有变化」那一行不在 <see cref="FixedPanesHeight"/> 里</b>：过期提示（<c>_stale</c>）只在
/// <c>task.Revision &gt; result.TaskRevision</c> 时出现，出现时顶部多一行、接口清单少一行。
/// 把它折进 <see cref="FixedPanesHeight"/> 会让那个数同时表示两件事——下一轮改提示行时没人知道该动多少。
/// 第二百四十九轮把这一支也收成算式（<see cref="StaleRowHeight"/> / <see cref="MainRowsWhenStale"/> /
/// <see cref="DialogHeightFor(int,bool)"/>）：<b>同一个窗体高，出现提示时接口清单让出一行</b>，
/// 两条门各判一支（出现时 8 行放不下、7 行放得下）。
/// </para>
/// <para>
/// <b>这一文件只管竖直轴</b>（与 <see cref="ListColumnWidths"/> 各管一半）：这一屏两张清单的列宽
/// （接口 250 / 200 / 96 / 56，模型 220 / 100 / 88 / 88 / 100）全部在 <c>CheckResultsDialog</c> 就地声明，
/// 这里一个宽度数都不给。
/// </para>
/// </summary>
public static class CheckResultsLayout
{
    /// <summary>
    /// 接口清单要给看的行数 = <b>8</b>。判据：这一屏的主内容就是这张清单，装不下就等于「检查结果看不见」。
    /// 冒烟段 <c>RunCheckResultsShown</c> 按这个数判「这一刻装得下几行」。
    /// </summary>
    public const int MainRows = 8;

    /// <summary>96 档实测行高（<c>ListViewItem.Bounds.Height</c>）。判据：行数都按这一个数换算。</summary>
    public const int RowHeight = 21;

    /// <summary>
    /// 清单表头占的高 = <b>24px</b>（实测：清单可视高 140px 时第 12 行底边 276px，276 − 12 × 21 = 24）。
    /// <b>它必须单独记一笔</b>：「行数 × 行高」与「装得下」之间差的正是这一笔，
    /// 把它并进 <see cref="ListHostChrome"/> 会让那个数同时表示两件事（表头 + 内边距），
    /// 下一轮改内边距时没人知道该动多少。
    /// </summary>
    public const int ColumnHeaderHeight = 24;

    /// <summary>
    /// 接口清单宿主比清单本体多出来的那一截 = <b>10px</b>（实测：宿主 150px 对清单 140px，
    /// 即下内边距 8px + 发丝边 2px）。判据：窗体高按宿主算，行数按清单本体算，两者之间差的就是这一笔。
    /// </summary>
    public const int ListHostChrome = 10;

    // ---------- 固定六块：顶部两行 + 底部四块（第二百四十九轮具名，简报 P0-1）----------

    /// <summary>
    /// 顶部标题行（检查时间 + 计数，过期提示那一行也在这一块里）的高 = <b>31px</b>（96 档实测）。
    /// <b>它是 <c>AutoSize</c>，本文件给的是读数不是设定值</b>——这一行的高度由字体与内容决定，
    /// 代码里没有一处能「设」它。收成常量是为了让 <see cref="FixedPanesHeight"/> 有一个可加的加数，
    /// 而不是让算式里出现一个来历不明的 570。
    /// <para>
    /// <b>31 与 39 这两个数第二百四十九轮才认对</b>：第二百四十八轮记的是「顶部 39 + 31」，
    /// 两个数没有名字，读的人自然会把 39 当成标题行——实测正好相反（筛选行 39px、标题行 31px）。
    /// 两个数加起来一样，所以任何「和等于 570」的对账门都抓不到这个错；抓到它的是冒烟段按内容认名
    /// （带下拉的那一行是筛选行）之后打出来的那一行读数。名字错了不影响布局，但会让下一轮改错那一块。
    /// </para>
    /// </summary>
    public const int HeadRowHeight = 31;

    /// <summary>
    /// 顶部筛选行（状态下拉 + 关键词框）的高 = <b>39px</b>（96 档实测，<c>AutoSize</c>，同 <see cref="HeadRowHeight"/>）。
    /// </summary>
    public const int FilterRowHeight = 39;

    /// <summary>
    /// 底部「检查问题与提示」块的高 = <b>124px</b>（96 档实测）。<b>这一块是定高，由本文件给</b>：
    /// <c>CheckResultsDialog</c> 的 <c>_problemsHost.Height</c> 取这个常量，不再就地写 124。
    /// </summary>
    public const int ProblemsPaneHeight = 124;

    /// <summary>底部「选中接口详情」块的高 = <b>168px</b>（96 档实测，定高，同 <see cref="ProblemsPaneHeight"/>）。</summary>
    public const int DetailPaneHeight = 168;

    /// <summary>底部「数据模型」块的高 = <b>148px</b>（96 档实测，定高，同 <see cref="ProblemsPaneHeight"/>）。</summary>
    public const int ModelsPaneHeight = 148;

    /// <summary>
    /// 底部按钮行的高 = <b>60px</b>（96 档实测）。<b>它是 <c>AutoSize</c>，本文件给的是读数不是设定值</b>
    /// （按钮行的高度由按钮高 + 上下内边距决定，代码里没有一处能「设」它）。
    /// </summary>
    public const int ButtonRowHeight = 60;

    /// <summary>
    /// 接口清单之外那六块固定区合计 = <b>570px</b>（96 档实测：顶部标题行 31 + 筛选行 39 + 底部 124 + 168 + 148 + 60）。
    /// <para>
    /// <b>第二百四十九轮起它是六块的和，不再是一个独立读数</b>：上一轮这里写的是字面量 570，
    /// 而六块的高度散在 <c>CheckResultsDialog.cs</c> 里就地写死——改任一块，570 不跟着动，
    /// 而 <c>DialogHeight_IsTheFormulaAtTheMeasuredRowCount</c> 照样绿（它只判 8 行那一点的取值）。
    /// 现在改任一块常量，这个和与窗体高一起动；冒烟段再把<b>实测的顶部两行 + 底部四块</b>
    /// 与这个和逐字对账，两边任一方单独改都被对面问住。
    /// </para>
    /// <para>
    /// <b>顶部两行与按钮行是 <c>AutoSize</c></b>：它们的常量是读数，代码里没有一处能设它们。
    /// 这三块若在真机上长高（字体度量不同），冒烟段的对账会当场点名——那时要动的是常量，不是算式。
    /// </para>
    /// </summary>
    public const int FixedPanesHeight =
        HeadRowHeight + FilterRowHeight + ProblemsPaneHeight + DetailPaneHeight + ModelsPaneHeight + ButtonRowHeight;

    // ---------- 过期提示那一行（第二百四十九轮，简报 P2-1）----------

    /// <summary>
    /// 「检查之后任务内容有变化」那一行的高 = <b>17px</b>（96 档实测：过期提示出现时顶部两行合计
    /// 31 + 39 + 17 = 87px，比不出现时多 17px；那一行长在标题行里，标题行 31 → 48px）。
    /// <para>
    /// <b>它不进 <see cref="FixedPanesHeight"/></b>：那一行只在 <c>task.Revision &gt; result.TaskRevision</c>
    /// 时出现，折进固定区会让那个数同时表示两件事。它单独记一笔，由
    /// <see cref="DialogHeightFor(int,bool)"/> 在「出现」那一支加上去。
    /// </para>
    /// <para>
    /// <b>它比一个行高（21px）矮</b>：这一行是 9pt 说明字（<c>UiTokens.Caption</c>）单行 <c>AutoSize</c>，
    /// 不是清单行。所以「出现时接口清单少一行」不是「多高就少多少」，而是
    /// <b>「多出来的这一截把清单从 8 行挤到 7 行」</b>——判据写成
    /// <see cref="MainRowsWhenStale"/>，不写成「减去 <see cref="StaleRowHeight"/>」。
    /// </para>
    /// </summary>
    public const int StaleRowHeight = 17;

    /// <summary>
    /// 过期提示出现时接口清单还能给的行数 = <see cref="MainRows"/> − 1 = <b>7</b>。
    /// <para>
    /// <b>为什么是「减一行」而不是「减 17px」</b>：窗体高在构造时就定了（<see cref="DialogHeight"/>），
    /// 提示行是 <c>OnLoad</c> 里才 <c>Visible = true</c> 的——它出现时窗体不会长高，只能从接口清单里让。
    /// 让出来的那一截是 17px，而清单按整行让（一行 21px），所以清单从 8 行掉到 7 行。
    /// 这一条由 <c>CheckResultsLayoutTests</c> 的两条门各判一支：出现时 8 行放不下、7 行放得下。
    /// </para>
    /// </summary>
    public const int MainRowsWhenStale = MainRows - 1;

    /// <summary>接口清单要给 <paramref name="rows"/> 行时占的高：<c>行数 × 行高 + 表头</c>。</summary>
    public static int MainListHeight(int rows) => rows * RowHeight + ColumnHeaderHeight;

    /// <summary>
    /// 这一屏的窗体高：<c>固定六块 + 过期提示（出现时）+ 接口清单（按行数算）+ 清单宿主多出来的那一截</c>。
    /// 实测口径（提示不出现）：570 + 8 × 21 + 24 + 10 = 772。
    /// <para>
    /// <b>为什么窗体高不许再写字面量</b>（第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测
    /// 那一屏上量到的同一课）：字面量与「放不放得下」之间没有算式绑住，改行数或改任一块开销，两边不会一起动，
    /// 而门在原来那一点上照样全绿。行数收成参数，为的是「改行数窗体高跟不跟着变」这句话
    /// 能在不动产品常量的前提下判。
    /// </para>
    /// <para>
    /// <b><paramref name="stale"/> 那一支判的是「同一个窗体高装不装得下」</b>：窗体高在构造时定死，
    /// 提示行出现时不会长高，所以「出现」那一支要问的是
    /// <c>DialogHeightFor(<see cref="MainRowsWhenStale"/>, stale: true) &lt;= <see cref="DialogHeight"/></c>
    /// （放得下）与 <c>DialogHeightFor(<see cref="MainRows"/>, stale: true) &gt; <see cref="DialogHeight"/></c>
    /// （放不下，必须让一行）。这两条就是简报 P2-1 要的「出现时顶部多一行、<see cref="MainRows"/> 相应减一」。
    /// </para>
    /// </summary>
    /// <param name="rows">接口清单要给看的行数。</param>
    /// <param name="stale">过期提示那一行是否出现（出现时顶部多一行）。</param>
    public static int DialogHeightFor(int rows, bool stale) =>
        FixedPanesHeight + (stale ? StaleRowHeight : 0) + MainListHeight(rows) + ListHostChrome;

    /// <summary>这一屏的窗体高（提示不出现那一支，产品实际用的就是它）。</summary>
    public static int DialogHeightFor(int rows) => DialogHeightFor(rows, stale: false);

    /// <summary>这一屏的窗体高（宽不变，1000）。<b>值由 <see cref="DialogHeightFor(int)"/> 给</b>。</summary>
    public static readonly int DialogHeight = DialogHeightFor(MainRows);
}
