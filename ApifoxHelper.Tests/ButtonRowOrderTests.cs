using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 按钮行的 Tab 次序口径（第三百一十五轮，具名 pass：harden）。
/// <para>
/// <b>为什么要有这个文件</b>：全原生界面的按钮行都是 <c>FlowLayoutPanel</c> + <c>FlowDirection.RightToLeft</c>
/// （确认类按钮在右），而 <c>Controls.Add</c> 的次序决定停靠位置——先加的在最右。WinForms 的 Tab 次序在
/// <c>TabIndex</c> 相同时按加入次序走，于是「先加的在最右」同时意味着「先加的最先被 Tab 到」：
/// Tab 键在按钮行里走的是<b>右 → 左</b>，与屏幕上的左右次序相反。第三百一十三轮在生成文档核对那一屏的
/// 失败态上量到过这一条（Tab 链是「… → 「关闭」 → 「重试」」，而屏幕上「重试」在「关闭」左边），
/// 当时只登记了「在链里、与「关闭」相邻」，没有判方向。
/// </para>
/// <para>
/// <b>本文件判两件事</b>：① 判定本身（<see cref="ButtonRowOrder.TabIndexFor"/> 的真值表与「是个排列」）；
/// ② <b>每一行都真的接上了</b>——<c>Desktop/Native</c> 下每一条 <c>RightToLeft</c> 按钮行都必须调一次
/// <see cref="Ui.TabOrderLeftToRight"/>，且发现到的行集合钉成封闭清单（发现正则写坏会让清单变空，
/// 那条用例先红，不会出现「一条都没发现、于是全部通过」的假绿）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「Tab 键在真机上真的这么走」——那要一台 Windows 机器（G-D5 仍 0 台）。
/// 模拟器上量到的那一次在冒烟器 <c>RunEvidenceLoadFailure</c> 里（失败态下「重试」在「关闭」之前），
/// 与这里判的源码形状是两半，谁也不替谁作证。
/// </para>
/// </summary>
public class ButtonRowOrderTests
{
    /// <summary>
    /// 真值表：加入次序 0 是最右那颗，它该<b>最后</b>被 Tab 到；最左那颗（加入次序 <c>count - 1</c>）最先。
    /// </summary>
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 2, 1)]
    [InlineData(1, 2, 0)]
    [InlineData(0, 3, 2)]
    [InlineData(1, 3, 1)]
    [InlineData(2, 3, 0)]
    [InlineData(0, 7, 6)]
    [InlineData(6, 7, 0)]
    public void TabIndexFor_CountsFromTheLeftmost(int addIndex, int count, int expected)
    {
        Assert.Equal(expected, ButtonRowOrder.TabIndexFor(addIndex, count));
    }

    /// <summary>
    /// 一行按钮拿到的 <c>TabIndex</c> 必须<b>恰好是 0…count-1 的一个排列</b>：重号会让两颗按钮的先后
    /// 退回隐式次序（也就是本文件要修的那个方向），漏号会让 Tab 链上多出一站空位。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void TabIndexFor_IsAPermutationOfZeroToCountMinusOne(int count)
    {
        var assigned = Enumerable.Range(0, count).Select(i => ButtonRowOrder.TabIndexFor(i, count)).ToList();
        Assert.Equal(Enumerable.Range(0, count), assigned.OrderBy(v => v));
    }

    /// <summary>
    /// 加入次序越大（越靠左），<c>TabIndex</c> 越小（越先被 Tab 到）——这就是「左 → 右」这一句本身。
    /// </summary>
    [Fact]
    public void TabIndexFor_LaterAddedIsTabbedEarlier()
    {
        const int count = 5;
        for (var i = 1; i < count; i++)
        {
            Assert.True(
                ButtonRowOrder.TabIndexFor(i, count) < ButtonRowOrder.TabIndexFor(i - 1, count),
                $"加入次序 {i} 的 TabIndex 不小于加入次序 {i - 1} 的：方向仍是右 → 左。");
        }
    }

    /// <summary>
    /// 空行没有可赋的值（<c>-1</c>）：调用方按 <c>Controls.Count</c> 循环，天然不动作。
    /// 这一条钉的是「别把 -1 当成一个合法的 TabIndex 赋出去」——赋了会让那一行整行退出 Tab 链。
    /// </summary>
    [Fact]
    public void TabIndexFor_EmptyRowHasNothingToAssign()
    {
        Assert.Equal(-1, ButtonRowOrder.TabIndexFor(0, 0));
        Assert.DoesNotContain(Enumerable.Range(0, 0), _ => true);
    }

    /// <summary>
    /// 发现口径：一条 <c>RightToLeft</c> 按钮行 = 一处 <c>var 名字 = new FlowLayoutPanel { … FlowDirection.RightToLeft … }</c>。
    /// 初始化器可以跨行（提示对话框与状态全文对话框就是跨行写的），故按整段文本匹配。
    /// </summary>
    private static readonly Regex RowDeclaration = new(
        @"var\s+(?<name>\w+)\s*=\s*new\s+FlowLayoutPanel\s*\{(?<body>[^}]*)\}",
        RegexOptions.Singleline);

    /// <summary>一条按钮行该有的接线：<c>Ui.TabOrderLeftToRight(那一行的名字)</c>。</summary>
    private static string Wiring(string name) => "Ui.TabOrderLeftToRight(" + name + ")";

    /// <summary>
    /// 发现 + 判定：返回「哪一条按钮行没接上」的清单（空 = 全接上了）。
    /// <para>
    /// 判的是<b>代码</b>（<see cref="NativeSource.Code"/>，注释已剥掉）：注释里写一句
    /// <c>Ui.TabOrderLeftToRight(buttonRow)</c> 是在说明理由，不是接线。
    /// </para>
    /// </summary>
    private static List<string> Breaks(IEnumerable<NativeSource> sources)
    {
        var breaks = new List<string>();
        foreach (var source in sources)
        {
            foreach (Match match in RowDeclaration.Matches(source.Code))
            {
                if (!match.Groups["body"].Value.Contains("FlowDirection.RightToLeft", StringComparison.Ordinal)) continue;
                var name = match.Groups["name"].Value;
                if (!source.Code.Contains(Wiring(name), StringComparison.Ordinal))
                {
                    breaks.Add($"{source.Name}：按钮行「{name}」是 RightToLeft 流向，却没有调 {Wiring(name)}——"
                        + "Tab 键在这一行里走的是右 → 左，与屏幕上的左右次序相反。");
                }
            }
        }
        return breaks;
    }

    /// <summary>
    /// 每一条 <c>RightToLeft</c> 按钮行都接上了左 → 右的 Tab 次序。
    /// </summary>
    [Fact]
    public void EveryRightToLeftButtonRow_WiresTheLeftToRightTabOrder()
    {
        var breaks = Breaks(Repo.NativeSources());
        Assert.True(breaks.Count == 0, string.Join("\n", breaks));
    }

    /// <summary>
    /// 发现到的按钮行钉成封闭清单：新增一条 <c>RightToLeft</c> 按钮行必须同时接上（否则上面那条红），
    /// 而<b>发现正则写坏</b>（比如 <c>FlowLayoutPanel</c> 换了写法）会让清单变空——那一条先在这里红，
    /// 不会出现「一条都没发现、于是全部通过」的假绿。
    /// </summary>
    [Fact]
    public void Discovery_CoversExactlyTheFifteenButtonRows()
    {
        var found = Repo.NativeSources()
            .SelectMany(source => RowDeclaration.Matches(source.Code)
                .Where(m => m.Groups["body"].Value.Contains("FlowDirection.RightToLeft", StringComparison.Ordinal))
                .Select(m => source.Name + ":" + m.Groups["name"].Value))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        string[] expected =
        [
            "BuildLogDialog.cs:buttonRow",
            "CheckResultsDialog.cs:buttonRow",
            "EvidenceDialog.cs:buttonRow",
            "GenerationDialog.cs:buttons",
            "JournalDialog.cs:buttonRow",
            "NoticeDialog.cs:buttonRow",
            "ProfileAdvancedDialog.cs:buttons",
            "ProfileAnalysisDialog.cs:buttons",
            "ProjectsPage.cs:buttons",
            "PropagationDialog.cs:buttonRow",
            "ScanResultsDialog.cs:buttonRow",
            "ScanResultsDialog.cs:buttonRow",
            "ScanResultsDialog.cs:buttons",
            "StatusTextDialog.cs:buttonRow",
            "TasksPage.cs:buttons",
        ];

        Assert.Equal(expected.OrderBy(entry => entry, StringComparer.Ordinal), found);
    }

    /// <summary>
    /// 发现与判定本身要抓得住破坏（第二百八十二轮那五步的第 ④ 步：删掉它之后别的门照旧全绿，
    /// 说明它判的是被承诺的实物、不是判据在判自己）。四发注入各判一次：
    /// ① 删掉一处接线 → 只报那一处；② 把接线挪到注释里 → 仍报（注释不算接线）；
    /// ③ 新增一条没接线的按钮行 → 报它；④ 把 <c>RightToLeft</c> 改成 <c>LeftToRight</c> → 不报（不是这一族）。
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        const string wired = """
            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft };
            buttonRow.Controls.Add(closeButton);
            Ui.TabOrderLeftToRight(buttonRow);
            """;

        Assert.Empty(Breaks([new NativeSource("Fake.cs", wired)]));

        // ① 删掉接线
        var removed = wired.Replace("Ui.TabOrderLeftToRight(buttonRow);", string.Empty);
        Assert.Single(Breaks([new NativeSource("Fake.cs", removed)]));

        // ② 接线只剩在注释里
        var commented = wired.Replace("Ui.TabOrderLeftToRight(buttonRow);", "// Ui.TabOrderLeftToRight(buttonRow);");
        Assert.Single(Breaks([new NativeSource("Fake.cs", commented)]));

        // ③ 新增一条没接线的按钮行
        var extra = wired + """

            var otherRow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft };
            otherRow.Controls.Add(closeButton);
            """;
        var extraBreaks = Breaks([new NativeSource("Fake.cs", extra)]);
        Assert.Single(extraBreaks);
        Assert.Contains("otherRow", extraBreaks[0], StringComparison.Ordinal);

        // ④ 不是这一族（左 → 右流向的按钮行本来就不需要改次序）
        var leftToRight = wired
            .Replace("FlowDirection.RightToLeft", "FlowDirection.LeftToRight")
            .Replace("Ui.TabOrderLeftToRight(buttonRow);", string.Empty);
        Assert.Empty(Breaks([new NativeSource("Fake.cs", leftToRight)]));
    }
}
