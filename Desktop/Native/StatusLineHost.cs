using System.Drawing;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 状态行宿主：状态行标签 + 右侧「查看完整错误」入口（第一百六十四轮）。
/// <para>
/// <b>为什么要有这个文件</b>：状态行装的是失败原因（<c>Exception.Message</c>，动辄两三百字符），
/// 第一百五十九轮开了 <c>AutoEllipsis</c> 之后末尾有「…」、悬停有全文提示，但悬停要鼠标停住、
/// 键盘用户拿不到、文字不可选中也不可复制。判定与文案在 <see cref="StatusLineText"/>
/// （不依赖 WinForms，Linux 侧有真值表），本文件只做接线：量宽度、按判定显隐入口、点开对话框。
/// </para>
/// <para>
/// <b>为什么入口不预留固定占位</b>：预留（按钮一直占着右侧一段宽度）会让状态行永久少一段，
/// 而绝大多数状态行是「已保存」「正在扫描」这类短句，为一条长错误让所有短句都少一段宽度不划算。
/// 不预留也不抖动的条件是<b>判定基准与入口自身占位无关</b>：按「入口不占位时这一行有多宽」判
/// （宿主客户区宽减状态行左右留白），而不是按标签当前宽度——入口一出现就把标签挤窄，拿挤窄后的
/// 宽度自比等于让入口替自己投票，短句也会一直挂着（wine 实测到的正是这一支，见 UpdateFullText）。
/// </para>
/// <para>
/// <b>为什么状态行仍保留悬停提示</b>：<c>AutoEllipsis</c> 的悬停提示是框架白送的，不占布局、
/// 不添控件；入口按钮是给「要读全、要复制、不用鼠标」的那条路。两条路指向同一句话，不冲突。
/// </para>
/// <para>
/// <b>宽度取「所在那一格的剩余宽度」，不取 Panel 缺省值</b>：宿主在工具栏里占的是
/// 「按钮列 <c>AutoSize</c> + 状态行列 <c>Percent 100</c>」的第二格，调用方必须给它
/// <c>Dock = DockStyle.Fill</c>。不设 Dock 时它停在 <c>Panel</c> 的缺省 200px，
/// 状态行这一格在 96/120 两档恒 194px（194 = 200 − 标签左右留白 6，wine 三档实测，
/// 窗口从 640 拉到 1600 都不变）——一条 94 字的真实失败原因是 643px，于是
/// 「放得下就不给入口」那一支在页面这一侧几乎永远走不到，用户每次失败都要多点一次入口。
/// 15 处对话框从接入那天起就设了 Dock，两个主页面漏了这一句（第一百六十五轮补上）；
/// 由 <c>StatusLineTextTests.Host_FillsItsCell_SoTheStatusLineGetsTheRemainingWidth</c> 按类逐段判住。
/// </para>
/// <para>
/// <b>补上 Dock 之后量到的是什么</b>（第二百一十六轮，简报 P0-2；第二百一十八轮按 P1-2 去掉无出处的读数）：
/// 页面侧状态行这一格从此拿到工具栏那一行的剩余宽度，「放得下就不给入口」那一支<b>第一次真的走到了</b>
/// （补 Dock 之前它恒为 194px，一条真实失败原因永远放不下）。三档的具体读数不写在这里——它们随窗口宽度
/// 与缩放档变化，抄进注释就成了没有出处的数；要念就读当场跑出来的那一份：<c>.winecheck/reading.txt</c>
/// 的 <c>RunStatusFullText</c> 段（三档各 4 段全绿，由 <c>ReadingConsistencyTests</c> 与文档对账），
/// 原文行在 <c>/tmp/ah-demo/report-&lt;档位&gt;.txt</c> 的「入口不占位时这一行 Npx」那一句里，
/// 复跑 <c>bash .winecheck/demo.sh</c> 即得。这一句只记机制，不把它读成真机结论（wine 模拟器，G-D5 仍 <c>[ ]</c>）。
/// </para>
/// <para>
/// <b>高度是固定占位</b>：宿主高度取状态行自身高度（DESIGN.md §6「状态行：固定占位，文案变化不推动布局」）。
/// 不这么做的话，<c>Panel</c> 的缺省高度（100px）会把工具栏那一行撑高——状态行从一行文字变成一条空带，
/// 而这是「加一颗入口」不该带来的布局变化（wine 实测：工具栏行高由 30px 变 100px）。
/// </para>
/// <para>
/// <b>范围</b>：磁盘上 <c>Ui.StatusLine()</c> 的 17 处调用点全部经本宿主写状态行——两个主页面
/// （任务页、项目配置页）与 15 处对话框（含嵌在两个页面文件里的 <c>ProfileDialog</c> /
/// <c>NewTaskDialog</c>、嵌在 <c>ScanResultsDialog.cs</c> 里的「手工填写说明」）。
/// 第一百六十四轮只接了两个主页面（失败原因最长的那两处），其余 15 处如实挂账到第一百六十六轮收口；
/// 挂账期间对话框只有悬停提示——能看出「还有下文」，但读不全、不可复制。
/// 这个覆盖范围由 <c>StatusLineTextTests.EveryStatusLine_OnDisk_IsWrittenThroughTheHost</c> 按顶格类
/// 逐段机械判住：新增一处状态行而不接宿主即红，接了却仍留在挂账清单里的同样判红。
/// </para>
/// </summary>
internal sealed class StatusLineHost : Panel
{
    private readonly Label _label;
    private readonly Button _fullText;

