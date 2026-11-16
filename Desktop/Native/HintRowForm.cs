namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 两列字段网格里说明行的<b>两种形态</b>（第三百五十二轮，具名 pass：distill + layout）。
/// <para>
/// <b>为什么要有这一处</b>：第三百四十九轮把说明行从「字段列那一格」改成<b>跨两列</b>，解决了 200% 档
/// 那两句放不下的问题，代价是<b>所有权信号从缩进换成了竖向邻接</b>——二十六句里二十句此前靠缩进指向
/// 它上面那一个字段，改完之后只剩「它就是那一行的下一行」。竖向邻接对<b>字段注脚</b>够用，对
/// <b>分区注脚</b>不够：那六句一次管两三个编辑位或整个分区，跨两列之后与字段注脚长得一模一样，
/// 读的人分不出「这一句在说上面那一行」还是「这一句在说这一整块」。
/// </para>
/// <para>
/// <b>这一处交出什么</b>：把「这一句是字段注脚还是分区注脚」从<b>读感</b>变成<b>调用处必须写下来的一个实参</b>，
/// 并给出两种形态在屏幕上的<b>可量化差异</b>（<see cref="HintRowForms.LeftIndent"/>：字段级 0px、
/// 分区级 2px 竖线 + 4px 文字让位 = 6px）。差异是<b>形状</b>不是颜色：灰阶加单强调色的规范里，
/// 层级靠留白与分隔线表达，不新造颜色（`docs/DESIGN.md` §2 / §6）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上那 6px 够不够一眼可辨——那是观感，只有人眼答得出，随 G-D5 复核；
/// 也不判这二十六句该归哪一形（归属关系在 `docs/NOTES.md`「提示行 26 句的所有权归属」那张表里，
/// 本文件只提供两个取值与它们的几何）。
/// </para>
/// </summary>
public enum HintRowForm
{
    /// <summary>字段注脚：讲的是它紧邻的那一个（或上面连续那几行）编辑位。左侧零缩进、无分隔线。</summary>
    Field,

    /// <summary>分区注脚：一次管两三个编辑位或整个分区。左侧一条 2px 竖线 + 4px 文字让位。</summary>
    Section,
}

/// <summary>
/// 两种形态的几何（不依赖 WinForms、与测试工程共享编译）：<see cref="Ui.HintRow"/> 按这里的数设
/// <c>Padding.Left</c>，测试按这里的数判「两种形态在同一屏上有可辨差异」。
/// <para>
/// <b>为什么几何单独一处</b>：判据要能在 Linux 上算，而 <c>Padding</c> 是 WinForms 的类型。
/// 把「分区级让出多少」写成这一处的纯函数，产品与门读的是同一个数——门里再写一遍 6，
/// 改产品的人不会知道门里还留着一份旧的。
/// </para>
/// </summary>
public static class HintRowForms
{
    /// <summary>分区注脚左侧那条竖线的宽度（px，96dpi 逻辑像素）。</summary>
    public const int RuleWidth = 2;

    /// <summary>竖线与文字之间的让位（px）：取间距档 <c>UiTokens.Xs</c>，由 <c>HintRowFormTests</c> 与令牌源对账。</summary>
    public const int TextGap = 4;

    /// <summary>
    /// 这一形态的说明行在 <b>96dpi 设计档</b>左侧让出多少像素（= 出厂时 <c>Padding.Left</c> 的基线）。
    /// <para>
    /// 字段级是 <b>0</b>：它紧贴网格左缘，与它上面那一行字段名同一条竖线，读起来是「这一行的注脚」。
    /// 分区级是 <b>竖线宽 + 让位 = 6</b>：文字整体右移，左缘多出一条竖线，读起来是「这一块的注脚」。
    /// 两个数<b>必须不相等</b>——相等就等于两种形态在屏幕上没有差异，这一处枚举就白加了。
    /// </para>
    /// <para>
    /// 这是<b>基线</b>，不是出厂值：真正写进 <c>Padding.Left</c> 的是
    /// <see cref="ScaledLeftIndent"/>（基线 × 当前缩放因子）。第三百五十四轮之前出厂值就是这一个数，
    /// 而框架那一次 <c>Form.Scale</c> 并没有替它放大——三档实测恒为 6px，见 <see cref="ScaledLeftIndent"/>。
    /// </para>
    /// </summary>
    public static int LeftIndent(HintRowForm form) => form == HintRowForm.Section ? RuleWidth + TextGap : 0;

