namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览「预览页」竖直方向的一份预算（不依赖 WinForms，与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>（第二百四十三轮，wine 96 档实测）：这一页要同时放五块东西——
/// 计划概要区、关键词行、写入清单、选中动作的写入内容、范围外影响与保留设置、未纳入清单。
/// 其中三块此前各写死一个像素高（190 / 130 / 150），窗体高也写死（1020×740）。把这几块与顶部
/// 概要区加起来，留给<b>写入清单</b>这一块的是 <b>27px</b>，清单本体 <b>0px</b>、这一刻<b>装得下 0 行</b>
/// （读数：<c>RunListColumnFit</c> 第二段——「由内到外 ListView 0px ← Panel 2px ← Panel 27px ← Panel 651px ← 窗体 740px」）。
/// 这一屏存在的理由正是逐动作读冻结计划，而那一行一行在主界面上看不见。
/// </para>
/// <para>
/// <b>此前为什么量不到</b>：冒烟器每一段都是「建句柄 + CreateControl + PerformLayout」，
/// 从不显示窗体；而预览页 <c>Visible = false</c> 直到计划出现为止。一张没显示过的清单，
/// <c>ClientSize</c> 的高就是 0——上一轮念的「写入清单 984 对预算 1001」取自宽、不取自高，
/// 于是这个 0 一直不在任何读数里。本轮改成真的显示，并先点产品自己那颗「查看已有计划」把这一页带出来。
/// </para>
/// <para>
/// <b>这一处的口径</b>：竖直方向不再按「哪一块给多少像素」写，而是按<b>「哪一块要给几行看」</b>给，
/// 像素由同一份行高与同一份非行高开销（说明行 + 内边距 + 发丝边）算出；主清单要的行数写在最前面，
/// 另外三块从剩余里让。这样四块的高度不再互不相干，而「挤没了主清单」这件事在算式里当场可见。
/// 行高与两块开销都是本轮实测（清单行高 21px；清单块开销 58px 由「未纳入清单 150 对清单本体 92」反推、
/// 文字块开销 30px 由「写入内容 190 对内容框 160」反推），不是设计稿里的数——设计稿只说「放得下」。
/// </para>
/// <para>
/// <b>这一处判到什么、不判什么</b>：判得到「每一块给到几行」；判不到真机上这些行的观感、
/// 也判不到窗口被屏幕尺寸截断时这几块怎么退让（wine 125% / 200% 档那一跑窗体是被屏幕截过的，
/// 读数只记不判，与列宽口径同一取舍）——那一些随 G-D5 真机复核。
/// </para>
/// </summary>
public static class SyncPreviewPanes
{
    /// <summary>
    /// 这一屏的窗体高（宽不变，1020）。<b>740 → 790 的出处，两个数都是本轮实测</b>：
    /// ① 窗体 740 而三块固定区按旧的 190 / 130 / 150 写死时，预览页 651px、顶部概要区与关键词行 154px，
    /// 主清单这一刻拿到 27px（清单本体 0px、<b>0 行</b>）——本轮开工的缺陷；
    /// ② 三块改按行数给、窗体仍 770 时，主清单到了 9 行，而<b>写入内容只给到 2 行</b>（判据 3 行）——
    /// 因为文字块那一刻的实际开销是 <see cref="TextPaneChrome"/>，不是先猜的那一个。
    /// 于是窗体高由「四块都要给自己的行数」推出来：<c>ActionRows × RowHeight + 25 + FixedPaneTotal
    /// + 概要区与关键词行 + 窗体级停靠</c>，取 790。让出的高度只给这两块，没有哪一块被挤没。
    /// 真机上这个高度被屏幕截断时怎么退让，本处判不到，随 G-D5。
    /// </summary>
    public const int DialogHeight = 790;

    /// <summary>96 档实测行高（<c>ListViewItem.Bounds.Height</c> 与正文文字行高同值）。判据：行数都按这一个数换算。</summary>
    public const int RowHeight = 21;

