namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 按钮行的 Tab 顺序口径（第三百一十五轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：全原生界面的按钮行都是 <c>FlowLayoutPanel</c> + <c>FlowDirection.RightToLeft</c>
/// （Windows 惯例：确认类按钮在右），而 <c>Controls.Add</c> 的次序决定停靠位置——先加的在最右。
/// WinForms 的 Tab 顺序在 <c>TabIndex</c> 相同时按<b>加入次序</b>走，于是「先加的在最右」同时意味着
/// 「先加的最先被 Tab 到」：Tab 键在按钮行里走的是<b>右 → 左</b>，与屏幕上的左右次序相反。
/// 第三百一十三轮在生成文档核对那一屏的失败态上量到过这一条（Tab 链是「… → 「关闭」 → 「重试」」，
/// 而屏幕上「重试」在「关闭」左边），当时只登记了「在链里、与「关闭」相邻」，没有判方向。
/// </para>
/// <para>
/// <b>为什么方向该是左 → 右</b>：Tab 键走的是阅读次序，与控件在屏幕上从左到右的排布一致；
/// 右 → 左意味着键盘用户先落到最右那颗（在失败态里是「关闭」），再往左退到屏上唯一的下一步。
/// 同一只壳里两种方向（<c>TableLayoutPanel</c> 那几屏按列号加入，本来就是左 → 右）也属同壳不同行为。
/// </para>
/// <para>
/// <b>本文件只放判定</b>（不依赖 WinForms、与测试工程单一编译，真值表在 Linux 真执行）：
/// 第 <c>addIndex</c> 个加入的控件该拿到的 <c>TabIndex</c>。触达控件的那一步在
/// <c>Ui.TabOrderLeftToRight</c> 一处，界面不各写一套。
/// </para>
/// </summary>
public static class ButtonRowOrder
{
    /// <summary>
    /// 第 <paramref name="addIndex"/> 个加入 <c>RightToLeft</c> 按钮行的控件该拿到的 <c>TabIndex</c>。
    /// <para>
    /// 加入次序 0 是最右那颗，它该<b>最后</b>被 Tab 到，故取 <c>count - 1</c>；最左那颗（加入次序
    /// <c>count - 1</c>）取 0，最先被 Tab 到。空行与单颗按钮行同样成立（0 颗时没有可赋的值，
    /// 调用方按 <c>Controls.Count</c> 循环，天然不动作）。
    /// </para>
    /// </summary>
    public static int TabIndexFor(int addIndex, int count) => count - 1 - addIndex;
}