    public StatusLineHost(Label label)
    {
        _label = label;
        // 顺序不能反：下面两句形状（AutoSize / Height）会当场触发一次布局，而 OnLayout 的判定要读
        // 入口按钮的可见性——按钮还没建出来时那一句就是空引用（wine 实测：两个主页面构造即崩）。
        _fullText = Ui.Ghost(StatusLineText.FullTextButton);
        _fullText.Dock = DockStyle.Right;
        _fullText.Visible = false;
        _fullText.Click += (_, _) => ShowFullText();

        BackColor = UiTokens.Bg;
        Margin = new Padding(0);
        // 固定占位：高度跟状态行走，不跟 Panel 缺省尺寸走（见类注释「高度是固定占位」）。
        AutoSize = false;
        Height = label.Height;
        _label.Dock = DockStyle.Fill;

        // 停靠按集合倒序生效：先加标签、后加按钮，按钮先占右侧、标签填满剩余宽度。
        Controls.Add(_label);
        Controls.Add(_fullText);
    }

    /// <summary>状态行标签本身（冒烟与既有断言按「非 AutoSize + 左对齐」找它，形状不变）。</summary>
    public Label Label => _label;

    /// <summary>「查看完整错误」入口（冒烟要真的点它，故暴露出来）。</summary>
    public Button FullTextButton => _fullText;

    /// <summary>
    /// 写状态行：文字与字色一起给，入口按「放不放得下」显隐。
    /// 页面与对话框此前直接写 <c>_status.Text</c>，现在两个主页面只经这一个入口——
    /// 入口的显隐与文字同一次更新，不会出现「文字换了、按钮还留着」的半截状态。
    /// </summary>
    public void SetText(string? text, Color color)
    {
        _label.Text = text ?? "";
        _label.ForeColor = color;
        UpdateFullText();
    }

    /// <summary>
    /// 按当前文字与可用宽度决定入口显隐。宽度变化（窗口缩放、分割条拖动、DPI 变化）后要重判，
    /// 故接在 <see cref="OnLayout"/> 上——文字没变但变窄了，同样该给入口。
    /// </summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        UpdateFullText();
    }

    /// <summary>
    /// 完整错误对话框（冒烟要拿到它核对内容，故单独一个入口；点击走同一条路）。
    /// 文字取状态行当前全文——不是截断后的显示文字，也不是悬停提示里的副本。
    /// </summary>
    internal StatusTextDialog BuildFullTextDialog() => new(_label.Text);

    private void ShowFullText()
    {
        using var dialog = BuildFullTextDialog();
        dialog.ShowDialog(FindForm());
    }

    private void UpdateFullText()
    {
        var text = _label.Text;
        var preferred = text.Length == 0
            ? 0
            : TextRenderer.MeasureText(
                text,
                _label.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        // 判定基准是「入口不占位时这一行有多宽」，不是标签当前宽度：入口一出现就把标签挤窄，
        // 拿挤窄后的宽度自比，等于让入口自己的宽度替它投票——短句也会被判成放不下而入口收不起来
        // （wine 实测：失败恢复后 188px 的计数句留在 85px 的标签上，按钮一直挂着）。
        // 宿主客户区宽减左右留白与入口可见与否无关，所以两个方向都收敛。
        var available = ClientSize.Width - _label.Margin.Horizontal;
        var show = StatusLineText.ShouldOfferFullText(preferred, available);
        if (_fullText.Visible != show) _fullText.Visible = show;
    }
}