    /// <summary>清单块的开销：说明行 + 关键词行 + 内边距 + 发丝边（本轮实测：未纳入清单 host 142 对清单本体 84）。</summary>
    public const int ListPaneChrome = 58;

    /// <summary>
    /// 主清单那一块的开销：说明行 + 内边距 + 发丝边 = <b>27px</b>（本轮实测：窗体 770 时 actionHost 220px
    /// 对清单本体 193px）。<b>它与 <see cref="ListPaneChrome"/> 不是一个数，也不该折成一个</b>：
    /// 未纳入清单的关键词行在它那一块里面，写入清单的关键词行在外面那一格（概要区与关键词行合计）里——
    /// 用 58 去算主清单要的地方，等于凭空多要 31px，把「放得下」算成「放不下」。
    /// </summary>
    public const int ActionPaneChrome = 27;

    /// <summary>
    /// 文字块的开销：说明行 + 内边距 + 发丝边。<b>取本轮实测两块里更紧的那一个</b>（写入内容 93 对内容框 42，
    /// 说明区 72 对内容框 42 再减去它那行更短的说明行）——说明行折不折行由文字长度决定，
    /// 为两块各写一个开销等于在算式里留两个来源，这里只留更严的那一个。
    /// </summary>
    public const int TextPaneChrome = 51;

    /// <summary>写入清单（本页主表）至少要看下的行数。低于这个数，逐动作读计划就不成立。</summary>
    public const int ActionRows = 8;

    /// <summary>选中动作的写入内容（JSON，可双向滚）至少要看下的行数。</summary>
    public const int PayloadRows = 3;

    /// <summary>范围外影响、计划提示与保留设置至少要看下的行数。</summary>
    public const int NotesRows = 2;

    /// <summary>未纳入清单至少要看下的行数（这一张比写入清单长，第八十六轮给它加关键词框的理由）。</summary>
    public const int ExcludedRows = 4;

    /// <summary>清单块的高度：给看的行数 × 行高 + 那一块的开销。</summary>
    public static int ListPaneHeight(int rows) => rows * RowHeight + ListPaneChrome;

    /// <summary>文字块的高度：给看的行数 × 行高 + 那一块的开销。</summary>
    public static int TextPaneHeight(int rows) => rows * RowHeight + TextPaneChrome;

    /// <summary>主清单块的高度：给看的行数 × 行高 + 它自己那一处开销（关键词行不算在这里，见 <see cref="ActionPaneChrome"/>）。</summary>
    public static int ActionPaneHeight(int rows) => rows * RowHeight + ActionPaneChrome;

    /// <summary>本页三块固定区合起来占掉的高度（写入清单拿的是窗体剩余，不在这里写第二份）。</summary>
    public static int FixedPaneTotal =>
        TextPaneHeight(PayloadRows) + TextPaneHeight(NotesRows) + ListPaneHeight(ExcludedRows);

    /// <summary>
    /// 预览页这一刻放不放得下主清单：窗体能给的页面高，减去三块固定区与顶部概要区/关键词行之后，
    /// 是否还不小于主清单要的那几行。剩余不足即假——那一支就是本轮实测到的 0 行。
    /// </summary>
    /// <param name="pageHeight">预览页那一刻的客户区高（本轮实测 651px 对 740 窗体）。</param>
    /// <param name="headHeight">顶部概要区 + 关键词行占掉的高（本轮实测 154px）。</param>
    public static bool ActionListFits(int pageHeight, int headHeight) =>
        pageHeight - headHeight - FixedPaneTotal >= ActionPaneHeight(ActionRows);

    /// <summary>这一刻主清单实际拿到的行高数（给读数用：让「装得下几行」这句话有出处）。</summary>
    public static int ActionRowsAvailable(int listHeight) => listHeight / RowHeight;
}
