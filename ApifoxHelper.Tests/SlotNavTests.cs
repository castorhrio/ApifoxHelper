using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释缺口清单的「上一条 / 下一条」与位置指示（DESIGN.md §6，第一百一十五轮）：
/// 冻结页面的注释位置详情有「‹ 上一条 / N / M / 下一条 ›」，原生此前只有清单——逐条处理
/// （看代码证据、手工填写说明、删除失效标签）时不知道自己在第几条、还剩多少，鼠标用户要逐条走
/// 只能回清单点行，属同壳不同行为。
/// 落点数学与位置指示收进 <see cref="ListAdvance"/> 单一口径（不依赖 WinForms、与测试工程共享编译，
/// 真值表在 Linux 真执行）；界面接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）。
/// </summary>
public class SlotNavTests
{
    // ---------- 文案（真执行） ----------

    [Fact]
    public void Labels_AreTheSharedConstants()
    {
        // 与冻结页面逐字一致：按钮上写的就是「上一条」「下一条」。
        Assert.Equal("上一条", ListAdvance.PrevLabel);
        Assert.Equal("下一条", ListAdvance.NextLabel);
        // 当前这一条不在可见清单里时那句话，与冻结页面 showSlot 的「不在当前筛选中」逐字一致。
        Assert.Equal("不在当前筛选中", ListAdvance.NotInFilterText);
    }

    // ---------- 位置指示（真执行） ----------

    [Fact]
    public void PositionText_EmptyListSaysNothing()
    {
        // 一行都没有时留空：那一屏由提示出口说话，位置指示再讲「第 0 条」是把不存在的行说成还能走。
        Assert.Equal("", ListAdvance.PositionText(0, 0));
        Assert.Equal("", ListAdvance.PositionText(-1, 0));
    }

    [Fact]
    public void PositionText_OutOfViewSaysSo()
    {
        // 没有选中（None）或序号越界：如实说「不在当前筛选中」，不猜第一行。
        Assert.Equal(ListAdvance.NotInFilterText, ListAdvance.PositionText(ListAdvance.None, 5));
        Assert.Equal(ListAdvance.NotInFilterText, ListAdvance.PositionText(5, 5));
        Assert.Equal(ListAdvance.NotInFilterText, ListAdvance.PositionText(-7, 5));
    }

    [Theory]
    [InlineData(0, 1, "第 1 条 / 共 1 条")]
    [InlineData(0, 5, "第 1 条 / 共 5 条")]
    [InlineData(2, 5, "第 3 条 / 共 5 条")]
    [InlineData(4, 5, "第 5 条 / 共 5 条")]
    public void PositionText_GivesPositionWithChineseUnit(int index, int count, string expected)
        => Assert.Equal(expected, ListAdvance.PositionText(index, count));

    // ---------- 落点（真执行） ----------

    [Fact]
    public void Step_StopsAtBothEnds()
    {
        // 到头不绕回：第一条再往前、末条再往后都没有落点（与冻结页面两侧按钮在边界上按不动同一口径）。
        Assert.Equal(ListAdvance.None, ListAdvance.Step(0, -1, 5));
        Assert.Equal(ListAdvance.None, ListAdvance.Step(4, 1, 5));
        // 单行清单两侧都按不动。
        Assert.Equal(ListAdvance.None, ListAdvance.Step(0, -1, 1));
        Assert.Equal(ListAdvance.None, ListAdvance.Step(0, 1, 1));
    }

    [Fact]
    public void Step_WithoutACurrentRowDoesNotGuessTheFirst()
    {
        // 没有当前行（None 或序号越界）时也不猜第一行：那一步的下一步是先在清单上选一行。
        Assert.Equal(ListAdvance.None, ListAdvance.Step(ListAdvance.None, 1, 5));
        Assert.Equal(ListAdvance.None, ListAdvance.Step(ListAdvance.None, -1, 5));
        Assert.Equal(ListAdvance.None, ListAdvance.Step(5, 1, 5));
        Assert.Equal(ListAdvance.None, ListAdvance.Step(-3, 1, 5));
        // 空清单没有落点。
        Assert.Equal(ListAdvance.None, ListAdvance.Step(0, 1, 0));
        Assert.Equal(ListAdvance.None, ListAdvance.Step(0, -1, 0));
    }

    [Fact]
    public void Step_ZeroDeltaIsNotAStep()
    {
        // delta 为 0 不是一次前进/后退，同样没有落点（不把「原地不动」当成一次移动）。
        Assert.Equal(ListAdvance.None, ListAdvance.Step(2, 0, 5));
    }

