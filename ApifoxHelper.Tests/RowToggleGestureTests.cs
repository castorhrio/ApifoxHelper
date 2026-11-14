using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 复选清单「翻这一行」的键盘入口（空格，第二百八十九轮）的断言。
/// <para>
/// 这一轮补的是同一件事的第二条入口：勾选页的批量动作（Ctrl+A 与「全选可同步」）第二百八十四轮已接上，
/// 而「只勾这一行」在键盘上一直靠 <c>ListView</c> 自带的系统实现——本仓没有它的判定、没有接线、
/// 也没有任何断言。于是同一件事的两条入口（鼠标点行 / 键盘空格）落在两处实现上：
/// 鼠标那条走 <c>Ui.List</c> 工厂的翻转点，键盘那条走系统；系统那一支一旦被自绘、被 <c>ItemCheck</c>
/// 的拒绝路径或某个修饰键改掉，界面上就是「点得动、按不动」，而全仓没有一条门会响。
/// </para>
/// <para>
/// 门分两段，各自只证明自己那一段：① 真执行段跑的是主工程那一份判定（口径文件与测试工程共享编译），
/// 在 Linux 上真的执行；② 接线段读的是源码形状（装在哪、装在谁身上、两条入口是不是同一个翻转点）——
/// WinForms 的按键投递在 Linux 上跑不起来，故这一段只能读形状，<b>不声称它运行过</b>；
/// 真机上的按键手感随 G-D5 复核。
/// </para>
/// </summary>
public class RowToggleGestureTests
{
    // ---------- ① 真执行段：跑的是主工程那一份判定 ----------

    /// <summary>
    /// 八种修饰键组合逐一过一遍：只认裸空格。带 Ctrl/Shift/Alt 的组合不属本手势、留给控件自身
    /// （不吞键，也不给既有组合键发明新语义）。
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void OnlyBareSpace_IsTheRowToggleGesture(bool shift, bool control, bool alt) =>
        Assert.Equal(!shift && !control && !alt, ListChecks.IsToggleRowGesture(shift, control, alt));

