using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 第七十九轮：注释回写的源码文件清单关键词框（DESIGN.md §6「列表筛选」）。
/// 这一屏的清单是整份计划的源码文件（真实项目一个模块几十个文件），要找到「上次那个文件」只能逐行扫；
/// 另九张长清单在第六十五至七十八轮都有关键词框，同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 这一屏与那九张有一处不同：**差异预览挂在选中上**，筛选要重建清单、重建会换选中——
/// 但差异是只读的，换选中只是换一份预览，没有未保存的输入会丢（与第七十六轮补充说明的编辑位不同），
/// 故不需要 `_rebuilding` 守卫，只需按源码文件路径恢复选中。
/// 口径是真执行（<see cref="ApplyPlanEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 界面接线属 WinForms 控件触达、本环境不可执行，全部以源码切片与行首正则钉住，不冒充跑过。
/// 真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ApplyPlanFilterTests
{
    // ---------- 真执行：回写范围列文字与关键词字段 ----------

    [Theory]
    [InlineData(true, "允许回写")]
    [InlineData(false, "只导出补丁")]
    public void ScopeText_IsTheSameTextTheCellShows(bool writeAllowed, string expected)
    {
        // 清单第二格与关键词匹配取同一份：两处各写一套必然漂移（敲「只导出补丁」筛不到那一行）。
        Assert.Equal(expected, ApplyPlanEntries.ScopeText(writeAllowed));
    }

    [Fact]
    public void KeywordFields_AreTheThreeCellsOfThatRow()
    {
        // 那一行在屏幕上被读作的三格：源码文件、回写范围、回写结果。
        var fields = ApplyPlanEntries.KeywordFields("src/Orders/OrderController.cs", "允许回写", "已回写");
        Assert.Equal(3, fields.Length);
        Assert.Equal(new string?[] { "src/Orders/OrderController.cs", "允许回写", "已回写" }, fields);
        // 注释处数是数字、不进关键词（与另几张清单的计数列同一取舍）：字段里没有它。
        Assert.DoesNotContain(fields, f => f is "12");
    }

    // ---------- 真执行：匹配规则走 ListFilter 同一份 ----------

    [Theory]
    [InlineData("orders", true)]        // 路径（忽略大小写）
    [InlineData("OrderController", true)]
    [InlineData("只导出补丁", true)]     // 回写范围
    [InlineData("已回写", true)]         // 回写结果
    [InlineData("跳过", false)]          // 跳过原因在下方另一张清单，不在这一行
    [InlineData("", true)]               // 空关键词即全部行
    public void Matches_TheThreeCellsOnly(string query, bool expected)
    {
        var fields = ApplyPlanEntries.KeywordFields("src/Orders/OrderController.cs", "只导出补丁", "已回写");
        Assert.Equal(expected, ListFilter.Matches(query, fields));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndOnlyChangesVisibleRows()
    {
        var rows = new[] { "src/A.cs", "src/B.cs", "src/C.cs" };
        var visible = ListFilter.Apply(rows, "b", path => ApplyPlanEntries.KeywordFields(path, "允许回写", ""));
        Assert.Equal(["src/B.cs"], visible);
        // 数据源自身不被改写（与 ListSort.Order 同一纪律）。
        Assert.Equal(["src/A.cs", "src/B.cs", "src/C.cs"], rows);
        // 空关键词返回数据源顺序的副本。
        Assert.Equal(rows, ListFilter.Apply(rows, "  ", path => ApplyPlanEntries.KeywordFields(path, "允许回写", "")));
    }

    [Fact]
    public void IsFilteredEmpty_SeparatesTheTwoEmpties()
    {
        // 有文件、只是当前关键词不命中：这一屏的空在筛选上。
        Assert.True(ListFilter.IsFilteredEmpty(3, 0));
        // 整份计划没有文件：下一步不在筛选上（那一屏由提示出口说话）。
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
        // 筛掉一部分不是空。
        Assert.False(ListFilter.IsFilteredEmpty(3, 1));
    }

    [Fact]
    public void FilteredEmptySentence_AndPlaceholder_AreTheSharedOnes()
    {
        // 这一屏没有筛选下拉，故下一步只指那个框（文字不指向界面上按不动的控件）。
        Assert.Equal("这个关键词下没有源码文件。换一个关键词再试。",
            ListFilter.FilteredEmptyKeywordHint(ListFilter.ApplyPlanNoun));
        Assert.Equal("筛选文件（路径、回写范围、结果）", ListFilter.ApplyPlanSearchPlaceholder);
        // 名词与另六张清单同一份词汇表，不另起一份。
        Assert.Equal("源码文件", ListFilter.ApplyPlanNoun);
    }

    [Fact]
    public void CountsLine_IsTheWholePlanNumbers()
    {
        // 计数行按整份计划统计、不随关键词变化（「跳过 N」正是要找的那批的凭据）。
        Assert.Equal("文件 12 · 注释 34 · 跳过 5", ApplyPlanEntries.CountsLine(12, 34, 5));
        Assert.Equal("文件 0 · 注释 0 · 跳过 0", ApplyPlanEntries.CountsLine(0, 0, 0));
    }

    // ---------- 接线：关键词框与筛选行经工厂 ----------

    [Fact]
    public void TheRow_ComesFromTheFactory_AndTheBoxIsNotMeasuredHere()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        // 这一屏没有筛选下拉：走 counts 可缺省的那一支（计数在表头行）。
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, counts: null\);$", source);
        // 恰为这一屏该有的行数（第一百一十二轮给跳过清单补了第二个框）：两行都走同一支重载、
        // 形状仍是同一份，故这里钉的是「两张清单各一行」，不是放宽成「至少一处」。
        Assert.Equal(2, Regex.Matches(source, @"Ui\.FilterRow\(").Count);
        Assert.Matches(@"(?m)^\s*var skippedFilterRow = Ui\.FilterRow\(_skippedKeyword, counts: null\);$", source);
        // 界面不自己拼这一行，也不自己量关键词框的宽度。
        // （`ColumnCount = 5` 不能当锚点——本对话框的按钮行也是五列，与筛选行无关。）
        Assert.DoesNotContain("filterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        // 占位文字取共享常量，不就地拼写。
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.ApplyPlanSearchPlaceholder;$", source);
        Assert.DoesNotContain("筛选文件（", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRow_IsDockedBelowTheHeadRow()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        // 停靠按集合倒序生效：筛选行要在标题行之前加入，才停靠在标题行之下、列表之上。
        var filter = source.IndexOf("Controls.Add(filterRow);", StringComparison.Ordinal);
        var head = source.IndexOf("Controls.Add(headRow);", StringComparison.Ordinal);
        Assert.True(filter >= 0 && head > filter, "筛选行要排在标题行之前加入（倒序停靠后落在标题行之下）。");
    }

    // ---------- 接线：筛到零行那一支 ----------

    [Fact]
    public void FilteredEmptyBranch_KeepsTheBoxAndTheWholePlanCounts()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs"), "private void PopulateList");
        // 判定经共享口径，入参是整份计划的文件数与本次可见行数。
        Assert.Matches(@"(?m)^\s*if \(ListFilter\.IsFilteredEmpty\(plan\.Files\.Count, visible\.Count\)\)$", body);
        // 那句话取共享组合（这一屏没有筛选下拉，只指那个框）。
        Assert.Matches(@"(?m)^\s*_hint\.Text = ListFilter\.FilteredEmptyKeywordHint\(ListFilter\.ApplyPlanNoun\);$", body);
        // 关键词框留着（§7「禁用不得移除下一步」）：这一支不出现 _keyword.Enabled。
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Enabled", body[branch..], StringComparison.Ordinal);
        // 计数行不随关键词变化：这一支不碰 _counts。
        Assert.DoesNotContain("_counts.Text", body, StringComparison.Ordinal);
        // 差异预览经既有的无选中路径收口，不另写一套收控件的赋值。
        Assert.Matches(@"(?m)^\s*ShowDiff\(\);$", body);
        Assert.DoesNotContain("_diff.Text =", body, StringComparison.Ordinal);
        // 分支先返回、回显在后：写在提示之前等于这句话刚显示就被自己抹掉。
        var hint = body.IndexOf("FilteredEmptyKeywordHint", StringComparison.Ordinal);
        var restore = body.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(hint >= 0 && restore > hint, "筛到零行的分支要排在列表回显之前。");
        Assert.Matches(@"(?m)^\s*_list\.Visible = true;$", body);
    }

    [Fact]
    public void CountsLine_IsComputedOnceFromTheWholePlan()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        // 计数只在 ShowPlan 按整份计划算一次（PopulateList 不重算，否则关键词一变数字就变）。
        Assert.Single(Regex.Matches(source, @"ApplyPlanEntries\.CountsLine\(").Cast<Match>());
        Assert.Matches(@"(?m)^\s*_counts\.Text = ApplyPlanEntries\.CountsLine\(plan\.Files\.Count, plan\.Files\.Sum\(f => f\.Edits\.Count\), plan\.Skipped\.Count\);$", source);
    }

    // ---------- 接线：选中按路径恢复，经唯一入口 ----------

    [Fact]
    public void Selection_IsRestoredByPath_ThroughTheSingleEntry()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs"), "private void PopulateList");
        // 先记路径再清空清单（清空会抹掉选中，之后取到的就是 null）。
        var remember = body.IndexOf("var selectedPath = (Ui.SelectedItem(_list)?.Tag as PlannedFile)?.RelativePath;", StringComparison.Ordinal);
        var clear = body.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "选中路径要在清空清单之前取局部。");
        // 恢复不到时落在新筛选第一条，仍经 Ui.SelectItem 唯一入口（Selected + Focused + 定位）。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);$", body);
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    // ---------- 接线：三个手势只经共享入口 ----------

    [Fact]
    public void TheThreeGestures_GoThroughTheSharedEntries()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
        // 第二百二十六轮起：两个框登记在同一张表里（主框是文件清单那个），落点由 FilterGesturePlan 判定
        // （Ctrl+F → Enter → Esc 的先后也在那里），对话框只留一行分发。
        Assert.Matches(@"(?m)^\s*if \(_gestures\.Dispatch\(keyData\)\) return true;$", body);
        Assert.Contains("new FilterGestures(_keyword, _list).Then(_skippedKeyword, _skippedList)", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryFocusFilter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryEnterFilter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryEscapeFilter", CodeLines(source), StringComparison.Ordinal);
        // 本对话框不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
        var code = CodeLines(source);
        Assert.DoesNotContain("Keys.F", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ListKeys.IsEnterGesture", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellShortcuts.IsFindGesture", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ListFilter.EscClearsInDialog", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Clear()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusFirstResult", code, StringComparison.Ordinal);
        // 对话框的 Enter=确认、Esc=取消在焦点不在框上时逐字不变。
        Assert.Matches(@"(?m)^\s*AcceptButton = closeButton;$", source);
        Assert.Matches(@"(?m)^\s*CancelButton = closeButton;$", source);
    }

    // ---------- 接线：整份数据不可得时框停用 ----------

    [Fact]
    public void TheBox_IsDisabledWhenTheWholePlanIsUnavailable()
    {
        var source = Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");
        // 提示出口（读取失败/生成失败）与「计划没有可写入的文件」两支都停用，有数据时恢复。
        Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(false);", source, StringComparison.Ordinal);
        Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(true);", source, StringComparison.Ordinal);
        // 作业运行期间清单正在被重建：同一处口径带上作业态。
        Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(_plan is { Files.Count: > 0 }, busy);", source, StringComparison.Ordinal);
        // 界面不就地写 true/false 字面。
        Assert.DoesNotContain("_keyword.Enabled = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Enabled = true;", source, StringComparison.Ordinal);
    }

    // ---------- 共享编译与文档 ----------

    [Fact]
    public void SharedCompilation_IncludesTheGauge()
    {
        // 口径只有一份：断言对准主工程真实实现。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ApplyPlanEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheClauseAndTheLedger()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("注释回写的源码文件清单（2026-11-08 第七十九轮）", section, StringComparison.Ordinal);
        Assert.Contains("ApplyPlanEntries", section, StringComparison.Ordinal);
        Assert.Contains("ApplyPlanSearchPlaceholder", section, StringComparison.Ordinal);
        var ledger = Repo.DesignSection(8);
        var row = ledger.Split('\n').Single(l => l.StartsWith("| 注释回写的文件清单（§6） |", StringComparison.Ordinal));
        Assert.Contains("第七十九轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了。
        Assert.Contains("不改回写口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已真机验证", row, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    /// <summary>只算代码行：注释里引用「界面不就地写 Keys.F」是在说明理由，不算第二套判定。</summary>
    private static string CodeLines(string source) => CodeScan.Text(source);

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