    [Fact]
    public void Step_MovesOneRowInBothDirections()
    {
        Assert.Equal(1, ListAdvance.Step(0, 1, 5));
        Assert.Equal(3, ListAdvance.Step(2, 1, 5));
        Assert.Equal(1, ListAdvance.Step(2, -1, 5));
        Assert.Equal(3, ListAdvance.Step(4, -1, 5));
    }

    /// <summary>
    /// 落点与位置指示同一口径的全量核对：有落点就一定是清单里真实存在的一条（位置指示说得出来），
    /// 没有落点就一定是边界（第一条往前、末条往后、没有当前行、空清单）——多一分是走到不存在的行，
    /// 少一分是到头了还能按。
    /// </summary>
    [Fact]
    public void Step_AndPositionText_AgreeOnEveryRow()
    {
        for (var count = 0; count <= 8; count++)
        {
            for (var index = -2; index <= count + 1; index++)
            {
                foreach (var delta in new[] { -1, 1 })
                {
                    var target = ListAdvance.Step(index, delta, count);
                    var hasCurrent = index >= 0 && index < count;
                    var inRange = index + delta >= 0 && index + delta < count;
                    if (hasCurrent && inRange)
                    {
                        Assert.Equal(index + delta, target);
                        // 落点说得出来是哪一条（不是「不在当前筛选中」）。
                        Assert.NotEqual(ListAdvance.NotInFilterText, ListAdvance.PositionText(target, count));
                        Assert.NotEqual("", ListAdvance.PositionText(target, count));
                    }
                    else
                    {
                        Assert.Equal(ListAdvance.None, target);
                    }
                }
            }
        }
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_BuildsTheNavRowFromTheSharedLabels()
    {
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        // 两个按钮经工厂出厂、文案取共享常量（界面不就地写「上一条」「下一条」）。
        Assert.Matches(@"(?m)^\s*_prevButton = Ui\.Ghost\(ListAdvance\.PrevLabel\);$", source);
        Assert.Matches(@"(?m)^\s*_nextButton = Ui\.Ghost\(ListAdvance\.NextLabel\);$", source);
        // 导航行是「按钮 + 位置指示 + 按钮」一行，停靠底部、面板底。
        Assert.Matches(@"(?m)^\s*_navRow = new FlowLayoutPanel$", source);
        Assert.Matches(@"(?m)^\s*Dock = DockStyle\.Bottom,$", source);
        Assert.Matches(@"(?m)^\s*FlowDirection = FlowDirection\.LeftToRight,$", source);
        Assert.Matches(@"(?m)^\s*_navRow\.Controls\.Add\(_prevButton\);$", source);
        Assert.Matches(@"(?m)^\s*_navRow\.Controls\.Add\(_navText\);$", source);
        Assert.Matches(@"(?m)^\s*_navRow\.Controls\.Add\(_nextButton\);$", source);
        // 点击落在 MoveSlot 一处（±1），不各写一套。
        Assert.Matches(@"(?m)^\s*_prevButton\.Click \+= \(_, _\) => MoveSlot\(-1\);$", source);
        Assert.Matches(@"(?m)^\s*_nextButton\.Click \+= \(_, _\) => MoveSlot\(1\);$", source);
    }

    [Fact]
    public void NavRow_SitsDirectlyAboveTheDetailBox()
    {
        // 停靠按集合倒序生效：导航行排在详情框之后加入，倒序生效后正好落在详情框正上方
        // （那一行讲的就是详情区里这一条，与冻结页面的「‹ 上一条 / N / M / 下一条 ›」同一位置）。
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        var detail = source.IndexOf("Controls.Add(detailHost);", StringComparison.Ordinal);
        var nav = source.IndexOf("Controls.Add(_navRow);", StringComparison.Ordinal);
        var status = source.IndexOf("Controls.Add(statusHost);", StringComparison.Ordinal);
        Assert.True(detail >= 0 && nav > detail && status > nav, "导航行要排在详情框之后、状态行之前加入。");
    }

    [Fact]
    public void MoveSlot_UsesTheSharedStepAndTheSingleSelectionEntry()
    {
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        var move = Slice(source, "private void MoveSlot(int delta)", "private void ApplyFilter(");
        // 落点取共享口径（对话框不自己 clamp、不自己算 index + delta）。
        Assert.Matches(@"(?m)^\s*var newIndex = ListAdvance\.Step\(index, delta, _list\.Items\.Count\);$", move);
        Assert.DoesNotContain("index + delta", move, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Min", move, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Max", move, StringComparison.Ordinal);
        // 选中经唯一入口（Selected + Focused + 滚入可见区），不就地设 Selected、不自己滚。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(_list\.Items\[newIndex\]\);$", move);
        Assert.DoesNotContain(".Selected = true", move, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", move, StringComparison.Ordinal);
        // 只移动选中：不重建清单、不写数据、不自己问详情（选中事件随即问到）。
        Assert.DoesNotContain("ApplyFilter", move, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDetail", move, StringComparison.Ordinal);
        Assert.DoesNotContain("AddManualAsync", move, StringComparison.Ordinal);
        // 键盘留在按钮上（这一行是给鼠标用户逐条走的），不交回清单。
        Assert.DoesNotContain("KeepKeyboardOnQueue", move, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus()", move, StringComparison.Ordinal);
    }

    [Fact]
    public void NavState_IsRefreshedFromTheDetailPathOnly()
    {
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        var nav = Slice(source, "private void UpdateNav()", "private void ShowDetail()");
        // 门控与位置指示都取共享口径，导航行随「一行都没有」整行收掉。
        Assert.Matches(@"(?m)^\s*_prevButton\.Enabled = ListAdvance\.Step\(index, -1, count\) >= 0;$", nav);
        Assert.Matches(@"(?m)^\s*_nextButton\.Enabled = ListAdvance\.Step\(index, 1, count\) >= 0;$", nav);
        Assert.Matches(@"(?m)^\s*_navText\.Text = ListAdvance\.PositionText\(index, count\);$", nav);
        Assert.Matches(@"(?m)^\s*_navRow\.Visible = count > 0;$", nav);
        // 详情区每次问到详情时一并问导航态：两个分支各一次（有选中 / 没有选中）。
        var detail = Slice(source, "private void ShowDetail()", "private void SortBy(");
        Assert.Equal(2, Regex.Matches(detail, @"(?m)^\s*UpdateNav\(\);$").Count);
        // 整份数据不可得那一屏也经同一处刷新，不就地逐控件关一遍。
        var hint = Slice(source, "private void ShowHint(", "private void UpdateCounts()");
        Assert.Matches(@"(?m)^\s*UpdateNav\(\);$", hint);
        Assert.DoesNotContain("_prevButton.Enabled = false", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("_nextButton.Enabled = false", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("_navText.Text = \"\"", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_DoesNotSpellTheLabelsOrThePositionTextItself()
    {
        // 文案与位置指示只在共享口径一处：界面不就地拼「第 N 条 / 共 M 条」、不就地写「不在当前筛选中」。
        var code = CodeLines(Repo.Source("Desktop", "Native", "ScanResultsDialog.cs"));
        Assert.DoesNotContain("上一条", code, StringComparison.Ordinal);
        Assert.DoesNotContain("下一条", code, StringComparison.Ordinal);
        Assert.DoesNotContain("不在当前筛选中", code, StringComparison.Ordinal);
        Assert.DoesNotContain("第 ", code, StringComparison.Ordinal);
        Assert.DoesNotContain("共 ", code, StringComparison.Ordinal);
        // 落点与位置指示只在共享口径一处：对话框里 Step 恰三处调用（两个按钮门控 + 一次落点）、
        // PositionText 恰一处（位置指示），不另写一套。
        Assert.Equal(3, Regex.Matches(code, "ListAdvance\\.Step").Count);
        Assert.Single(Regex.Matches(code, "ListAdvance\\.PositionText").Cast<Match>());
    }

    [Fact]
    public void NavState_StaysInTheDialogAndIsNotPersisted()
    {
        // 导航态是这一屏的呈现状态：不落盘、不写任务与配置数据（跨启动记忆只有两个常驻页面）。
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        Assert.DoesNotContain("ListSortMemory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListAdvance.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSlotNavigation()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("注释缺口清单的上一条/下一条", section);
        Assert.Contains("`ListAdvance`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("注释缺口清单的上一条/下一条（§6）", ledger);
        Assert.Contains("ListAdvance.Step", ledger);
    }

    // ---------- 辅助 ----------

    /// <summary>只算代码行：注释里引用「界面不就地写『上一条』」是在说明理由，不算第二套文案。</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