    /// <summary>
    /// 这一形态在<b>当前缩放档</b>下左侧让出多少像素——出厂时真正写进 <c>Padding.Left</c> 的那个数。
    /// <para>
    /// <b>为什么让位要在这里按因子算一次</b>（第三百五十四轮，简报 P0-2）：第三百五十三轮的前提是
    /// 「<c>DpiScale.Apply</c> 那一次 <c>Form.Scale</c> 会把 <c>Padding.Left</c> 按缩放档放大」。
    /// 三档复跑把这个前提量掉了：四个子对话框的构造顺序是<b>先</b> <c>DpiScale.Apply</c>、<b>后</b>加控件，
    /// 说明行在缩放那一刻还不存在，<c>Form.Scale</c> 一次都没碰过它——wine 三档实测
    /// <c>Padding.Left</c> 恒为 <b>6px</b>（96 / 125% / 200% 同一个数），不是 6 / 8 / 12。
    /// 于是 200% 档上「一条 2px 线 + 4px 让位」被留在 96 档的尺寸上，而旁边的文字已经放大一倍。
    /// </para>
    /// <para>
    /// <b>怎么算</b>：与左列宽同一条口径——基线 × 缩放因子，因子取 <c>DpiScale.ScaleFactor()</c>
    /// （<c>ProfileGridColumns.ScaledLabelWidth</c> 用的就是它），取整走 <see cref="DpiScaleFactor.ScaledExtent"/>
    /// 同一份，不另写第二个取整。三档因此是 6 / 8 / 12，<see cref="RuleWidthAt"/> 的入参前提由此成立。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判真机上 200% 档那条线够不够一眼可辨——那是观感，随 G-D5 复核。
    /// </para>
    /// </summary>
    public static int ScaledLeftIndent(HintRowForm form, float factor) =>
        DpiScaleFactor.ScaledExtent(LeftIndent(form), factor);

    /// <summary>这一形态要不要在左缘画那条竖线（只有分区级画）。</summary>
    public static bool DrawsRule(HintRowForm form) => form == HintRowForm.Section;

    /// <summary>
    /// 这一形态在<b>当前缩放档</b>下那条竖线该画多宽（px）：由<b>已经缩放过的让位</b>反算，
    /// 竖线与让位之比恒为 <see cref="RuleWidth"/> : <see cref="TextGap"/>（96 档 2 : 4）。
    /// <para>
    /// <b>为什么线宽不能是一个常量</b>（第三百五十三轮，具名 pass：layout + harden）：让位随缩放档放大
    /// （<see cref="ScaledLeftIndent"/>：96 档 6px → 200% 档 12px），而 <c>OnPaint</c> 里那个
    /// <c>new Pen(…, RuleWidth)</c> 是画出来的，框架不会替它放大。于是 200% 档上竖线仍是 2px、
    /// 让位已经 12px——两种形态的差异还在，但「一条线 + 一段让位」这个形状本身被拉成了 1 : 6，
    /// 与 96 档量过并归档的 1 : 2 不是同一个形状。线宽按让位反算之后，三档都落在同一个比例附近
    /// （四舍五入的界内：96 档 2 : 4、125% 档 3 : 5、200% 档 4 : 8），产品与门读的仍是
    /// <see cref="RuleWidth"/> 与 <see cref="TextGap"/> 这两个数（不新增第三个常量，也不在界面层就地写一个 4）。
    /// </para>
    /// <para>
    /// <b>「恒为 1 : 2」这句措辞按实测收窄</b>（第三百五十四轮，简报 P2-1）：三档里只有 96 与 200% 档
    /// 恰好是 1 : 2，125% 档是 3 : 5——让位 8px 反算出 2.67px，取整到 3px。这是四舍五入的界，
    /// 不是比例漂了；写成「恒为」等于替一个没量过的档位下结论。
    /// </para>
    /// <para>
    /// <b>为什么入参是让位而不是缩放因子</b>：让位是<b>控件上此刻真的那一个数</b>——它已经含了当前缩放档
    /// （出厂时由 <see cref="ScaledLeftIndent"/> 按因子算好），也含了运行中 <c>WM_DPICHANGED</c>
    /// 那一次重算的结果。再取一次屏幕 DPI 等于把同一件事算两遍，而两遍在「窗口被拖到另一块屏」
    /// 那一刻会给出不同的答案（<c>DpiScale</c> 的类注释记着这一族）。
    /// 让位为 0（字段级那一形，或有人把让位改没了）时答 0：不画线，也不猜一个最小宽度。
    /// </para>
    /// </summary>
    public static int RuleWidthAt(int indent)
    {
        if (indent <= 0) return 0;
        var total = RuleWidth + TextGap;
        // 四舍五入到最近整数：6 → 2、8 → 3、12 → 4（96 / 125% / 200% 三档的让位实测值）。
        var scaled = (indent * RuleWidth + total / 2) / total;
        return scaled < 1 ? 1 : scaled;
    }
}
