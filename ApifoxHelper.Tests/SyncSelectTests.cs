using System.Text.RegularExpressions;
using ApifoxHelper.Contracts;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览接口勾选页的关键词框（第七十二轮，DESIGN.md §6「列表筛选」）：这一页是「生成计划前选哪些接口」
/// 的唯一入口，清单是整份检查结果的接口——真实项目几十上百个（方案 §4.2 记录千级接口分支），
/// 要找到「上次那个接口」只能逐行扫；冻结页面的差异视图一直有关键词框，另六张长清单在第六十五至六十七轮
/// 补过同一个框，同一个动作（在长清单里按关键词找一行）在别处有、在这一处没有，属同壳不同行为。
/// <para>
/// 这一页与那六张有一处不同：**勾选状态挂在行上**。筛选要重建清单，重建就把勾选丢了——用户勾了 12 个、
/// 筛一下再筛回来只剩 3 个，点「生成同步计划」写出去的就是 3 个（静默少同步比勾不上难发现得多）。
/// 故勾选状态搬到 <see cref="SyncSelect"/> 按接口键记（单一来源），筛选只改可见行、不改勾选；
/// 这条不变量由本文件在 Linux 上真执行钉住（<c>SyncSelect.cs</c> 与测试工程共享编译）。
/// </para>
/// <para>
/// 界面接线（关键词框、筛选行、勾选回写、筛到零行那句话）属 WinForms 控件触达，本环境不可执行，
/// 全部以源码切片与行首正则钉住，不冒充跑过；真机上的输入手感与排布随 G-D5 复核。
/// </para>
/// </summary>
public class SyncSelectTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：筛选只改可见行，不改勾选 ----------

    [Fact]
    public void Visible_OnlyChangesVisibleRows_NotTheChecks()
    {
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "structure", "创建订单"));
        var select = new SyncSelect();
        select.Set("GET /orders", true);
        select.Set("POST /orders", true);

        select.Query = "创建";

        // 可见行只剩一条，勾选仍是两条——筛选不是取消勾选。
        Assert.Equal(["POST /orders"], select.Visible(diff).Select(e => e.Key));
        Assert.Equal(2, select.CheckedCount);
        Assert.True(select.IsChecked("GET /orders"));
        Assert.True(select.IsChecked("POST /orders"));
    }

    [Fact]
    public void CheckedState_SurvivesAFilterRoundTrip()
    {
        // 清单重建（筛一下再筛回来）不丢勾选：勾选按接口键记，行只是它的呈现。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "new", "创建订单"),
            Endpoint("GET", "/users", "docs", "用户列表"));
        var select = new SyncSelect();
        select.Set("GET /orders", true);
        select.Set("GET /users", true);

        select.Query = "orders";
        Assert.Equal(2, select.Visible(diff).Count);
        select.Query = "";
        Assert.Equal(3, select.Visible(diff).Count);

        Assert.Equal(2, select.CheckedCount);
        Assert.Equal(["GET /orders", "GET /users"], select.CheckedKeys(diff));
    }

    [Fact]
    public void CheckedKeys_FollowDataSourceOrder_NotTheOrderTheyWereTicked()
    {
        // 交出去的是数据源顺序：先勾哪个不影响交出去的内容（计划里的动作顺序由服务层按模型依赖排）。
        var diff = Diff(
            Endpoint("GET", "/orders", "new"),
            Endpoint("POST", "/orders", "new"),
            Endpoint("GET", "/users", "new"));
        var select = new SyncSelect();
        select.Set("GET /users", true);
        select.Set("GET /orders", true);

        Assert.Equal(["GET /orders", "GET /users"], select.CheckedKeys(diff));
    }

    [Fact]
    public void CheckedKeys_IncludeRowsHiddenByTheKeyword()
    {
        // 被关键词筛掉的行同样在册：筛选只改可见行，不改勾选——否则「筛一下再生成」就静默少同步。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("GET", "/users", "new", "用户列表"));
        var select = new SyncSelect();
        select.Set("GET /orders", true);
        select.Set("GET /users", true);
        select.Query = "users";

        Assert.Single(select.Visible(diff));
        Assert.Equal(["GET /orders", "GET /users"], select.CheckedKeys(diff));
    }

    // ---------- 两个批量动作的作用域不同，各有理由 ----------

    [Fact]
    public void SelectAllSyncable_OnlyTouchesVisibleRows()
    {
        // 「全选可同步」只作用于可见行（与资源管理器 Ctrl+A 同序）：屏幕上只有 1 行时点「全选」，
        // 看不见的行一个都不动——否则用户看不见自己勾了什么。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "new", "创建订单"),
            Endpoint("GET", "/users", "unchanged", "用户列表"));
        var select = new SyncSelect();
        select.Set("POST /orders", true);
        select.Query = "订单列表";

        select.SelectAllSyncable(diff, new DecisionStore(), offlineMode: false);

        Assert.True(select.IsChecked("GET /orders"));
        // 看不见的那一行保持原样（勾着的仍勾着），不可同步的那一行没被勾上。
        Assert.True(select.IsChecked("POST /orders"));
        Assert.False(select.IsChecked("GET /users"));
    }

    [Fact]
    public void SelectAllSyncable_WithoutKeyword_MatchesTheOldBehaviour()
    {
        // 没有关键词时清单里就是全部行，故行为与改前逐字一致：可同步的勾上、其余取消。
        var diff = Diff(
            Endpoint("GET", "/orders", "new"),
            Endpoint("POST", "/orders", "structure"),
            Endpoint("GET", "/users", "unchanged"),
            Endpoint("DELETE", "/users", "blocked"));
        var select = new SyncSelect();
        select.Set("GET /users", true);

        select.SelectAllSyncable(diff, new DecisionStore(), offlineMode: false);

        Assert.True(select.IsChecked("GET /orders"));
        Assert.True(select.IsChecked("POST /orders"));
        Assert.False(select.IsChecked("GET /users"));
        Assert.False(select.IsChecked("DELETE /users"));
        Assert.Equal(2, select.CheckedCount);
    }

    [Fact]
    public void SelectAllSyncable_SkipsEndpointsWithPendingDecisions()
    {
        // 待确认（潜在不兼容且还没逐项确认）的接口不进「全选可同步」——口径仍取 SyncSelection，不另写一份。
        var diff = Diff(Endpoint("GET", "/orders", "new"), Endpoint("POST", "/orders", "structure"));
        diff.Items.Add(new DiffItem { Id = "d1", ResourceKey = "POST /orders", NeedsDecision = true });
        var decisions = new DecisionStore();
        var select = new SyncSelect();

        select.SelectAllSyncable(diff, decisions, offlineMode: false);
        Assert.False(select.IsChecked("POST /orders"));
        Assert.True(select.IsChecked("GET /orders"));

        decisions.Items["d1"] = "accepted";
        select.SelectAllSyncable(diff, decisions, offlineMode: false);
        Assert.True(select.IsChecked("POST /orders"));
    }

    [Fact]
    public void Clear_ClearsEverything_NotJustTheVisibleRows()
    {
        // 「清空」按全部清：按钮说的是清空，不是清空可见——留着看不见的勾选，点完清空计数还显示已选择。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("GET", "/users", "new", "用户列表"));
        var select = new SyncSelect();
        select.Set("GET /orders", true);
        select.Set("GET /users", true);
        select.Query = "orders";

        select.Clear();

        Assert.Equal(0, select.CheckedCount);
        Assert.Empty(select.CheckedKeys(diff));
    }

    [Fact]
    public void VisibleSyncableCount_MatchesWhatSelectAllWouldCheck()
    {
        // 按钮上的数字与按钮实际勾的行必须是同一批：数字说 2 而只勾上 1 个，就是按钮在说谎。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "structure", "创建订单"),
            Endpoint("GET", "/users", "unchanged", "用户列表"));
        var select = new SyncSelect();
        select.Query = "orders";

        var count = select.VisibleSyncableCount(diff, new DecisionStore(), offlineMode: false);
        select.SelectAllSyncable(diff, new DecisionStore(), offlineMode: false);

        Assert.Equal(2, count);
        Assert.Equal(count, select.CheckedCount);
    }

    // ---------- 匹配的是屏幕上那几格，不另造一套字段 ----------

    [Fact]
    public void SearchFields_MatchTheCellsTheRowShows()
    {
        var check = Endpoint("GET", "/orders", "new", "订单列表");
        var fields = SyncSelect.SearchFields(check);

        // 接口（方法 + 路径）、名称、状态文字——这一行在屏幕上被读作的那三格。
        Assert.Equal(["GET /orders", "订单列表", "新增"], fields);
        Assert.True(ListFilter.Matches("orders", fields));
        Assert.True(ListFilter.Matches("订单", fields));
        // 状态那一列的文字进关键词：这一页没有筛选下拉，不进关键词就等于筛不到。
        Assert.True(ListFilter.Matches("新增", fields));
        // 「待确认」那一列是数字，不进关键词（与五张长清单「状态列由筛选下拉管、不进关键词」同一口径）。
        Assert.Equal(3, fields.Length);
    }

    [Fact]
    public void SearchFields_ReadTheSameStatusTextAsTheCell()
    {
        // 状态文字与单元格同源（TaskDisplay.CheckStatusText）：两处各拼一次，必然有一处先漂。
        foreach (var status in new[] { "new", "structure", "docs", "unchanged", "blocked", "sourceOnly" })
        {
            Assert.Contains(TaskDisplay.CheckStatusText(status), SyncSelect.SearchFields(Endpoint("GET", "/x", status)));
        }
        var source = Repo.Source("Desktop", "Native", "SyncSelect.cs");
        Assert.Contains("TaskDisplay.CheckStatusText(check.Status)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_IsTrimmedAndBlankMeansAllRows()
    {
        var diff = Diff(Endpoint("GET", "/orders", "new"), Endpoint("GET", "/users", "new"));
        var select = new SyncSelect();

        select.Query = "   ";
        Assert.Equal(2, select.Visible(diff).Count);
        select.Query = "  users  ";
        Assert.Equal(["GET /users"], select.Visible(diff).Select(e => e.Key));
    }

    // ---------- 口径不复制勾选规则，也不引用 WinForms ----------

    [Fact]
    public void SelectionRules_AreNotCopiedHere()
    {
        // 可同步状态与待确认口径只有一份（SyncSelection）：本文件只决定「哪些行看得见」与「勾选记在哪」。
        var source = Repo.Source("Desktop", "Native", "SyncSelect.cs");
        Assert.DoesNotContain("SyncableStatuses", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"unchanged\"", source, StringComparison.Ordinal);
        Assert.Contains("SyncSelection.SyncableKeys(", source, StringComparison.Ordinal);
        Assert.Contains("SyncSelection.SyncableNow(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Gauge_DoesNotReachForWinForms()
    {
        // 引用只算代码行：注释里写框架文档名不算引用，而 using 一行真写上就编不进测试工程（共享编译）。
        Assert.Empty(CodeLines(Repo.Source("Desktop", "Native", "SyncSelect.cs"), "System.Windows.Forms"));
        Assert.Empty(CodeLines(Repo.Source("Desktop", "Native", "SyncSelect.cs"), "System.Drawing"));
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void Placeholder_ComesFromTheSharedConstant_AndHasOneHome()
    {
        Assert.Equal("筛选接口（方法、路径、名称、状态）", ListFilter.SyncSelectSearchPlaceholder);
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.SyncSelectSearchPlaceholder;$", source);
        // 占位文字不在界面就地拼写：整份源码里只有 ListFilter 那一处字面。
        foreach (var other in Repo.NativeSources())
        {
            Assert.Equal(other.Name == "ListFilter.cs" ? 1 : 0, CodeLines(other.Text, ListFilter.SyncSelectSearchPlaceholder).Count);
        }
        // 与检查结果那一屏的框说的不是同一件事：这一页的状态列没有下拉可管，故状态文字进关键词。
        Assert.NotEqual(ListFilter.SyncSelectSearchPlaceholder, ListFilter.EndpointSearchPlaceholder);
    }

    [Fact]
    public void KeywordBox_IsBuiltByTheSharedFactory_AndFiltersAsYouType()
    {
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => ApplyFilter\(\);$", source);
        // 宽度取工厂常量（第六十八轮）：界面不各写一个数。
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterRow_ComesFromTheFactory_WithTheKeywordOnlyBranch()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 这一页只有关键词框、没有筛选下拉：走 counts 可缺省的那一支，行形状仍是同一份五列。
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, _visibleCount\);$", source);
        Assert.Single(Regex.Matches(source, @"Ui\.FilterRow\(").Cast<Match>());
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.Field(_keyword)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_AreWrittenThroughOneEntry()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 勾选状态的唯一写入口：界面只在 ItemChecked 里调 SyncSelect.Set，不自己维护第二份。
        Assert.Matches(@"(?m)^\s*if \(e\.Item\.Tag is EndpointCheck check\) _select\.Set\(check\.Key, e\.Item\.Checked\);$", source);
        // 重建清单期间不回写（回写幂等，但每行刷一次计数是 O(n²)）。
        Assert.Matches(@"(?m)^\s*if \(_rebuilding\) return;$", source);
        // 界面不再从行上读勾选（读的是 SyncSelect 那一份）。
        Assert.DoesNotContain("i.Checked", source, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Checked = checkAll", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Rebuild_RestoresChecksFromTheSingleSource()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 清单重建时每行的勾选从 SyncSelect 恢复——重建不丢勾选，这是本页筛选的前提。
        Assert.Matches(@"(?m)^\s*row\.Checked = _select\.IsChecked\(check\.Key\);$", source);
        // 可见行取共享口径（与五张长清单同一份匹配规则），不在界面里另拼一套。
        Assert.Matches(@"(?m)^\s*foreach \(var check in _select\.Visible\(diff\)\)$", source);
    }

    [Fact]
    public void Generate_HandsOverTheCheckedKeys_NotTheVisibleRows()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = Slice(source, "private void Generate()", "private void SetBusy(bool busy)");
        Assert.Contains("var keys = _select.CheckedKeys(_diff);", body, StringComparison.Ordinal);
        // 交出去的不是「清单上勾着的行」：那样筛一下再生成就静默少同步。
        Assert.DoesNotContain("_selectList.Items", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BothEntries_StartAndStopTogether()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 整份数据不可得时两个筛选入口一起停：只停一个等于留一个按不动的入口。
        // 第七十八轮起「框能不能用」取 ListFilter.KeywordEnabled 单一口径（此前这一页就地写
        // `!busy && _diff is not null`，另三张只有关键词框的清单则根本没有这一句——同一个状态两种样子）。
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = ListFilter\.KeywordEnabled\(false\);$").Cast<Match>());
        // 生成或执行中一并停，结束后按「数据可读」恢复。
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = ListFilter\.KeywordEnabled\(_diff is not null, busy\);$").Cast<Match>());
        // 界面不就地拼这条判定（拼一遍就有一处先漂）。
        Assert.DoesNotContain("_keyword.Enabled = !busy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FilteredEmpty_NamesTheKeywordBox_AndLeavesItUsable()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = Slice(source, "private void ApplyFilter()", "private void PopulateSelectList()");
        // 这一页没有筛选下拉，故下一步只指那个框（文字不指向界面上按不动的控件）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("FilteredEmptyHint(", body, StringComparison.Ordinal);
        // 筛到零行时两个入口都留着：提示语指名的下一步不能按不动（§7「禁用不得移除下一步」）。
        Assert.DoesNotContain("_keyword.Enabled", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectAllButton.Enabled", body, StringComparison.Ordinal);
        // 判定取共享口径，不在界面里就地写「总数 > 0 且可见 0」。
        Assert.Contains("ListFilter.IsFilteredEmpty(_diff.Endpoints.Count, _selectList.Items.Count)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsCell_CountsTheWholeCheckResult_NotTheVisibleRows()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 聚合数字不随关键词变化（第五十九轮）：接口总数与可同步数是整份检查结果的凭据。
        Assert.Matches(@"(?m)^\s*_visibleCount\.Text = ""接口 "" \+ diff\.Endpoints\.Count \+ "" · 可同步 ""$", source);
        var body = Slice(source, "private void PopulateSelectList()", "private void ApplyChecks()");
        Assert.DoesNotContain("_selectList.Items.Count", body, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardGestures_AreWiredThroughTheSharedEntries()
    {
        // 第七十二轮如实挂账「Ctrl+F / Enter / Esc 在这一页还没接」，第七十三轮接上（另六张长清单在第六十九至
        // 七十一轮各接一次）：三个手势只经 Ui 的三处共享入口，本对话框不就地认键、不就地拆修饰键。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Contains("protected override bool ProcessDialogKey(Keys keyData)", source, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryFocusFilter\(keyData, _keyword\)\) return true;$", source);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, _selectList\)\) return true;$", source);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _keyword\)\) return true;$", source);
        // 不就地认键：代码行里没有 Keys.*（注释里写手势名是说明理由，不算第二套判定）。
        Assert.Empty(CodeLines(source, "Keys."));
    }

    [Fact]
    public void SharedCompileList_CarriesTheSelectionState()
    {
        // 口径文件必须与测试工程共享编译，否则上面那些真值表跑的不是主工程那一份代码。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\SyncSelect.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheKeywordBoxOnTheSelectPage()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("同步预览的接口勾选页", section, StringComparison.Ordinal);
        Assert.Contains("SyncSelect", section, StringComparison.Ordinal);
        // 这一页与另六张的差别要写清：勾选挂在行上，筛选重建清单会把勾选丢掉。
        Assert.Contains("勾选状态", section, StringComparison.Ordinal);
        // 只有关键词框、没有筛选下拉，故筛到零行那句话只指那个框。
        Assert.Contains("FilteredEmptyKeywordHint", section, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var section = Repo.DesignSection(8);
        var row = section.Split('\n').FirstOrDefault(l => l.StartsWith("| 同步预览勾选页的关键词框（§6） |", StringComparison.Ordinal));
        Assert.NotNull(row);
        Assert.Contains("第七十二轮", row!, StringComparison.Ordinal);
        // 台账要如实：这一轮只加框与勾选状态搬家，不改勾选口径、不加视觉语言；
        // 三个手势当时如实挂账，第七十三轮接上后那一行指向本轮（旧措辞「本轮未接」不得留着冒充现状）。
        Assert.Contains("不改勾选口径", row!, StringComparison.Ordinal);
        Assert.Contains("Ctrl+F", row!, StringComparison.Ordinal);
        Assert.Contains("G-D5", row!, StringComparison.Ordinal);
        // 台账是历史：当时「未接」的理由留着，但必须指向第七十三轮的收口（旧措辞不得冒充现状）。
        Assert.Contains("第七十三轮", row!, StringComparison.Ordinal);
        Assert.Contains("已于第七十三轮接上", row!, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimWhatThisRoundDidNotDo()
    {
        var section = Repo.DesignSection(6);
        // 本轮没有新增视觉语言、没有改可同步口径、没有把键盘手势接上。
        Assert.DoesNotContain("勾选页新增视觉", section, StringComparison.Ordinal);
        Assert.DoesNotContain("勾选页已接 Ctrl+F", section, StringComparison.Ordinal);
        // 可同步口径与两个批量动作的作用域逐字不动——条款里要写明这一点。
        Assert.Contains("可同步口径", section, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static CheckResult Diff(params EndpointCheck[] endpoints) => new() { Endpoints = [.. endpoints] };

    private static EndpointCheck Endpoint(string method, string path, string status, string? name = null) => new()
    {
        Key = method + " " + path,
        Method = method,
        Path = path,
        Status = status,
        RemoteName = name,
        InRemote = true,
    };

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }

    /// <summary>出现该字面量的代码行（注释行跳过：注释里引用这句话是说明理由，不是第二套拼写）。</summary>
    private static List<string> CodeLines(string text, string fragment) =>
        text.Split('\n').Where(l =>
        {
            var trimmed = l.TrimStart();
            return trimmed.Contains(fragment, StringComparison.Ordinal)
                && !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal);
        }).ToList();
}
