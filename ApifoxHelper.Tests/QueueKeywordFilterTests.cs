using System.Text.RegularExpressions;
using ApifoxHelper.Comments;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Source;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 两个审阅队列的关键词框（第六十七轮，DESIGN.md §6「列表筛选」）：注释候选与响应契约是逐条清队列的
/// 两张清单，队列里上百条时找「上次那条」只能逐行扫——此前它们只有状态下拉，是五张带筛选下拉的清单里
/// 最后两张没有关键词框的。另三张（检查结果、差异确认、注释缺口）在第六十五、六十六轮补过同一个框，
/// 冻结页面的差异视图一直有关键词框：「同一个动作（在长清单里按关键词找一行）在主页有、在对话框没有，
/// 属同壳不同行为」，故按同一份口径收口——占位文字、匹配规则与「筛到零行那句话」都从 <see cref="ListFilter"/> 取。
/// 匹配的是那一行在屏幕上被读作的那几格（取法在 <c>QueueText</c>，与清单单元格同源），
/// 审阅状态那一列由筛选下拉管、不进关键词。
/// 关键词框不是表单字段：输入即筛选，框里的裸 Enter 不提交对话框。
/// 真机上的输入手感、按键路由与五列筛选行的排布随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class QueueKeywordFilterTests
{
    // ---------- 占位文字：单一落点，说的是这一屏的东西 ----------

    [Fact]
    public void Placeholders_SayWhatEachQueueHolds_AndAreDistinct()
    {
        Assert.Equal("筛选候选（位置、标注、依据、来源）", ListFilter.CandidateSearchPlaceholder);
        Assert.Equal("筛选契约（接口、封装、类型）", ListFilter.ContractSearchPlaceholder);
        // 五张清单找的不是同一样东西：并成一份等于把「找候选」说成「找契约」。
        Assert.NotEqual(ListFilter.CandidateSearchPlaceholder, ListFilter.ContractSearchPlaceholder);
        Assert.NotEqual(ListFilter.CandidateSearchPlaceholder, ListFilter.EndpointSearchPlaceholder);
        Assert.NotEqual(ListFilter.CandidateSearchPlaceholder, ListFilter.DecisionSearchPlaceholder);
        Assert.NotEqual(ListFilter.CandidateSearchPlaceholder, ListFilter.SlotSearchPlaceholder);
        Assert.NotEqual(ListFilter.ContractSearchPlaceholder, ListFilter.EndpointSearchPlaceholder);
        Assert.NotEqual(ListFilter.ContractSearchPlaceholder, ListFilter.DecisionSearchPlaceholder);
        Assert.NotEqual(ListFilter.ContractSearchPlaceholder, ListFilter.SlotSearchPlaceholder);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs", "CandidateSearchPlaceholder", "候选")]
    [InlineData("ContractsDialog.cs", "ContractSearchPlaceholder", "契约")]
    public void Placeholder_IsTakenFromTheSharedConstant(string file, string constant, string literal)
    {
        var source = Repo.Source("Desktop", "Native", file);
        Assert.Matches($@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.{constant};$", source);
        // 占位文字不在界面就地拼写：整份源码里只有 ListFilter 那一处字面。
        var text = literal == "候选" ? ListFilter.CandidateSearchPlaceholder : ListFilter.ContractSearchPlaceholder;
        foreach (var other in Repo.NativeSources())
        {
            Assert.Equal(other.Name == "ListFilter.cs" ? 1 : 0, CodeLinesContaining(other.Text, text));
        }
    }

    // ---------- 接线：框、形态、输入即筛选 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void KeywordBox_IsBuiltByTheSharedFactory_AndFiltersAsYouType(string file)
    {
        var source = Repo.Source("Desktop", "Native", file);
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        // 宽度取工厂常量（第六十八轮）：五张长清单的框一样宽，界面不各写一个数。
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => ApplyFilter\(\);$", source);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void FilterRow_CarriesBothEntries(string file)
    {
        var source = Repo.Source("Desktop", "Native", file);
        // 与另三张长清单同一形状：筛选 | 状态下拉 | 关键词 | 关键词框 | 计数——形状取工厂（第六十八轮），
        // 界面只交三件东西，不各拼一遍（拼一遍就有一处先漂：检查结果那一屏曾拼成 Flow + 240 宽）。
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_filter, _keyword, _counts\);$", source);
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add", source, StringComparison.Ordinal);
    }

    // ---------- 匹配的是屏幕上那几格，不另造一套字段 ----------

    [Fact]
    public void CandidateFields_AreTheCellsTheRowShows()
    {
        var slot = new DocSlot { Id = "s1", SymbolId = "OrderService.CreateOrder", Display = "CreateOrder", Tag = "param", ParameterName = "orderId" };
        var candidate = new CommentCandidate
        {
            Id = "c1",
            SlotId = "s1",
            SymbolId = "OrderService.CreateOrder",
            Tag = "param",
            ParameterName = "orderId",
            Basis = "code",
            Source = "rule",
            ReviewState = "pending",
        };
        var fields = QueueText.CandidateFields(candidate, slot);
        // 位置、标注、依据、来源——这一行在屏幕上被读作的那几格。
        Assert.Equal("CreateOrder", fields[0]);
        Assert.Equal("参数 orderId", fields[1]);
        Assert.Equal("代码可证实", fields[2]);
        Assert.Equal("规则提取", fields[3]);
        // 审阅状态不在关键词里：那一列的取值由筛选下拉管（待审阅/已接受/已搁置/已驳回/证据已变化）。
        Assert.DoesNotContain("待审阅", fields, StringComparer.Ordinal);
    }

    [Fact]
    public void CandidateFields_FallBackToTheSymbolId_WhenTheSlotIsGone()
    {
        // 注释位置已不在本次扫描结果中时，标题如实显示符号编号（与清单那一格同一取值）。
        var candidate = new CommentCandidate { Id = "c1", SlotId = "missing", SymbolId = "OrderService.CreateOrder", Tag = "summary", Basis = "needsConfirmation", Source = "model" };
        var fields = QueueText.CandidateFields(candidate, slot: null);
        Assert.Equal("OrderService.CreateOrder", fields[0]);
        Assert.Equal("说明", fields[1]);
        Assert.Equal("需业务确认", fields[2]);
        Assert.Equal("模型生成", fields[3]);
    }

    [Fact]
    public void ContractFields_AreTheCellsTheRowShows()
    {
        var contract = new ResponseContract
        {
            EndpointKey = "POST /api/orders",
            WrapperType = "PageResult",
            DataType = "OrderDto",
            ReviewState = "pending",
        };
        var fields = QueueText.ContractFields(contract);
        Assert.Equal("POST /api/orders", fields[0]);
        Assert.Equal("PageResult", fields[1]);
        Assert.Equal("OrderDto", fields[2]);
        // 审阅状态不在关键词里：那一列的取值由筛选下拉管（待确认/已确认/已拒绝/已阻断/已失效）。
        Assert.DoesNotContain("待确认", fields, StringComparer.Ordinal);
    }

    [Fact]
    public void ContractFields_SayTheSameThingTheCellsDo_WhenSomethingIsMissing()
    {
        // 没有封装类型与未确定的类型都如实显示，不留空、不编造。
        var contract = new ResponseContract { EndpointKey = "GET /api/users", WrapperType = "", DataType = null };
        var fields = QueueText.ContractFields(contract);
        Assert.Equal("（直接返回）", fields[1]);
        Assert.Equal("未确定", fields[2]);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs", "QueueText.CandidateFields(c, _slotsById.GetValueOrDefault(c.SlotId))")]
    [InlineData("ContractsDialog.cs", "QueueText.ContractFields")]
    public void TheQueue_IsFilteredThroughTheSharedCaliber(string file, string fields)
    {
        var source = Repo.Source("Desktop", "Native", file);
        // 清单经这份口径接上，不在界面里另拼一套字段。
        Assert.Contains("ListFilter.Apply(", source, StringComparison.Ordinal);
        Assert.Contains(fields, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Cells_AndFilterFields_ReadTheSameText_NotTwoCopies()
    {
        var candidates = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        var contracts = Repo.Source("Desktop", "Native", "ContractsDialog.cs");
        var caliber = Repo.Source("Desktop", "Native", "QueueText.cs");
        // 筛选字段与单元格取同一处：两处各拼一次，必然有一处先漂。
        Assert.Matches(@"(?m)^\s*var item = new ListViewItem\(QueueText\.CandidateTitle\(candidate, slot\)\) \{ Tag = candidate \};$", candidates);
        Assert.Matches(@"(?m)^\s*item\.SubItems\.Add\(QueueText\.CandidateTag\(candidate\)\);$", candidates);
        Assert.Matches(@"(?m)^\s*var wrapper = item\.SubItems\.Add\(QueueText\.ContractWrapper\(contract\.WrapperType\)\);$", contracts);
        Assert.Matches(@"(?m)^\s*item\.SubItems\.Add\(QueueText\.ContractDataType\(contract\.DataType\)\);$", contracts);
        // 每一处拼装在整个原生界面只出现一次（就地拼第二次即断锚）：拼法只在口径文件里。
        Assert.Equal(1, Occurrences(NativeText(), "TaskDisplay.SlotTagText(candidate.Tag) + (candidate.ParameterName is { } name"));
        Assert.Equal(1, Occurrences(NativeText(), "wrapperType.Length > 0 ? wrapperType : \"（直接返回）\""));
        Assert.Equal(1, Occurrences(NativeText(), "dataType ?? \"未确定\""));
        // 两个对话框里都不就地再拼一遍。
        Assert.DoesNotContain("WrapperType.Length > 0", candidates, StringComparison.Ordinal);
        Assert.DoesNotContain("DataType ?? \"未确定\"", contracts, StringComparison.Ordinal);
        Assert.Equal(0, Occurrences(caliber, "System.Windows.Forms"));
    }

    // ---------- 关键词与状态下拉都是「且」 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs", @"ListFilter\.Apply\(_store\.Candidates, _keyword\.Text, c => QueueText\.CandidateFields\(c, _slotsById\.GetValueOrDefault\(c\.SlotId\)\)\)\.Where\(Matches\)\.ToList\(\)")]
    [InlineData("ContractsDialog.cs", @"ListFilter\.Apply\(_contracts, _keyword\.Text, QueueText\.ContractFields\)\.Where\(Matches\)\.ToList\(\)")]
    public void Keyword_AndStatusFilter_AreBothAnd(string file, string chain)
    {
        var source = Repo.Source("Desktop", "Native", file);
        // 关键词先经共享口径（与两个主页面、另三张长清单同一份匹配规则），状态筛选在其后：
        // 两个条件都是「且」，顺序不影响结果，但匹配规则只有一份。
        Assert.Matches($@"(?m)^\s*var visible = {chain};$", source);
        // 状态下拉的判定仍是原来那一份，不因加了关键词而另写一套。
        Assert.Matches(@"(?m)^\s*private bool Matches\((CommentCandidate|ResponseContract) \w+\) => _filter\.SelectedIndex switch$", source);
    }

    // ---------- 两个筛选入口一起启停 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void BothEntries_StartAndStopTogether(string file)
    {
        var source = Repo.Source("Desktop", "Native", file);
        // 整份数据不可得（读取失败/没有条目）时两个入口一起停：只停一个等于留一个按不动的入口。
        Assert.Equal(Occurrences(source, "_filter.Enabled = keepAggregate;"), Occurrences(source, "_keyword.Enabled = keepAggregate;"));
        Assert.Equal(Occurrences(source, "_filter.Enabled = true;"), Occurrences(source, "_keyword.Enabled = true;"));
        // 提示出口不写死禁用：出口按状态同启停两个入口。
        Assert.DoesNotMatch(@"(?m)^\s*_keyword\.Enabled = false;$", source);
        Assert.DoesNotMatch(@"(?m)^\s*_filter\.Enabled = false;$", source);
    }

    [Fact]
    public void TheImportPath_ReopensBothEntriesTogether()
    {
        // 导入编码助手结果后候选重新可读：只开筛选下拉、把关键词框留在禁用态，
        // 等于留一个按不动的入口（第六十六轮同一条纪律）。
        var source = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        var body = Slice(source, "private async void ImportResult()", "private void SetStatus");
        Assert.Matches(@"(?m)^\s*_filter\.Enabled = true;$", body);
        Assert.Matches(@"(?m)^\s*_keyword\.Enabled = true;$", body);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void FilteredEmpty_LeavesBothEntriesUsable(string file)
    {
        // 筛到零行那句话指名的下一步就是这两个入口：把它们按下去，等于把恢复路径一起收掉
        // （§7「禁用不得移除下一步」，第四十二轮那条纪律）。
        var body = Slice(Repo.Source("Desktop", "Native", file), "private void ShowHint(", "private void UpdateCounts()");
        Assert.DoesNotMatch(@"(?m)^\s*_filter\.Enabled = false;$", body);
        Assert.DoesNotMatch(@"(?m)^\s*_keyword\.Enabled = false;$", body);
    }

    // ---------- 筛到零行那句话：有关键词框时下一步有两个真实入口 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs", "CandidateNoun")]
    [InlineData("ContractsDialog.cs", "ContractNoun")]
    public void FilteredEmptyHint_NamesBothEntries_WhenThereIsAKeyword(string file, string nounConst)
    {
        var source = Repo.Source("Desktop", "Native", file);
        Assert.Matches($@"(?m)^\s*ShowHint\(ListFilter\.FilteredEmptyHint\(ListFilter\.{nounConst}, ListFilter\.IsActive\(_keyword\.Text\)\), UiTokens\.Muted, keepAggregate: true\);$", source);
        // 关键词为空时仍只指筛选器：文字不指向界面上按不动的控件。
        Assert.EndsWith(ListFilter.FilteredEmptySearchPointer, ListFilter.FilteredEmptyHint(ListFilter.CandidateNoun, true), StringComparison.Ordinal);
        Assert.EndsWith(ListFilter.FilteredEmptyPointer, ListFilter.FilteredEmptyHint(ListFilter.CandidateNoun, false), StringComparison.Ordinal);
    }

    // ---------- 关键词框不是表单字段：裸 Enter 不提交对话框 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void KeywordBox_EnterDoesNotSubmitTheDialog(string file)
    {
        var source = Repo.Source("Desktop", "Native", file);
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

    [Theory]
    [InlineData("CandidatesDialog.cs", "foreach (var candidate in _store.Candidates)")]
    [InlineData("ContractsDialog.cs", "foreach (var contract in _contracts)")]
    public void CountsRow_StillCountsEveryEntry_NotTheFilteredOnes(string file, string loop)
    {
        // 聚合计数不随筛选变化（第五十九轮）：「候选 128 · 待审阅 0」正是清完队列的凭据，
        // 被当前筛选抹掉等于把结果藏起来。
        var body = Slice(Repo.Source("Desktop", "Native", file), "private void UpdateCounts()", "private bool Matches");
        Assert.Contains(loop, body, StringComparison.Ordinal);
        Assert.DoesNotContain("_list.Items.Count", body, StringComparison.Ordinal);
        Assert.DoesNotContain("visible", body, StringComparison.Ordinal);
    }

    // ---------- 口径是真执行：跑的是主工程同一份 ListFilter 与 QueueText ----------

    [Fact]
    public void CandidateRows_FilterByTheSharedRules()
    {
        var rows = new[]
        {
            Candidate("c1", "s1", "OrderService.CreateOrder", "param", "orderId", "code", "rule"),
            Candidate("c2", "s2", "UserService.GetUser", "summary", null, "needsConfirmation", "model"),
        };
        var slots = new Dictionary<string, DocSlot>(StringComparer.Ordinal)
        {
            ["s1"] = new() { Id = "s1", SymbolId = "OrderService.CreateOrder", Display = "CreateOrder" },
            ["s2"] = new() { Id = "s2", SymbolId = "UserService.GetUser", Display = "GetUser" },
        };
        string?[] Fields(CommentCandidate c) => QueueText.CandidateFields(c, slots.GetValueOrDefault(c.SlotId));

        // 一个词命中即中（字段间「或」）。
        Assert.Single(ListFilter.Apply(rows, "orderId", Fields));
        Assert.Single(ListFilter.Apply(rows, "GetUser", Fields));
        // 词间「且」：两个词都要各自落在至少一个字段里，两个词落在同一行才算。
        Assert.Single(ListFilter.Apply(rows, "CreateOrder 参数", Fields));
        Assert.Single(ListFilter.Apply(rows, "GetUser 模型", Fields));
        // 「CreateOrder」在第一行、「模型」在第二行：没有任何一行同时满足两个词，故一行都不剩。
        Assert.Empty(ListFilter.Apply(rows, "CreateOrder 模型", Fields));
        // 空关键词即全部行：清空框回到整份清单。
        Assert.Equal(2, ListFilter.Apply(rows, "", Fields).Count);
        // 忽略大小写。
        Assert.Single(ListFilter.Apply(rows, "getuser", Fields));
    }

    [Fact]
    public void ContractRows_FilterByTheSharedRules()
    {
        var rows = new[]
        {
            new ResponseContract { EndpointKey = "POST /api/orders", WrapperType = "PageResult", DataType = "OrderDto" },
            new ResponseContract { EndpointKey = "GET /api/users", WrapperType = "", DataType = null },
        };
        // 一个词命中即中（字段间「或」）。
        Assert.Single(ListFilter.Apply(rows, "orders", QueueText.ContractFields));
        Assert.Single(ListFilter.Apply(rows, "PageResult", QueueText.ContractFields));
        // 词间「且」：两个词都要各自落在至少一个字段里。
        Assert.Single(ListFilter.Apply(rows, "users 直接返回", QueueText.ContractFields));
        Assert.Empty(ListFilter.Apply(rows, "orders 用户", QueueText.ContractFields));
        // 空关键词即全部行。
        Assert.Equal(2, ListFilter.Apply(rows, "", QueueText.ContractFields).Count);
    }

    // ---------- 纯呈现层：不写数据 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void ThisScreen_StaysReadOnly(string file)
    {
        // 关键词框只改这一屏看得见哪些行：不写任务与配置数据、不改审阅状态、服务层一行未触达。
        var body = Slice(Repo.Source("Desktop", "Native", file), "private void ApplyFilter()", "private void ShowDetail()");
        Assert.DoesNotContain("_tasks.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_drafts.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_reviews.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReviewAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", body, StringComparison.Ordinal);
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void SharedCompile_IncludesTheQueueTextCaliber()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 QueueText.cs 与它依赖的两处模型）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\QueueText.cs", csproj);
        Assert.Contains(@"..\Desktop\Native\SlotText.cs", csproj);
        Assert.Contains(@"..\Source\SourceModels.cs", csproj);
        Assert.Contains(@"..\Comments\DraftModels.cs", csproj);
    }

    [Fact]
    public void TheCaliber_DoesNotDependOnWinForms()
    {
        // 口径文件要能在 Linux 的测试宿主里真执行：引用了 WinForms 就编不进测试工程。
        var source = Repo.Source("Desktop", "Native", "QueueText.cs");
        Assert.DoesNotContain("System.Windows.Forms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Drawing;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheTwoReviewQueuesJoiningTheFamily()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("第六十七轮", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 长清单的关键词框（§6） \|", ledger);
        Assert.Contains("第六十七轮", ledger);
    }

    // ---------- 辅助 ----------

    private static CommentCandidate Candidate(string id, string slotId, string symbolId, string tag, string? parameter, string basis, string source) =>
        new()
        {
            Id = id,
            SlotId = slotId,
            SymbolId = symbolId,
            Tag = tag,
            ParameterName = parameter,
            Basis = basis,
            Source = source,
            ReviewState = "pending",
        };

    private static string Slice(string source, string start, string end)
    {
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
