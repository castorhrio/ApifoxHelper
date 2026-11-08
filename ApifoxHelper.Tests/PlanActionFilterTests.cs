using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 第八十轮：同步预览写入清单的关键词框（DESIGN.md §6「列表筛选」）。
/// 这一屏的清单是整份冻结计划的逐动作写入清单（真实项目几十上百个接口与模型），确认同步之前要逐条看过，
/// 要找到「失败的那几条」「上次那个动作」只能逐行扫；另十张长清单在第六十五至七十九轮都有关键词框，
/// 同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 这一屏与那十张有一处不同：**同一个对话框里两页各有一个关键词框**（勾选页筛接口、预览页筛写入清单），
/// 故三个手势的门控按当前可见页走——一次按键只有一个含义，预览页的 Esc 不会去清勾选页的关键词。
/// 另一处不同是序号列：它是执行顺序里的第几步，筛选只改可见行、不改序号（筛掉几条之后「第 7 步」还是第 7 步，
/// 否则按序号核对执行日志就对不上）。
/// 口径是真执行（<see cref="PlanEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 界面接线属 WinForms 控件触达、本环境不可执行，全部以源码切片与行首正则钉住，不冒充跑过。
/// 真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class PlanActionFilterTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：改动列文字与关键词字段 ----------

    [Fact]
    public void ChangesText_IsTheSameTextTheCellShows()
    {
        // 清单第五格与关键词匹配取同一份：两处各写一套必然漂移（敲「写入字段」筛不到那一行）。
        Assert.Equal("新增字段：remark；说明变化", PlanEntries.ChangesText(["新增字段：remark", "说明变化"], []));
        Assert.Equal("写入字段：name, path", PlanEntries.ChangesText([], ["name", "path"]));
        Assert.Equal("—", PlanEntries.ChangesText([], []));
        // 有改动时不再拼写入字段：那一列说的是「这次改了什么」，不是「这次允许改什么」。
        Assert.Equal("说明变化", PlanEntries.ChangesText(["说明变化"], ["name"]));
    }

    [Fact]
    public void KeywordFields_AreTheFourCellsOfThatRow()
    {
        // 那一行在屏幕上被读作的四格：动作、资源、改动、状态。
        var fields = PlanEntries.KeywordFields("更新接口", "GET /orders", "新增字段：remark", "写入失败");
        Assert.Equal(4, fields.Length);
        Assert.Equal(new string?[] { "更新接口", "GET /orders", "新增字段：remark", "写入失败" }, fields);
        // 动作序号与远端 ID 是数字、不进关键词（与另几张清单的计数列同一取舍）。
        Assert.DoesNotContain(fields, f => f is "7" or "12345");
    }

    // ---------- 真执行：匹配规则走 ListFilter 同一份 ----------

    [Theory]
    [InlineData("orders", true)]        // 资源（忽略大小写）
    [InlineData("更新接口", true)]       // 动作
    [InlineData("remark", true)]        // 改动
    [InlineData("写入失败", true)]       // 状态
    [InlineData("12345", false)]        // 远端 ID 不进关键词
    [InlineData("通过", false)]         // CLI 校验那一列不进关键词
    [InlineData("", true)]              // 空关键词即全部行
    public void Matches_TheFourCellsOnly(string query, bool expected)
    {
        var fields = PlanEntries.KeywordFields("更新接口", "GET /orders", "新增字段：remark", "写入失败");
        Assert.Equal(expected, ListFilter.Matches(query, fields));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndTheExecutionIndex()
    {
        // 行是「动作 + 它在计划里的位次」：筛选只改可见行，位次跟着动作走（序号列因此仍是执行顺序里的第几步）。
        var rows = new[] { ("a001", 0), ("a002", 1), ("a003", 2) };
        var visible = ListFilter.Apply(rows, "a002", row => PlanEntries.KeywordFields("更新接口", row.Item1, "—", "待执行"));
        Assert.Equal([("a002", 1)], visible);
        // 数据源自身不被改写（与 ListSort.Order 同一纪律）。
        Assert.Equal([("a001", 0), ("a002", 1), ("a003", 2)], rows);
        // 空关键词返回数据源顺序的副本。
        Assert.Equal(rows, ListFilter.Apply(rows, "  ", row => PlanEntries.KeywordFields("更新接口", row.Item1, "—", "待执行")));
    }

    [Fact]
    public void IsFilteredEmpty_SeparatesTheTwoEmpties()
    {
        // 有计划动作、只是当前关键词不命中：这一屏的空在筛选上。
        Assert.True(ListFilter.IsFilteredEmpty(3, 0));
        // 整份计划没有动作：下一步不在筛选上（那一屏由表头与排除清单说话）。
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
        // 筛掉一部分不是空。
        Assert.False(ListFilter.IsFilteredEmpty(3, 1));
    }

    [Fact]
    public void FilteredEmptySentence_AndPlaceholder_AreTheSharedOnes()
    {
        // 这一屏没有筛选下拉，故下一步只指那个框（文字不指向界面上按不动的控件）。
        Assert.Equal("这个关键词下没有写入动作。换一个关键词再试。",
            ListFilter.FilteredEmptyKeywordHint(ListFilter.ActionNoun));
        Assert.Equal("筛选写入动作（资源、动作、改动、状态）", ListFilter.PlanActionSearchPlaceholder);
        // 名词与另七张清单同一份词汇表，不另起一份。
        // 取「写入动作」而不是「动作」：后者是「选中动作随之可用」这类既有文案里的普通词，
        // 名词要能在一句话里指认这一屏的东西，不能与别处的常用词撞成同一个字面（MissingSelectionTests 钉住）。
        Assert.Equal("写入动作", ListFilter.ActionNoun);
    }

    [Fact]
    public void CountsLine_IsTheWholePlanNumbers()
    {
        // 计数行按整份计划统计、不随关键词变化（「排除 N」正是要找的那批的凭据）。
        Assert.Equal("接口 3 · 模型 2 · 排除 1", PlanEntries.CountsLine(3, 2, 1));
        Assert.Equal("接口 0 · 模型 0 · 排除 0", PlanEntries.CountsLine(0, 0, 0));
    }

    // ---------- 接线：关键词框与筛选行经工厂 ----------

    [Fact]
    public void TheRow_ComesFromTheFactory_AndTheBoxIsNotMeasuredHere()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 这一屏没有筛选下拉：走 counts 可缺省的那一支（计数在表头行）。
        Assert.Matches(@"(?m)^\s*var actionFilterRow = Ui\.FilterRow\(_actionKeyword, counts: null\);$", source);
        // 三行三框：勾选页那一行仍在（第七十二轮），预览页写入清单这一行是本轮（第八十轮），
        // 预览页未纳入清单那一行是第八十六轮——三行都走同一支重载，行形状仍是同一份五列。
        Assert.Equal(3, Regex.Matches(source, @"Ui\.FilterRow\(").Count);
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, _visibleCount\);$", source);
        Assert.Matches(@"(?m)^\s*var excludedFilterRow = Ui\.FilterRow\(_excludedKeyword, counts: null\);$", source);
        // 界面不自己拼这一行，也不自己量关键词框的宽度。
        Assert.DoesNotContain("actionFilterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_actionKeyword.Width", source, StringComparison.Ordinal);
        // 占位文字取共享常量，不就地拼写。
        Assert.Matches(@"(?m)^\s*_actionKeyword\.PlaceholderText = ListFilter\.PlanActionSearchPlaceholder;$", source);
        Assert.DoesNotContain("筛选动作（", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRow_IsDockedBelowTheHeadRow()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 停靠按集合倒序生效：筛选行要在标题区之前加入，才停靠在标题区之下、写入清单之上。
        var filter = source.IndexOf("page.Controls.Add(actionFilterRow);", StringComparison.Ordinal);
        var head = source.IndexOf("page.Controls.Add(head);", StringComparison.Ordinal);
        Assert.True(filter >= 0 && head > filter, "筛选行要排在标题区之前加入（倒序停靠后落在标题区之下）。");
    }

    // ---------- 接线：筛到零行那一支 ----------

    [Fact]
    public void FilteredEmptyBranch_KeepsTheBoxAndTheWholePlanCounts()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", File), "private void PopulateActions");
        // 判定经共享口径，入参是整份计划的动作数与本次可见行数。
        Assert.Matches(@"(?m)^\s*if \(ListFilter\.IsFilteredEmpty\(plan\.Actions\.Count, visible\.Count\)\)$", body);
        // 那句话取共享组合（这一屏没有筛选下拉，只指那个框）。
        Assert.Matches(@"(?m)^\s*_actionHint\.Text = ListFilter\.FilteredEmptyKeywordHint\(ListFilter\.ActionNoun\);$", body);
        // 关键词框留着（§7「禁用不得移除下一步」）：这一支不出现 _actionKeyword.Enabled。
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.DoesNotContain("_actionKeyword.Enabled", body[branch..], StringComparison.Ordinal);
        // 计数行不随关键词变化：这个方法不碰 _planCounts。
        Assert.DoesNotContain("_planCounts.Text", body, StringComparison.Ordinal);
        // 写入内容预览经既有的无选中路径收口，不另写一套收控件的赋值。
        Assert.Matches(@"(?m)^\s*ShowPayload\(\);$", body);
        Assert.DoesNotContain("_payload.Text =", body, StringComparison.Ordinal);
        // 分支先返回、回显在后：写在提示之前等于这句话刚显示就被自己抹掉。
        var hint = body.IndexOf("FilteredEmptyKeywordHint", StringComparison.Ordinal);
        var restore = body.IndexOf("_actionHint.Visible = false;", StringComparison.Ordinal);
        Assert.True(hint >= 0 && restore > hint, "筛到零行的分支要排在清单回显之前。");
        Assert.Matches(@"(?m)^\s*_actionList\.Visible = true;$", body);
    }

    [Fact]
    public void CountsLine_IsComputedOnceFromTheWholePlan()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 计数只在 ShowPlan 按整份计划算一次（PopulateActions 不重算，否则关键词一变数字就变）。
        Assert.Single(Regex.Matches(source, @"PlanEntries\.CountsLine\(").Cast<Match>());
        Assert.Matches(@"(?m)^\s*_planCounts\.Text = PlanEntries\.CountsLine\(endpoints, plan\.Actions\.Count - endpoints, plan\.Excluded\.Count\);$", source);
    }

    // ---------- 接线：序号仍是执行顺序里的第几步 ----------

    [Fact]
    public void TheSerialNumber_StaysTheExecutionOrder()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", File), "private void PopulateActions");
        // 行是「动作 + 它在计划里的位次」，序号取位次 + 1，不取可见行的第几行。
        Assert.Matches(@"(?m)^\s*plan\.Actions\.Select\(\(action, index\) => \(action, index\)\),$", body);
        Assert.Matches(@"(?m)^\s*var row = new ListViewItem\(\(index \+ 1\)\.ToString\(\)\) \{ Tag = action, UseItemStyleForSubItems = false \};$", body);
        Assert.DoesNotContain("Items.Count + 1", body, StringComparison.Ordinal);
    }

    // ---------- 接线：选中按动作标识恢复，经唯一入口 ----------

    [Fact]
    public void Selection_IsRestoredByActionId_ThroughTheSingleEntry()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", File), "private void PopulateActions");
        // 先记标识再清空清单（清空会抹掉选中，之后取到的就是 null）。
        var remember = body.IndexOf("var selectedId = (Ui.SelectedItem(_actionList)?.Tag as PlanAction)?.ActionId;", StringComparison.Ordinal);
        var clear = body.IndexOf("_actionList.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "选中标识要在清空清单之前取局部。");
        // 按标识恢复而不是按对象引用：执行或核对后计划被重读成新对象，按引用恢复会落空。
        Assert.Contains("(i.Tag as PlanAction)?.ActionId == selectedId", body, StringComparison.Ordinal);
        // 恢复不到时落在新筛选第一条，仍经 Ui.SelectItem 唯一入口（Selected + Focused + 定位）。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _actionList\.Items\[0\]\);$", body);
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    // ---------- 接线：两页各管自己那一页的三个手势 ----------

    [Fact]
    public void TheThreeGestures_GoThroughTheSharedEntries_OnEachPage()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
        // 勾选页那一支（第七十三轮）逐字不变。
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryFocusFilter\(keyData, _keyword\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, _selectList\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _keyword\)\) return true;$", body);
        // 预览页那一支（本轮）：同一个框、同一张清单，仍只经三处共享入口。
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryFocusFilter\(keyData, _actionKeyword\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _actionKeyword, _actionList\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _actionKeyword\)\) return true;$", body);
        // 本对话框不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
        var code = CodeLines(source);
        Assert.DoesNotContain("Keys.F", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ListKeys.IsEnterGesture", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellShortcuts.IsFindGesture", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ListFilter.EscClearsInDialog", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_actionKeyword.Clear()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusFirstResult", code, StringComparison.Ordinal);
        // 对话框的 Enter=确认、Esc=取消在焦点不在框上时逐字不变。
        Assert.Matches(@"(?m)^\s*AcceptButton = _closeButton;$", source);
        Assert.Matches(@"(?m)^\s*CancelButton = _closeButton;$", source);
    }

    [Fact]
    public void EachPage_OnlyTouchesItsOwnKeywordBox()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
        // 门控按当前可见页走：勾选页那一支只认 _keyword，预览页那一支只认 _actionKeyword。
        var page1 = body[..body.IndexOf("else if (_page2.Visible)", StringComparison.Ordinal)];
        var page2 = body[body.IndexOf("else if (_page2.Visible)", StringComparison.Ordinal)..];
        Assert.DoesNotContain("_actionKeyword", page1, StringComparison.Ordinal);
        Assert.DoesNotContain("_actionList", page1, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword", page2, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectList", page2, StringComparison.Ordinal);
        // 两页各一个门控：一次按键只有一个含义。Ctrl+F 两页各一次（一个手势一个落点），
        // Enter 与 Esc 在预览页各两次——第八十六轮给未纳入清单补了第二个框，两个框各接一次。
        Assert.Single(Regex.Matches(body, @"_page1\.Visible"));
        Assert.Single(Regex.Matches(body, @"_page2\.Visible"));
        Assert.Equal(2, Regex.Matches(body, @"Ui\.TryFocusFilter\(").Count);
        Assert.Equal(3, Regex.Matches(body, @"Ui\.TryEnterFilter\(").Count);
        Assert.Equal(3, Regex.Matches(body, @"Ui\.TryEscapeFilter\(").Count);
        // Ctrl+F 只交给写入清单那个框：未纳入清单的框经 Tab 或点击进入，不为它发明第二个查找手势。
        Assert.DoesNotContain("TryFocusFilter(keyData, _excludedKeyword)", body, StringComparison.Ordinal);
    }

    // ---------- 接线：整份数据不可得时框停用 ----------

    [Fact]
    public void TheBox_IsDisabledWhenThePlanHasNoActions()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 计划没有动作：框停用（没有可筛的东西，下一步不在这框上）。
        Assert.Contains("_actionKeyword.Enabled = ListFilter.KeywordEnabled(plan.Actions.Count > 0);", source, StringComparison.Ordinal);
        // 作业运行期间清单正在被重建：同一处口径带上作业态。
        Assert.Contains("_actionKeyword.Enabled = ListFilter.KeywordEnabled(_plan is { Actions.Count: > 0 }, busy);", source, StringComparison.Ordinal);
        // 界面不就地写 true/false 字面。
        Assert.DoesNotContain("_actionKeyword.Enabled = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_actionKeyword.Enabled = true;", source, StringComparison.Ordinal);
    }

    // ---------- 共享编译与文档 ----------

    [Fact]
    public void SharedCompilation_IncludesTheGauge()
    {
        // 口径只有一份：断言对准主工程真实实现。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\PlanEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheClauseAndTheLedger()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("同步预览的写入清单（2026-11-08 第八十轮）", section, StringComparison.Ordinal);
        Assert.Contains("PlanEntries", section, StringComparison.Ordinal);
        Assert.Contains("PlanActionSearchPlaceholder", section, StringComparison.Ordinal);
        // 两页各一个框这件事要写清：否则下一轮会以为预览页的 Esc 也归勾选页。
        Assert.Contains("两页各有一个关键词框", section, StringComparison.Ordinal);
        var ledger = Repo.DesignSection(8);
        var row = ledger.Split('\n').Single(l => l.StartsWith("| 同步预览的写入清单（§6） |", StringComparison.Ordinal));
        Assert.Contains("第八十轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了。
        Assert.Contains("不改写入边界", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已真机验证", row, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    /// <summary>只算代码行：注释里引用「界面不就地写 Keys.F」是在说明理由，不算第二套判定。</summary>
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

    private static string MethodSlice(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到方法：" + signature);
        var depth = 0;
        var seen = false;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') { depth++; seen = true; }
            else if (source[i] == '}')
            {
                depth--;
                if (seen && depth == 0) return source[start..(i + 1)];
            }
        }
        return source[start..];
    }
}