    /// <summary>
    /// 四只手势互不重叠：翻行只认裸键，三只 Ctrl 手势只认裸 Ctrl——同一个按键组合不可能同时是两件事。
    /// 这条门让「一次按键只有一个含义」成为可核对的，而不是各文件里的一句自述。
    /// </summary>
    [Fact]
    public void TheFourGestures_NeverOverlap()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var shift = (mask & 4) != 0;
            var control = (mask & 2) != 0;
            var alt = (mask & 1) != 0;
            var toggleRow = ListChecks.IsToggleRowGesture(shift, control, alt);
            var ctrlGestures = ListChecks.IsSelectAllGesture(shift, control, alt)
                || ListCopy.IsCopyGesture(shift, control, alt)
                || ShellShortcuts.IsFindGesture(shift, control, alt);
            Assert.False(toggleRow && ctrlGestures, $"修饰键组合 {shift}/{control}/{alt} 同时命中翻行与一只 Ctrl 手势。");
        }
    }

    /// <summary>
    /// 真执行段跑的是主工程那一份：口径文件必须与测试工程共享编译，
    /// 否则上面那张真值表量的是一份拷贝，改主工程它不响。
    /// </summary>
    [Fact]
    public void TheGestureRule_IsSharedWithTheMainProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListChecks.cs", csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判定文件不碰 WinForms：它要能在这台 Linux 上真的跑，故不得引控件类型。
    /// </summary>
    [Fact]
    public void TheGestureRule_DoesNotDependOnWinForms()
    {
        var source = Repo.Source("Desktop", "Native", "ListChecks.cs");
        Assert.DoesNotContain("using System.Windows.Forms", source, StringComparison.Ordinal);
    }

    // ---------- ② 接线段：读源码形状（WinForms 投递在 Linux 上跑不起来，不声称它运行过） ----------

    /// <summary>
    /// 「空格」这个键全仓只在一处被认出来。认键散成两处，两处迟早对不上
    /// （一处认了裸空格、另一处认了 Shift+空格，界面上就是一个键有时灵有时不灵）。
    /// </summary>
    [Fact]
    public void TheSpaceKey_IsRecognizedInExactlyOnePlace()
    {
        var holders = Repo.NativeSources()
            .Where(s => CodeScan.HasLine(s.Text, "Keys.Space"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, holders);
    }

    /// <summary>
    /// 两条入口、一件事、一处翻转：鼠标点行与键盘空格都调同一个 <c>ToggleRow</c>，
    /// 而「翻」这个动作（<c>Checked = !Checked</c>）在整个原生界面恰出现一次。
    /// 谁把其中一条改成就地翻，这里先红。
    /// </summary>
    [Fact]
    public void BothEntries_CallTheVerySameFlipPoint()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var code = CodeScan.Text(ui);
        // 翻转动作只有一处（工厂里那个私有方法）。
        var flips = CodeScan.LinesContaining(ui, "Checked = !").ToList();
        Assert.Single(flips);
        Assert.Contains("private static void ToggleRow(ListViewItem row) => row.Checked = !row.Checked;", flips[0], StringComparison.Ordinal);
        // 两条入口都调它：鼠标那一条与键盘那一条各一次。
        Assert.Equal(2, CodeScan.LinesContaining(ui, "ToggleRow(row);").Count());
        Assert.Matches(@"(?m)^\s*ToggleRow\(row\);$", code);
    }

    /// <summary>
    /// 键盘入口装在工厂的 <c>checkBoxes</c> 分支里（每一张复选清单都自带），
    /// 认键与修饰键交给 <see cref="ListChecks.IsToggleRowGesture"/>，本工厂不自己拆修饰键；
    /// 焦点行缺失时不动作、也不吞键。
    /// </summary>
    [Fact]
    public void TheKeyboardEntry_IsInstalledInTheCheckboxBranch()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var branch = Slice(ui, "if (checkBoxes)", "return list;");
        Assert.Contains("Keys.Space", branch, StringComparison.Ordinal);
        Assert.Contains("ListChecks.IsToggleRowGesture(e.Shift, e.Control, e.Alt)", branch, StringComparison.Ordinal);
        Assert.Contains("list.FocusedItem is not ListViewItem row", branch, StringComparison.Ordinal);
        Assert.Contains("e.SuppressKeyPress = true;", branch, StringComparison.Ordinal);
        // 不就地拆修饰键：判定只在 ListChecks 一处。
        Assert.DoesNotContain("e.Control &&", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Shift &&", branch, StringComparison.Ordinal);
    }

    /// <summary>
    /// 手势不接管文本框：它装在清单的按键通路上（形参就是清单），
    /// 焦点在关键词框里时空格仍是「打一个空格」。
    /// </summary>
    [Fact]
    public void TheGesture_IsNotWiredToAnyTextBox()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var branch = Slice(ui, "if (checkBoxes)", "return list;");
        Assert.Contains("list.KeyDown += (_, e) =>", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox", branch, StringComparison.Ordinal);
    }

    /// <summary>
    /// 对话框不就地认键：勾选页不写 <c>Keys.Space</c>、不自己翻行——
    /// 那一页的勾选状态仍只经 <c>ItemChecked</c> 写进 <c>SyncSelect</c>（单一来源）。
    /// </summary>
    [Fact]
    public void TheDialog_DoesNotRecognizeTheKeyItself()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.Empty(CodeScan.LinesContaining(source, "Keys.Space"));
        Assert.Empty(CodeScan.LinesContaining(source, "Checked = !"));
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheRowToggleGesture()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("复选清单的键盘翻行", section, StringComparison.Ordinal);
        Assert.Contains("ListChecks.IsToggleRowGesture", section, StringComparison.Ordinal);
        Assert.Contains("ToggleRow", section, StringComparison.Ordinal);
        // 这一页的可同步口径与两个批量动作的作用域逐字不动——条款里要写明这一点。
        Assert.Contains("不改可同步口径", section, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var section = Repo.DesignSection(8);
        var row = section.Split('\n').FirstOrDefault(l => l.StartsWith("| 复选清单的键盘翻行（§6） |", StringComparison.Ordinal));
        Assert.NotNull(row);
        Assert.Contains("第二百八十九轮", row!, StringComparison.Ordinal);
        Assert.Contains("ListChecks.IsToggleRowGesture", row!, StringComparison.Ordinal);
        Assert.Contains("ToggleRow", row!, StringComparison.Ordinal);
        // 台账要如实：接线不证明真机上的按键手感，那一条仍挂在 G-D5 下面。
        Assert.Contains("G-D5", row!, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
