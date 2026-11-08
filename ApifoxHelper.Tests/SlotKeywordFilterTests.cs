using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释缺口清单的关键词框（第六十六轮，DESIGN.md §6「列表筛选」）：这一屏是全应用最长的一张清单
/// ——它列出一次扫描的全部注释位置（模块 × 符号 × 标注点），方案 §4.2 记录千级接口分支，
/// 此前只有状态下拉，找「上次那个位置」只能逐行扫。另两张长清单（检查结果、差异确认）在第六十五轮
/// 补过同一个框，冻结页面的差异视图一直有关键词框：「同一个动作（在长清单里按关键词找一行）
/// 在主页有、在对话框没有，属同壳不同行为」，故按同一份口径收口——占位文字、匹配规则与
/// 「筛到零行那句话」都从 <see cref="ListFilter"/> 取，三个对话框不各写一套。
/// 关键词框不是表单字段：输入即筛选，框里的裸 Enter 不提交对话框（另两张长清单与冻结页面同样什么都不做）。
/// 真机上的输入手感、按键路由与五列筛选行的排布随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class SlotKeywordFilterTests
{
    private const string File = "ScanResultsDialog.cs";

    // ---------- 占位文字：单一落点，说的是这一屏的东西 ----------

    [Fact]
    public void Placeholder_SaysWhatThisListHolds_AndIsTakenFromTheSharedConstant()
    {
        Assert.Equal("筛选位置（符号、标注、角色、文件）", ListFilter.SlotSearchPlaceholder);
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.SlotSearchPlaceholder;$", source);
        // 占位文字不在界面就地拼写：整份源码里只有 ListFilter 那一处字面。
        foreach (var other in Repo.NativeSources())
        {
            Assert.Equal(other.Name == "ListFilter.cs" ? 1 : 0, CodeLinesContaining(other.Text, ListFilter.SlotSearchPlaceholder));
        }
    }

    [Fact]
    public void Placeholder_IsDistinctFromTheOtherThreeLists()
    {
        // 四张清单找的不是同一样东西：并成一份等于把「找位置」说成「找接口」。
        Assert.NotEqual(ListFilter.SlotSearchPlaceholder, ListFilter.EndpointSearchPlaceholder);
        Assert.NotEqual(ListFilter.SlotSearchPlaceholder, ListFilter.DecisionSearchPlaceholder);
        Assert.NotEqual(ListFilter.SlotSearchPlaceholder, ListFilter.TaskPlaceholder);
        Assert.NotEqual(ListFilter.SlotSearchPlaceholder, ListFilter.ProfilePlaceholder);
    }

    // ---------- 接线：框、形态、输入即筛选 ----------

    [Fact]
    public void KeywordBox_IsBuiltByTheSharedFactory_AndFiltersAsYouType()
    {
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        // 宽度取工厂常量（第六十八轮）：五张长清单的框一样宽，界面不各写一个数。
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => ApplyFilter\(\);$", source);
    }

    [Fact]
    public void FilterRow_CarriesBothEntries_AndKeepsTheCoverageRowSpanning()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 与另四张长清单同一形状：筛选 | 状态下拉 | 关键词 | 关键词框 | 计数——形状取工厂（第六十八轮）。
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_filter, _keyword, _counts\);$", source);
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        // 行里的三件东西不就地加（覆盖率行仍由本屏自己加在第二行）。
        Assert.DoesNotContain("filterRow.Controls.Add(_filter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(Ui.Field(_keyword)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(_counts", source, StringComparison.Ordinal);
        // 覆盖率行原来跨三列，加了关键词框要跟着跨五列——漏改就是覆盖率行只盖住前三格。
        Assert.Matches(@"(?m)^\s*filterRow\.SetColumnSpan\(_coverage, 5\);$", source);
    }

    // ---------- 匹配的是屏幕上那几格，不另造一套字段 ----------

    [Fact]
    public void Keyword_MatchesTheCellsTheRowShows()
    {
        var source = Repo.Source("Desktop", "Native", "SlotText.cs");
        // 符号、标注、角色、文件位置——这一行在屏幕上被读作的那几格。
        Assert.Matches(@"(?m)^\s*public static string\?\[\] KeywordFields\(DocSlot slot\) =>$", source);
        Assert.Matches(@"(?m)^\s*\[Name\(slot\), Tag\(slot\), TaskDisplay\.SlotRoleText\(slot\.Role\), Location\(slot\)\];$", source);
        // 状态不在关键词里：那一列的取值由筛选下拉管（缺失/空/已失效/有效/其他问题），不必再敲一遍。
        Assert.DoesNotContain("SlotStatusText(slot.Status)], ", source, StringComparison.Ordinal);
        // 清单经这份口径接上，不在界面里另拼一套（第一百零二轮起这份筛选链是 ListSort.Order 的输入，
        // 与两个主页面、检查结果、接口追踪明细、差异确认、注释候选、响应契约同一顺序：筛选先于排序）。
        var dialog = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", dialog);
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(_slots, _keyword\.Text, SlotText\.KeywordFields\)\.Where\(Matches\)\.ToList\(\),$", dialog);
    }

    [Fact]
    public void Cells_AndFilterFields_ReadTheSameText_NotTwoCopies()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 筛选字段与单元格取同一处：两处各拼一次，必然有一处先漂。
        Assert.Matches(@"(?m)^\s*var item = new ListViewItem\(SlotText\.Name\(slot\)\) \{ Tag = slot \};$", source);
        Assert.Matches(@"(?m)^\s*item\.SubItems\.Add\(SlotText\.Tag\(slot\)\);$", source);
        Assert.Matches(@"(?m)^\s*var location = item\.SubItems\.Add\(SlotText\.Location\(slot\)\);$", source);
        // 每一处拼装在整个原生界面只出现一次（就地拼第二次即断锚）。
        Assert.Equal(1, Occurrences(NativeText(), "string.IsNullOrWhiteSpace(slot.Display) ? slot.SymbolId : slot.Display"));
        Assert.Equal(1, Occurrences(NativeText(), "slot.File + \":\" + slot.Line"));
        Assert.Equal(1, Occurrences(NativeText(), "TaskDisplay.SlotTagText(slot.Tag) + (slot.Tag == \"param\""));
    }

    // ---------- 关键词与状态下拉都是「且」 ----------

    [Fact]
    public void Keyword_AndStatusFilter_AreBothAnd()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 关键词先经共享口径（与两个主页面、另两张长清单同一份匹配规则），状态筛选在其后：
        // 两个条件都是「且」，顺序不影响结果，但匹配规则只有一份；这一份筛选链自第一百零二轮起
        // 作为 ListSort.Order 的输入（筛选先于排序），两个条件仍都在排序之前生效。
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(_slots, _keyword\.Text, SlotText\.KeywordFields\)\.Where\(Matches\)\.ToList\(\),$", source);
        // 状态下拉的判定仍是原来那一份，不因加了关键词而另写一套。
        Assert.Matches(@"(?m)^\s*private bool Matches\(DocSlot slot\) => _filter\.SelectedIndex switch$", source);
        Assert.Matches(@"(?m)^\s*5 => slot\.Status is ""inheritDoc"" or ""inheritDocUnresolved"" or ""invalidXml"" or ""unsupported"",$", source);
    }

    // ---------- 两个筛选入口一起启停 ----------

    [Fact]
    public void BothEntries_StartAndStopTogether()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 整份数据不可得（读取失败/没有扫描产物）时两个入口一起停：只停一个等于留一个按不动的入口。
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_filter\.Enabled = false;$"));
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = false;$"));
        // 数据可读时两个入口一起开。
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_filter\.Enabled = true;$"));
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = true;$"));
    }

    [Fact]
    public void FilteredEmpty_LeavesBothEntriesUsable()
    {
        // 筛到零行那句话指名的下一步就是这两个入口：把它们按下去，等于把恢复路径一起收掉
        // （§7「禁用不得移除下一步」，第四十二轮那条纪律）。
        var body = Slice("private void ShowFilteredEmpty()", "private void ShowDetail()");
        Assert.DoesNotMatch(@"(?m)^\s*_filter\.Enabled", body);
        Assert.DoesNotMatch(@"(?m)^\s*_keyword\.Enabled", body);
    }

    // ---------- 筛到零行那句话：有关键词框时下一步有两个真实入口 ----------

    [Fact]
    public void FilteredEmptyHint_NamesBothEntries_WhenThereIsAKeyword()
    {
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*_hint\.Text = ListFilter\.FilteredEmptyHint\(ListFilter\.SlotNoun, ListFilter\.IsActive\(_keyword\.Text\)\);$", source);
        // 关键词为空时仍只指筛选器：文字不指向界面上按不动的控件。
        Assert.EndsWith(ListFilter.FilteredEmptySearchPointer, ListFilter.FilteredEmptyHint(ListFilter.SlotNoun, true), StringComparison.Ordinal);
        Assert.EndsWith(ListFilter.FilteredEmptyPointer, ListFilter.FilteredEmptyHint(ListFilter.SlotNoun, false), StringComparison.Ordinal);
    }

    // ---------- 关键词框不是表单字段：裸 Enter 不提交对话框 ----------

    [Fact]
    public void KeywordBox_EnterDoesNotSubmitTheDialog()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 对话框的 Enter=确认说的是表单字段；筛选框输入即筛选、没有「提交」这一步，
        // 不挡一下按 Enter 会把刚筛出来的结果连同对话框一起关掉。第七十轮起这一次按键不再什么都不做：
        // 与两个主页面同一手势，把焦点与选中交给筛选结果第一行（共享入口 Ui.TryEnterFilter，见 FilterEnterTests）。
        Assert.Matches(@"(?m)^\s*protected override bool ProcessDialogKey\(Keys keyData\)$", source);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, _list\)\) return true;$", source);
        Assert.Matches(@"(?m)^\s*return base\.ProcessDialogKey\(keyData\);$", source);
        // 只挡裸 Enter：带修饰键的组合照旧交给基类，不吞键；判定只有共享入口一份，不就地再写一条。
        Assert.DoesNotContain("Keys.Enter |", source, StringComparison.Ordinal);
        Assert.DoesNotContain("keyData == Keys.Enter", source, StringComparison.Ordinal);
    }

    // ---------- 计数行仍按全部条目统计 ----------

    [Fact]
    public void CountsRow_StillCountsEverySlot_NotTheFilteredOnes()
    {
        // 聚合计数不随筛选变化（第五十九轮）：「位置 128 · 有效 12 · …」是整份扫描的凭据，
        // 被当前筛选抹掉等于把结果藏起来。
        var body = Slice("private void UpdateCounts()", "private bool Matches");
        Assert.Contains("foreach (var slot in _slots)", body, StringComparison.Ordinal);
        Assert.Contains("_slots.Count", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_list.Items.Count", body, StringComparison.Ordinal);
        Assert.DoesNotContain("visible", body, StringComparison.Ordinal);
    }

    // ---------- 口径是真执行：跑的是主工程同一份 ListFilter ----------

    [Fact]
    public void SlotFields_CombineByTheSharedRules()
    {
        // 与 ListFilter.Apply 同一份规则（词间「且」、字段间「或」、忽略大小写、空关键词即全部行）。
        var rows = new[]
        {
            (Name: "OrderService.CreateOrder", Tag: "参数 orderId", Role: "请求", File: "Services/OrderService.cs:42"),
            (Name: "UserService.GetUser", Tag: "说明", Role: "接口", File: "Services/UserService.cs:17"),
        };
        // 一个词命中即中（字段间「或」）。
        Assert.Single(ListFilter.Apply(rows, "orderId", r => [r.Name, r.Tag, r.Role, r.File]));
        Assert.Single(ListFilter.Apply(rows, "UserService", r => [r.Name, r.Tag, r.Role, r.File]));
        // 词间「且」：两个词都要各自落在至少一个字段里，两个词落在同一行才算。
        Assert.Single(ListFilter.Apply(rows, "order 请求", r => [r.Name, r.Tag, r.Role, r.File]));
        Assert.Single(ListFilter.Apply(rows, "User 接口", r => [r.Name, r.Tag, r.Role, r.File]));
        // 「order」在第一行、「接口」在第二行：没有任何一行同时满足两个词，故一行都不剩。
        Assert.Empty(ListFilter.Apply(rows, "order 接口", r => [r.Name, r.Tag, r.Role, r.File]));
        // 空关键词即全部行：清空框回到整份清单。
        Assert.Equal(2, ListFilter.Apply(rows, "", r => [r.Name, r.Tag, r.Role, r.File]).Count);
    }

    // ---------- 纯呈现层：不写数据 ----------

    [Fact]
    public void ThisScreen_StaysReadOnly()
    {
        // 关键词框只改这一屏看得见哪些行：不写任务与配置数据、不改审阅状态、服务层一行未触达。
        var body = Slice("private void ApplyFilter(", "private void ShowFilteredEmpty()");
        Assert.DoesNotContain("_tasks.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_drafts.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AddManualAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", body, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static string Slice(string start, string end)
    {
        var source = Repo.Source("Desktop", "Native", File);
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }

    /// <summary>该字面量在源码里出现的次数。</summary>
    private static int Occurrences(string haystack, string needle) => haystack.Split(needle).Length - 1;

    /// <summary>整个原生界面的源码拼起来（跨文件统计「只拼一次」时用）。</summary>
    private static string NativeText() => string.Join('\n', Repo.NativeSources().Select(s => s.Text));

    /// <summary>出现该字面量的代码行数（注释行跳过：注释里引用这句话是说明理由，不是第二套拼写）。</summary>
    private static int CodeLinesContaining(string text, string fragment) =>
        text.Split('\n').Count(l =>
        {
            var trimmed = l.TrimStart();
            return trimmed.Contains(fragment, StringComparison.Ordinal)
                && !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal);
        });
}
