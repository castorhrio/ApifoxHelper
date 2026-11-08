using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 长清单的关键词框（第六十五轮，DESIGN.md §6「列表筛选」）：检查结果与差异确认两张清单在真实项目上
/// 动辄几十上百行（方案 §4.2 记录千级接口分支），此前只有状态下拉，找「上次那个接口」只能逐行扫；
/// 冻结页面的差异视图一直有关键词框（「关键词（路径、接口名）」）。同一个动作（在长清单里按关键词找一行）
/// 在主页有、在对话框没有，属同壳不同行为，故按同一份口径收口：占位文字、匹配规则与「筛到零行那句话」
/// 都从 <see cref="ListFilter"/> 取，两个对话框不各写一套。
/// 关键词框不是表单字段：输入即筛选，框里的裸 Enter 不提交对话框（冻结页面的关键词框在 Enter 上同样什么都不做）。
/// 真机上的输入手感与按键路由随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class KeywordFilterTests
{
    // ---------- 文案单一落点 ----------

    [Fact]
    public void Placeholders_AreDistinctAndSayWhatTheyMatch()
    {
        Assert.Equal("筛选接口（方法、路径、名称）", ListFilter.EndpointSearchPlaceholder);
        Assert.Equal("筛选条目（资源、位置、类型）", ListFilter.DecisionSearchPlaceholder);
        Assert.Equal("筛选位置（符号、标注、角色、文件）", ListFilter.SlotSearchPlaceholder);
        // 三张长清单找的不是同一样东西：并成一份等于把「找接口」说成「找条目」。
        Assert.NotEqual(ListFilter.EndpointSearchPlaceholder, ListFilter.DecisionSearchPlaceholder);
        Assert.NotEqual(ListFilter.EndpointSearchPlaceholder, ListFilter.SlotSearchPlaceholder);
        Assert.NotEqual(ListFilter.DecisionSearchPlaceholder, ListFilter.SlotSearchPlaceholder);
        Assert.NotEqual(ListFilter.TaskPlaceholder, ListFilter.EndpointSearchPlaceholder);
        Assert.NotEqual(ListFilter.ProfilePlaceholder, ListFilter.DecisionSearchPlaceholder);
    }

    [Theory]
    [InlineData("CheckResultsDialog.cs", "EndpointSearchPlaceholder")]
    [InlineData("DecisionsDialog.cs", "DecisionSearchPlaceholder")]
    public void Placeholder_IsTakenFromTheSharedConstant(string file, string constant)
    {
        var source = Repo.Source("Desktop", "Native", file);
        Assert.Matches($@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.{constant};$", source);
        // 占位文字不在界面就地拼写：整份源码里只有 ListFilter 那一处字面。
        var literal = constant == "EndpointSearchPlaceholder" ? ListFilter.EndpointSearchPlaceholder : ListFilter.DecisionSearchPlaceholder;
        foreach (var other in Repo.NativeSources())
        {
            Assert.Equal(other.Name == "ListFilter.cs" ? 1 : 0, CodeLinesContaining(other.Text, literal));
        }
    }

    // ---------- 筛到零行那句话：有关键词框时下一步有两个真实入口 ----------

    [Fact]
    public void Hint_WithKeywordBox_PointsAtBothEntries()
    {
        Assert.Equal("换一个关键词或筛选再试。", ListFilter.FilteredEmptySearchPointer);
        Assert.NotEqual(ListFilter.FilteredEmptyPointer, ListFilter.FilteredEmptySearchPointer);
        foreach (var noun in new[] { ListFilter.EndpointNoun, ListFilter.DecisionNoun, ListFilter.SlotNoun })
        {
            Assert.EndsWith(ListFilter.FilteredEmptySearchPointer, ListFilter.FilteredEmptyHint(noun, true), StringComparison.Ordinal);
            // 不传「框里有字」时只指筛选器：文字不指向界面上按不动的控件（第四十二、四十六轮同一条纪律）。
            Assert.EndsWith(ListFilter.FilteredEmptyPointer, ListFilter.FilteredEmptyHint(noun), StringComparison.Ordinal);
            Assert.EndsWith(ListFilter.FilteredEmptyPointer, ListFilter.FilteredEmptyHint(noun, false), StringComparison.Ordinal);
        }
    }

    [Theory]
    // 第六十七轮起注释候选与响应契约也并入同一册：这两个审阅队列是逐条清队列的两张清单，
    // 队列里上百条时找「上次那条」只能逐行扫。此前它们只有状态下拉，话术只指筛选器；
    // 现在有了关键词框，下一步有两个真实入口，话术按「框里有没有字」内联判定。
    [InlineData("CandidatesDialog.cs", "CandidateNoun")]
    [InlineData("ContractsDialog.cs", "ContractNoun")]
    public void TheTwoReviewQueues_JoinedTheKeywordBoxFamily(string file, string nounConst)
    {
        var source = Repo.Source("Desktop", "Native", file);
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        // 筛到零行那句话的下一步因此有两个真实入口（关键词或筛选）：框里有字才指两个，框里没字仍只指筛选器。
        Assert.Matches($@"(?m)^\s*(?:ShowHint\(|_hint\.Text = )ListFilter\.FilteredEmptyHint\(ListFilter\.{nounConst}, ListFilter\.IsActive\(_keyword\.Text\)\)", source);
    }

    [Fact]
    public void TheSlotList_JoinedTheKeywordBoxFamily()
    {
        // 第六十六轮：注释缺口清单并入同一册——它是全应用最长的一张清单（一次扫描的全部注释位置），
        // 只有状态下拉时找「上次那个位置」只能逐行扫。
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.SlotSearchPlaceholder;$", source);
        // 筛到零行那句话的下一步因此有两个真实入口（关键词或筛选）。
        Assert.Matches(@"(?m)^\s*_hint\.Text = ListFilter\.FilteredEmptyHint\(ListFilter\.SlotNoun, ListFilter\.IsActive\(_keyword\.Text\)\);$", source);
    }

    // ---------- 检查结果：关键词按列表首两列的文字找 ----------

    [Fact]
    public void CheckResults_KeywordBox_FiltersByTheVisibleColumns()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => PopulateList\(\);$", source);
        // 匹配的是这一行在屏幕上被读作的那两格（接口身份、远端名称），不另造一套字段。
        // 第九十七轮起筛选结果先经 ListSort.Order 再上屏（筛选先于排序，与两个主页面同一顺序），
        // 匹配字段与筛选口径一字未变，故断言跟着真实形状走、强度不降。
        Assert.Matches(@"(?m)^\s*var endpoints = ListSort\.Order\($", source);
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(_result\.Endpoints, keyword, c => \[c\.Method \+ "" "" \+ c\.Path, c\.RemoteName\]\)$", source);
        // 关键词与状态下拉都是「且」：状态筛选在共享口径之后，匹配规则只有一份。
        Assert.Matches(@"(?m)^\s*\.Where\(c => status\.Length == 0 \|\| c\.Status == status\)$", source);
        // 两个筛选入口一起启停：只停一个等于留一个按不动的入口。
        Assert.Equal(2, Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = false;$").Count);
        Assert.Matches(@"(?m)^\s*_keyword\.Enabled = _result\.Endpoints\.Count > 0;$", source);
    }

    // ---------- 差异确认：关键词与分类筛选都是「且」 ----------

    [Fact]
    public void Decisions_KeywordBox_FiltersByTheVisibleColumns()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => ApplyFilter\(Current\(\)\);$", source);
        // 匹配的是这一行在屏幕上被读作的那三格——资源、位置、类型。
        Assert.Matches(@"(?m)^\s*MatchesKind\(entry\) && ListFilter\.Matches\(_keyword\.Text, entry\.Resource, entry\.Location, KindText\(entry\.Kind\)\);$", source);
        // 打开期四个无数据门控与运行期筛到零行都一起启停两个筛选入口。
        Assert.Equal(4, Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = false;$").Count);
        Assert.Equal(2, Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = true;$").Count);
    }

    // ---------- 关键词框不是表单字段：裸 Enter 不提交对话框 ----------

    [Theory]
    [InlineData("CheckResultsDialog.cs")]
    [InlineData("DecisionsDialog.cs")]
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

    // ---------- 口径是真执行：两张清单的字段组合跑的是主工程同一份代码 ----------

    [Fact]
    public void EndpointFields_MatchMethodPathAndRemoteName()
    {
        var rows = new[] { ("GET /orders", "订单列表"), ("POST /users", "创建用户") };
        var hit = ListFilter.Apply(rows, "orders", r => [r.Item1, r.Item2]);
        Assert.Single(hit);
        Assert.Equal("GET /orders", hit[0].Item1);
        // 词间「且」：两个词分别落在不同字段里也算命中。
        Assert.Single(ListFilter.Apply(rows, "get 订单", r => [r.Item1, r.Item2]));
        Assert.Empty(ListFilter.Apply(rows, "get 用户", r => [r.Item1, r.Item2]));
    }

    [Fact]
    public void DecisionFields_MatchResourceLocationAndKind()
    {
        Assert.True(ListFilter.Matches("高风险 订单", "订单服务", "data.items", "高风险变化"));
        Assert.True(ListFilter.Matches("模型绑定", "订单模型", "待绑定", "模型绑定"));
        Assert.False(ListFilter.Matches("说明冲突", "订单服务", "data.items", "高风险变化"));
        // 空关键词即全部行：清空框回到整份清单。
        Assert.True(ListFilter.Matches("", "订单服务", "data.items", "高风险变化"));
    }

    // ---------- 辅助 ----------

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
