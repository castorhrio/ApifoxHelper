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
    /// 这一形态的说明行左侧让出多少像素（= <c>Padding.Left</c>）。
    /// <para>
    /// 字段级是 <b>0</b>：它紧贴网格左缘，与它上面那一行字段名同一条竖线，读起来是「这一行的注脚」。
    /// 分区级是 <b>竖线宽 + 让位 = 6</b>：文字整体右移，左缘多出一条竖线，读起来是「这一块的注脚」。
    /// 两个数<b>必须不相等</b>——相等就等于两种形态在屏幕上没有差异，这一处枚举就白加了。
    /// </para>
    /// </summary>
    public static int LeftIndent(HintRowForm form) => form == HintRowForm.Section ? RuleWidth + TextGap : 0;

    /// <summary>这一形态要不要在左缘画那条竖线（只有分区级画）。</summary>
    public static bool DrawsRule(HintRowForm form) => form == HintRowForm.Section;
}
