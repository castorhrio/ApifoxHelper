using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览接口勾选页的「全选」手势（Ctrl+A，第二百八十四轮）的断言。
/// <para>
/// 这一轮补的是一只手势：勾选页此前只有一颗「全选可同步」按钮，键盘用户要勾一批只能逐行按空格；
/// 而 <c>SyncSelect</c> 的类注释早就写着那颗按钮的作用域「与资源管理器 Ctrl+A 同序」——
/// 那句话指着一个界面上按不出来的键。
/// </para>
/// <para>
/// 门分两段，各自只证明自己那一段：① 真执行段跑的是主工程那一份判定（口径文件与测试工程共享编译），
/// 在 Linux 上真的执行；② 接线段读的是源码形状（装在哪、装在谁身上、动作是不是同一个）——
/// WinForms 的按键投递在 Linux 上跑不起来，故这一段只能读形状，<b>不声称它运行过</b>；
/// 真机上的按键手感随 G-D5 复核。
/// </para>
/// </summary>
public class SelectAllGestureTests
{
    // ---------- ① 真执行段：跑的是主工程那一份判定 ----------

    /// <summary>
    /// 八种修饰键组合逐一过一遍：只认裸 Ctrl。带 Shift/Alt 的组合不属本手势、留给控件自身
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
    public void OnlyBareControl_IsTheSelectAllGesture(bool shift, bool control, bool alt) =>
        Assert.Equal(control && !shift && !alt, ListChecks.IsSelectAllGesture(shift, control, alt));

