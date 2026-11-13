namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「差异检查结果」这一屏竖直方向的一份预算（不依赖 WinForms，与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>（第二百四十八轮，wine 96 档实测）：这一屏此前窗体高写死 1000×720，
/// 而它要同时放七块——标题行、筛选行（顶部两行），接口清单（填满剩余），问题清单、条目详情、
/// 模型清单、按钮行（底部四块）。把六块固定区按产品自己的停靠方式量出来：顶部 <b>39 + 31 = 70px</b>、
/// 底部 <b>124 + 168 + 148 + 60 = 500px</b>，合计 <b>570px</b>；720 减掉这 570 只剩 <b>150px</b> 给接口清单宿主，
/// 清单本体 <b>140px</b>——扣掉表头 24px 之后<b>装得下 5 行</b>。而这一屏的主内容正是那张接口清单
/// （真实项目上几十上百个接口），5 行等于「检查结果看不见」。
/// </para>
/// <para>
/// <b>此前为什么量不到</b>：冒烟器每一段都只「建句柄 + <c>CreateControl</c> + <c>PerformLayout</c>」，
/// 从不显示窗体；而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。
/// 第二百四十六轮把这件事登记成表（<see cref="MeasuredScreens"/>），这一屏标的是「未量」；
/// 本轮补上 <c>RunCheckResultsShown</c> 真的 <c>Show()</c> 出来，第一次量到上面那几个数。
/// </para>
/// <para>
/// <b>这一处的口径</b>：竖直方向不再按「窗体给多少像素」写，而是按<b>「接口清单要给几行看」</b>给，
/// 像素由同一份行高与同一份非行高开销算出；窗体高由「固定六块 + 接口清单」推出来，不再是一个独立字面量。
/// 这样「挤没了接口清单」这件事在算式里当场可见，改行数或改任一块开销，窗体高跟着动。
/// 行高与开销都是本轮实测（清单行高 21px、表头 24px、清单宿主比清单本体多 10px），不是设计稿里的数——
/// 设计稿只说「放得下」。
/// </para>
/// <para>
/// <b>这一处判到什么、不判什么</b>：判得到「接口清单给到几行」；判不到真机上这些行的观感、
/// 也判不到窗口被屏幕尺寸截断时这几块怎么退让（wine 125% / 200% 档那一跑窗体是被屏幕截过的，
/// 读数只记不判，与列宽口径同一取舍）——那一些随 G-D5 真机复核。
/// </para>
/// <para>
/// <b>「检查之后任务有变化」那一行不在预算里</b>：过期提示（<c>_stale</c>）只在
/// <c>task.Revision &gt; result.TaskRevision</c> 时出现，出现时顶部多一行、接口清单少一行
/// （<see cref="MainRows"/> → <see cref="MainRows"/> − 1）。本轮量的是它不出现的那一支（夹具里两者相等），
/// 把那一行也折进 <see cref="FixedPanesHeight"/> 会让这个数同时表示两件事——下一轮改提示行时没人知道该动多少。
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
    /// 清单表头占的高 = <b>24px</b>（本轮实测：清单可视高 140px 时第 12 行底边 276px，276 − 12 × 21 = 24）。
    /// <b>它必须单独记一笔</b>：「行数 × 行高」与「装得下」之间差的正是这一笔，
    /// 把它并进 <see cref="ListHostChrome"/> 会让那个数同时表示两件事（表头 + 内边距），
    /// 下一轮改内边距时没人知道该动多少。
    /// </summary>
    public const int ColumnHeaderHeight = 24;

    /// <summary>
    /// 接口清单宿主比清单本体多出来的那一截 = <b>10px</b>（本轮实测：宿主 150px 对清单 140px，
    /// 即下内边距 8px + 发丝边 2px）。判据：窗体高按宿主算，行数按清单本体算，两者之间差的就是这一笔。
    /// </summary>
    public const int ListHostChrome = 10;

    /// <summary>
    /// 接口清单之外那六块固定区合计：顶部 39 + 31 + 底部 124 + 168 + 148 + 60 = <b>570px</b>（96 档实测）。
    /// 顶部两行是 <c>AutoSize</c>、底部四块是定高，所以这里记的是<b>读数</b>而不是算式；
    /// 内容一长它就漂，漂了由冒烟段「七块停靠区高度加起来等于客户区」当场点名。
    /// </summary>
    public const int FixedPanesHeight = 570;

    /// <summary>接口清单要给 <paramref name="rows"/> 行时占的高：<c>行数 × 行高 + 表头</c>。</summary>
    public static int MainListHeight(int rows) => rows * RowHeight + ColumnHeaderHeight;

    /// <summary>
    /// 这一屏的窗体高：<c>固定六块 + 接口清单（按行数算）+ 清单宿主多出来的那一截</c>。
    /// 实测口径：570 + 8 × 21 + 24 + 10 = 772。
    /// <para>
    /// <b>为什么窗体高不许再写字面量</b>（第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测
    /// 那一屏上量到的同一课）：字面量与「放不放得下」之间没有算式绑住，改行数或改任一块开销，两边不会一起动，
    /// 而门在原来那一点上照样全绿。行数收成参数，为的是「改行数窗体高跟不跟着变」这句话
    /// 能在不动产品常量的前提下判。
    /// </para>
    /// </summary>
    public static int DialogHeightFor(int rows) => FixedPanesHeight + MainListHeight(rows) + ListHostChrome;

    /// <summary>这一屏的窗体高（宽不变，1000）。<b>值由 <see cref="DialogHeightFor(int)"/> 给</b>。</summary>
    public static readonly int DialogHeight = DialogHeightFor(MainRows);
}
