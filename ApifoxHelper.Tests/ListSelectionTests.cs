using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「读当前选中的那一行」收口断言（DESIGN.md §6「列表行」、§8 本轮条款）：
/// 第六十轮如实挂账「ShowEvidence 等按钮回调读 SelectedItems[0] 的空选中守卫与否属另一件事，不扩到全界面」，本轮做那件事。
/// 基线里的形状可核对：界面 22 处先量选中数才索引、3 处取选中序号，另有 4 处直接写 <c>SelectedItems[0]?.</c>——
/// 把「无选中时这一次点击发生什么」交给索引器的越界语义（返回 null 还是抛异常）。
/// 本环境既无 WinForms 运行时、又无网络可核对官方源码（本轮实测：curl 到 raw.githubusercontent 不通），
/// 两种语义都不能验证：猜返回 null 就是第四十轮修过的「按钮残留启用、点击无响应」同一族，
/// 猜不抛就是那一次点击变成未处理异常。同壳不同行为，按第五十五、五十七、五十九轮的同一条理由判定收口。
/// 现收成一处：位次判定在 <see cref="ListSelection"/>（不依赖 WinForms、与测试工程共享编译、下列真值表真执行），
/// 索引只在 <c>Ui.SelectedItem</c> / <c>Ui.SelectedIndex</c> 两个唯一入口发生，量选中数也只在 Ui.cs 发生——
/// 与第五十二轮「剪贴板只触达一次」、第五十七轮「滚动只触达一次」、第五十八轮「交回键盘只触达一次」同一做法。
/// 列表一律经 Ui.List 工厂出厂且 MultiSelect = false，选中数最多为 1，故「第一格」就是「那一行」，不猜其余行。
/// 实际按键与鼠标在真机上走哪条路随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListSelectionTests
{
    // ---------- 口径真值表：量到选中数才有那一格 ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void OneRowIsThereOnlyWhenSomethingIsSelected(int selectedCount)
    {
        // 有选中：可取的位次是选中集合的第一格。
        Assert.Equal(0, ListSelection.FirstPosition(selectedCount));
    }

    [Theory]
    [InlineData(0)]
    public void NothingIsSelected_NoRowIsTaken(int selectedCount)
    {
        // 没有选中就没有「那一行」：返回 null，调用方不得碰索引器。
        Assert.Null(ListSelection.FirstPosition(selectedCount));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AnImpossibleCountIsTreatedAsNoSelection(int selectedCount)
    {
        // 负数不可发生，但发生了也不猜它指向哪一格
        // （与第五十三轮「声明的列落在格数之外时宁可复制不到」、第五十六轮「一行不剩则没有落点」同一取舍）。
        Assert.Null(ListSelection.FirstPosition(selectedCount));
    }

    [Fact]
    public void Rule_GrantsTheRowExactlyWhenTheSelectionIsNotEmpty()
    {
        // 逐点核对判定窗口：放行当且仅当「选中数大于零」，且放行时给的位次恒为 0。
        // 多一分是把无选中当成有选中（界面对空集合下手，正是本轮要断掉的那件事），
        // 少一分是有选中时答不出那一行。
        for (var count = -3; count <= 200; count++)
        {
            var position = ListSelection.FirstPosition(count);
            Assert.Equal(count > 0 ? 0 : (int?)null, position);
        }
    }

    [Fact]
    public void Rule_MustNotBeRestatedPerSelectionKind()
    {
        // 「有没有选中」只有一条规则：取行与取序号共用同一个判定，不各写一份 > 0
        // （两份必然漂移，与第六十轮「在出口里再写一遍四个赋值就是第二套」同一理由）。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Equal(2, Regex.Matches(ui, @"(?m)ListSelection\.FirstPosition\(").Count);
    }

    // ---------- 唯一入口：先量后索引 ----------

    private static string Entry(string signature)
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var match = Regex.Match(ui, @"(?m)^    " + Regex.Escape(signature) + @" =>\s*\n(?<body>[^;]*);");
        Assert.True(match.Success, $"Ui 缺少 {signature} 读选中入口。");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void RowEntry_CountsBeforeItIndexes()
    {
        var body = Entry("public static ListViewItem? SelectedItem(ListView list)");
        Assert.Contains("ListSelection.FirstPosition(list.SelectedItems.Count)", body);
        Assert.Contains("list.SelectedItems[position]", body);
        Assert.Contains(": null", body, StringComparison.Ordinal);
        // 位次来自判定，不是就地写的 0——写死 0 就绕过了「先量」这一步，本入口就成了第二套猜法。
        Assert.DoesNotContain("SelectedItems[0]", body, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexEntry_CountsBeforeItIndexesAndReadsTheSelectedCollection()
    {
        var body = Entry("public static int? SelectedIndex(ListView list)");
        Assert.Contains("ListSelection.FirstPosition(list.SelectedIndices.Count)", body);
        Assert.Contains("list.SelectedIndices[position]", body);
        Assert.Contains(": null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndices[0]", body, StringComparison.Ordinal);
        // 取的是选中集合第一格指向的显示序号，不是选中数本身。
        Assert.DoesNotContain("selectedCount", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Entries_ResolveToTheSameRow()
    {
        // 行入口读 SelectedItems、序号入口读 SelectedIndices，两份集合按同一位次取——
        // 一个用 Items[i]、另一个用 SelectedIndices[i] 就是两套落点。
        var row = Entry("public static ListViewItem? SelectedItem(ListView list)");
        var index = Entry("public static int? SelectedIndex(ListView list)");
        Assert.Contains("list.SelectedItems.Count", row);
        Assert.Contains("list.SelectedIndices.Count", index);
        Assert.DoesNotContain("Items[", index, StringComparison.Ordinal);
    }

    [Fact]
    public void Entries_DoNotGuessTheFirstRowWhenNothingIsSelected()
    {
        // 无选中时不借第一行：那会把没被选中的那一行当成用户正在操作的那一行
        // （第三十九轮「命中哪行激活哪行」、第五十一轮「焦点行缺失即不动作」同一纪律）。
        foreach (var body in new[]
                 {
                     Entry("public static ListViewItem? SelectedItem(ListView list)"),
                     Entry("public static int? SelectedIndex(ListView list)"),
                 })
        {
            Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
            Assert.DoesNotContain(".Focus", body, StringComparison.Ordinal);
            Assert.DoesNotContain("FocusedItem", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KeyboardEntry_DoesNotCountTheSelectionByItself()
    {
        // 第六十轮基线里 Ui 自己也写过一份「量选中数再取第一格」的三元式；入口出现后它必须走入口，
        // 否则判定就有两处，第五十八轮的「序号是同一行才交键盘」会跟着漂移。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var helper = Regex.Match(ui, @"public static void KeepKeyboardOnQueue\(ListView list\)\s*\{(?<body>[^}]*)\}");
        Assert.True(helper.Success, "Ui 缺少 KeepKeyboardOnQueue。");
        Assert.Matches(@"(?m)^\s*var selected = SelectedIndex\(list\);$", helper.Groups["body"].Value);
    }

    // ---------- 全界面扫描：界面不自己索引选中集合 ----------

    [Fact]
    public void NoView_IndexesTheSelectionItself()
    {
        // 本轮的总不变量：Desktop 下选中集合的索引只发生在 Ui.cs 的两个入口里，各一处。
        // 任何一处回到 SelectedItems[0] 都算红——那正是本轮断掉的那件事（含把返回值当成可空的赌法）。
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            Assert.DoesNotContain("SelectedItems[", source.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("SelectedIndices[", source.Text, StringComparison.Ordinal);
        }

        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Single(Regex.Matches(ui, @"list\.SelectedItems\["));
        Assert.Single(Regex.Matches(ui, @"list\.SelectedIndices\["));
    }

    [Fact]
    public void NoView_CountsTheSelectionItself()
    {
        // 「有没有选中」与「是哪一行」是同一个问题，量选中数也只在入口发生（两页的按钮启用判定于本轮跟上）。
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            Assert.DoesNotContain("SelectedItems.Count", source.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("SelectedIndices.Count", source.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SelectionTouchpoints_AreOnlyTheTwoEntries()
    {
        // 判定只有一份：ListSelection.FirstPosition 的调用者只有 Ui.cs。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListSelection.FirstPosition"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    [Fact]
    public void Rule_LivesWithoutWinForms()
    {
        // 判定必须能在 Linux 真执行：口径文件不碰 WinForms、不碰控件集合类型。
        var rule = Repo.Source("Desktop", "Native", "ListSelection.cs");
        Assert.DoesNotContain("System.Windows.Forms", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItems[", rule, StringComparison.Ordinal);
    }

    // ---------- 接线点名：两条老语义已断 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs", "CommentCandidate")]
    [InlineData("ScanResultsDialog.cs", "DocSlot")]
    public void EvidenceButtons_ReadTheSelectedRowThroughTheEntry(string file, string payload)
    {
        // 第六十轮点名的那四处里，两处是按钮回调直接索引：现在读的是入口给出的那一行。
        var source = Repo.Source("Desktop", "Native", file);
        Assert.Matches(
            @"(?m)^\s*_evidenceButton\.Click \+= \(_, _\) => ShowEvidence\(Ui\.SelectedItem\(_list\)\?\.Tag as " + payload + @"\);",
            source);
        // 旧的赌法不得回来。
        Assert.DoesNotContain("SelectedItems[0]?.", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ScanResultsDialog.cs", "ShowManualFill")]
    [InlineData("ScanResultsDialog.cs", "AddRemoveStale")]
    public void WritingEntryButtons_ReadTheSelectedRowThroughTheEntry(string file, string method)
    {
        // 另两处在会写任务数据的动作入口上（手工填写、删除失效标签）：
        // 那里赌错一次比只读视图更贵——取到 null 走的就是「没有这一步」，取到别行则写错条目。
        var source = Repo.Source("Desktop", "Native", file);
        var body = Regex.Match(source, @"private void " + method + @"\(\)\s*\{(?<body>[^}]*)\}");
        Assert.True(body.Success, $"{file} 缺少 {method}。");
        Assert.Matches(@"(?m)^\s*if \(Ui\.SelectedItem\(_list\)\?\.Tag is not \w+ \w+\) return;$", body.Groups["body"].Value);
        Assert.DoesNotContain("SelectedItems[0]?.", body.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySelectionBranch_StillCollapsesThroughTheExistingPath()
    {
        // 注释缺口的「没有选中怎么收掉」分支（第三十九、四十、六十轮立的收口）动作逐字不变，
        // 本轮只把它的问法改成入口——不得顺手写第二套收控件。
        var scan = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        var body = Regex.Match(scan, @"private void ShowDetail\(\)\s*\{(?<body>[^}]*)\}");
        Assert.True(body.Success, "ScanResultsDialog 缺少 ShowDetail 的无选中分支。");
        Assert.Matches(@"(?m)^\s*if \(Ui\.SelectedItem\(_list\) is null\)$", body.Groups["body"].Value);
        Assert.Matches(@"(?m)^\s*_detail\.Text = """";$", body.Groups["body"].Value);
        Assert.Equal(3, Regex.Matches(body.Groups["body"].Value, @"(?m)^\s*_\w+Button\.Enabled = false;$").Count);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs", "Ui.SelectedIndex(_list) ?? ListAdvance.None")]
    [InlineData("ContractsDialog.cs", "Ui.SelectedIndex(_list) ?? ListAdvance.None")]
    [InlineData("DecisionsDialog.cs", "Ui.SelectedIndex(_list) ?? ListAdvance.None")]
    public void ReviewPaths_RecordTheHandledRowThroughTheIndexEntry(string file, string wiring)
    {
        // 续接落点要的是「被处理那一行的显示序号」，走序号入口；缺省 ListAdvance.None 讲的就是
        // 「没有刚处理的一条」，不是随手补的兜底（ListAdvance 对该值从队列开头继续）。
        var source = Repo.Source("Desktop", "Native", file);
        // 记序号在这一条路上只发生一次：两处取序号就是两套落点，第二套必然与第一套漂移。
        Assert.Single(Regex.Matches(source, Regex.Escape(wiring)));
        Assert.Contains(wiring, source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndices[0]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPages_EnableTheirButtonsFromTheSameJudgement()
    {
        // 配置页「编辑」「连接与写入探测」此前自己量 SelectedIndices.Count——同一个问题的第三种问法。
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Equal(2, Regex.Matches(projects, @"(?m)^\s*_\w+Button\.Enabled = Ui\.SelectedIndex\(_list\) is not null;$").Count);
    }

    [Fact]
    public void ListsAreSingleSelect_SoTheFirstSlotIsThatRow()
    {
        // 「取第一格」成立的前提：列表一律经工厂出厂且关掉多选。哪天放出多选，
        // 本入口取的就只是其中一行，那句话得改——由这条用例拦住。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Single(Regex.Matches(ui, @"(?m)^\s*MultiSelect = false,$"));
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void SharedCompile_IncludesListSelection()
    {
        // 真值表跑的是主工程同一份代码：共享编译清单里必须有 ListSelection.cs。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListSelection.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsTheSingleEntry()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**读当前选中的那一行**（`ListSelection` 单一口径，2026-11-08 第六十一轮）", section);
        Assert.Contains("索引器因此在无选中时不被触发", section);
        Assert.Contains("`Ui.SelectedItem`（取行）与 `Ui.SelectedIndex`（取显示序号）两个唯一入口", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 选中行读取（§6） \|", ledger);
        Assert.Contains("第六十一轮", ledger);
        Assert.Contains("20 处先量后索引", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimWhatThisRoundDidNotDo()
    {
        // 本轮没做的事不能被写成做了：无选中时给一句「缺什么」（§7 禁用行的另一半）仍属挂账，
        // 焦点行（键盘通路）也不由本入口管辖。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 选中行读取（§6） |", "\n| 输入框（§6） |");
        Assert.Contains("不新增无选中时「缺什么」那句话", row);
        Assert.Contains("FocusedItem", row);
        Assert.DoesNotContain("无选中说明已接线", row, StringComparison.Ordinal);
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