    /// <summary>
    /// 本仓三只 Ctrl 手势（找、复制、全选）拆修饰键必须是同一个形状：只认 Ctrl，
    /// 带 Shift/Alt 的组合不吞键。这条门让「同一纪律」成为可核对的，而不是各文件里的一句自述——
    /// 三处里有一处改了形状，这里就红，逼着改的人说清为什么。
    /// </summary>
    [Fact]
    public void TheThreeCtrlGestures_SplitTheModifiersTheSameWay()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var shift = (mask & 4) != 0;
            var control = (mask & 2) != 0;
            var alt = (mask & 1) != 0;
            var expected = control && !shift && !alt;
            Assert.Equal(expected, ListChecks.IsSelectAllGesture(shift, control, alt));
            Assert.Equal(expected, ListCopy.IsCopyGesture(shift, control, alt));
            Assert.Equal(expected, ShellShortcuts.IsFindGesture(shift, control, alt));
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
        Assert.DoesNotContain("System.Windows.Forms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Windows.Forms", source, StringComparison.Ordinal);
    }

    // ---------- ② 接线段：读源码形状（WinForms 投递在 Linux 上跑不起来，不声称它运行过） ----------

    /// <summary>
    /// 「A」这个键全仓只在一处被认出来。认键散成两处，两处迟早对不上
    /// （一处认了 Ctrl+A、另一处认了 Ctrl+Alt+A，界面上就是一个键有时灵有时不灵）。
    /// </summary>
    [Fact]
    public void TheLetterA_IsRecognizedInExactlyOnePlace()
    {
        var holders = Repo.NativeSources()
            .Where(s => CodeScan.HasLine(s.Text, "Keys.A"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, holders);
    }

    /// <summary>
    /// 手势与按钮调的是同一个方法：两条入口、一件事。
    /// 若日后有人把 Ctrl+A 接到另一处动作上（哪怕只是「多勾几行」），
    /// 「手势的作用域与那颗按钮逐字相同」这句就假了，这里先红。
    /// </summary>
    [Fact]
    public void TheGestureAndTheButton_CallTheVerySameMethod()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        var code = CodeScan.Text(source);
        Assert.Matches(@"(?m)^\s*_selectAllButton\.Click \+= \(_, _\) => SelectAllSyncable\(\);$", code);
        Assert.Matches(@"(?m)^\s*Ui\.WireSelectAll\(_selectList, SelectAllSyncable\);$", code);
    }

    /// <summary>
    /// 手势只装在一处。清单以后若要再装（别处的复选清单），
    /// 那一处必须是同一个动作——所以这里是「装在一处」，不是「只有一处」。
    /// </summary>
    [Fact]
    public void TheGesture_IsInstalledInExactlyOnePlace()
    {
        var callers = Repo.NativeSources()
            .Where(s => CodeScan.HasLine(s.Text, "Ui.WireSelectAll("))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "SyncPreviewDialog.cs" }, callers);
    }

    /// <summary>
    /// 对话框不就地认键、不就地拆修饰键：认键在共用入口一处，界面不写第二份。
    /// （同一族的问题此前在别处出现过：对话框里按 Ctrl+F 没反应，正是「就地写了一份」。）
    /// </summary>
    [Fact]
    public void TheDialog_DoesNotRecognizeTheKeyItself()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.Empty(CodeScan.LinesContaining(source, "Keys.A"));
        Assert.Empty(CodeScan.LinesContaining(source, "Keys.Control"));
        Assert.Empty(CodeScan.LinesContaining(source, "e.Shift"));
    }

    /// <summary>
    /// 共用入口只认键与修饰键，别的什么都不做：勾哪些行由调用方交进来的动作决定，
    /// 入口自己不碰勾选状态、不重建清单、不写数据。
    /// </summary>
    [Fact]
    public void TheSharedEntry_OnlyRecognizesTheKey()
    {
        var source = Repo.Source("Desktop", "Native", "Ui.cs");
        var body = Slice(source, "public static void WireSelectAll", "计数格");
        Assert.Contains("ListChecks.IsSelectAllGesture", body, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode != Keys.A", body, StringComparison.Ordinal);
        Assert.Contains("e.SuppressKeyPress = true;", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Checked", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Clear", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 手势不接管文本框：形参就是清单（不是文本框），装的也是那张清单。
    /// 焦点在关键词框里时 Ctrl+A 仍是「选中框里的全部文字」。
    /// </summary>
    [Fact]
    public void TheGesture_IsNotWiredToAnyTextBox()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Matches(@"public static void WireSelectAll\(ListView list, Action selectAll\)", ui);
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.Matches(@"(?m)^\s*private readonly ListView _selectList = Ui\.List\(true\);$", CodeScan.Text(dialog));
    }

    /// <summary>
    /// Ctrl+A 归列表层，不归壳：壳的快捷键全集里没有它（壳不占这个键），
    /// 而编辑键保留名单里有它（壳不得抢走文本框的 Ctrl+A）。
    /// 两边同时成立，这一键才只有一个主人。
    /// </summary>
    [Fact]
    public void CtrlA_BelongsToTheListLayer_NotToTheShell()
    {
        Assert.DoesNotContain(ShellShortcuts.All, e => e.Gesture.Equals("Ctrl+A", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Ctrl+A", ShellShortcuts.ReservedEditingKeys);
    }

    /// <summary>
    /// 忙的时候两条入口一起闭：按钮在忙时禁用，手势也走同一条守门。
    /// 否则会出现「按钮点不动、Ctrl+A 还能改勾选」这种两个入口两副面孔的情形。
    /// </summary>
    [Fact]
    public void TheBusyGate_CoversTheGestureAndTheButtonAlike()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        var body = Slice(source, "private void SelectAllSyncable()", "private void RefreshSelectionCount()");
        Assert.Contains("_diff is null || _busy", body, StringComparison.Ordinal);
        var busy = Slice(source, "private void SetBusy(bool busy)", "private void RefreshPlanActionButtons()");
        // 第二百九十五轮起这颗按钮的启停收进 RefreshSelectAllButton 一处（与「清空」同一形状）：
        // 忙时两条入口一起闭这一条不变，认的是那次调用，而不是就地写一行赋值。
        Assert.Contains("RefreshSelectAllButton();", busy, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectAllButton.Enabled", busy, StringComparison.Ordinal);
        var judge = Slice(source, "private void RefreshSelectAllButton()", "private void RefreshClearButton()");
        Assert.Contains("_selectAllButton.Enabled = !_busy && _diff is not null && SyncSelect.CanSelectAll(count);", judge, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSelectAllGesture()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("复选清单的批量全选", section, StringComparison.Ordinal);
        Assert.Contains("ListChecks.IsSelectAllGesture", section, StringComparison.Ordinal);
        Assert.Contains("Ui.WireSelectAll", section, StringComparison.Ordinal);
        // 这一页的可同步口径与两个批量动作的作用域逐字不动——条款里要写明这一点。
        Assert.Contains("不改可同步口径", section, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var section = Repo.DesignSection(8);
        var row = section.Split('\n').FirstOrDefault(l => l.StartsWith("| 复选清单的批量全选（§6） |", StringComparison.Ordinal));
        Assert.NotNull(row);
        Assert.Contains("第二百八十四轮", row!, StringComparison.Ordinal);
        Assert.Contains("ListChecks.IsSelectAllGesture", row!, StringComparison.Ordinal);
        Assert.Contains("Ui.WireSelectAll", row!, StringComparison.Ordinal);
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
