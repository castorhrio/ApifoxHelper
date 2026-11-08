using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 筛选行形状收成一处（第六十八轮，DESIGN.md §6「列表筛选」）：五张带筛选下拉的清单（检查结果、差异确认、
/// 注释缺口、注释候选、响应契约）的筛选行此前四处各拼一遍，第五张（检查结果）拼成
/// <c>FlowLayoutPanel</c> + 240 宽 + <c>Sm</c> 间距，与另四张的 <c>TableLayoutPanel</c> + 220 宽 + <c>Xs</c> 间距
/// 是两种样子——同一个筛选行在五张清单上有两种排布（窄窗口下 Flow 把关键词框裁掉、Table 让末列吸收剩余宽度），
/// 属同壳不同行为，故收成 <c>Ui.FilterRow(筛选下拉, 关键词框, 计数标签)</c> 一处工厂。
/// 本文件钉住三件事：工厂的形状（五列、宽度常量、关键词框经 <c>Ui.Field</c> 包发丝边、计数可缺省）、
/// 五张清单都从工厂取（不各拼一遍）、以及「本轮没做的事不能被写成做了」。
/// 第七十二轮起第六张清单（同步预览的接口勾选页）也走同一份形状：它只有关键词框、没有筛选下拉，
/// 故走 counts 可缺省的那一支（<c>Ui.FilterRow(关键词框, 计数)</c> 重载），前两列留空、行形状仍是同一份；
/// 第七十五轮第七张清单（接口追踪明细的接口清单）同走这一支（计数在表头行，故 counts 传 null）；
/// 第七十六轮第八张清单（补充说明的接口清单）同走这一支（计数在底部动作行，故 counts 传 null）。
/// 工厂本身是 WinForms 控件触达，本环境不可执行，全部以源码切片与行首正则钉住，不冒充跑过；
/// 真机上五列筛选行的排布与窄窗口下的表现随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class FilterRowFactoryTests
{
    private static readonly string[] FiveLists =
    [
        "CheckResultsDialog.cs",
        "DecisionsDialog.cs",
        "ScanResultsDialog.cs",
        "CandidatesDialog.cs",
        "ContractsDialog.cs",
    ];

    // ---------- 关键词框宽度：一个数，一处落点 ----------

    [Fact]
    public void KeywordWidth_HasOneHome()
    {
        // 常量值从源码取（Ui.cs 是 WinForms 文件，不编进测试程序集）。
        Assert.Matches(@"(?m)^\s*public const int FilterKeywordWidth = 220;$", Repo.Source("Desktop", "Native", "Ui.cs"));
        // 工厂取常量，不就地写数。
        Assert.Contains("keyword.Width = FilterKeywordWidth;", FactoryBody(), StringComparison.Ordinal);
        // 五张清单都不自己量宽度：各写一个数就有一处先漂（检查结果那一屏曾写 240）。
        foreach (var file in FiveLists)
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        }
        // 关键词框的宽度在整个原生界面里只由工厂给（两个主页面的筛选下拉各写 220 是另一件事：
        // 那是下拉框的宽度，不是关键词框）；检查结果那一屏此前的 240 一并消失。
        foreach (var source in Repo.NativeSources())
        {
            Assert.Equal(0, CodeLinesContaining(source.Text, "_keyword.Width"));
            Assert.Equal(0, CodeLinesContaining(source.Text, "Width = 240"));
        }
    }

    // ---------- 工厂形状：五列、发丝边、计数可缺省 ----------

    [Fact]
    public void Factory_IsATableOfFiveColumns()
    {
        var body = FactoryBody();
        Assert.Contains("ColumnCount = 5,", body, StringComparison.Ordinal);
        // 前四列按内容自适应、末列吸收剩余宽度（窄窗口下被裁掉的是留白，不是关键词框）。
        Assert.Equal(4, Regex.Matches(body, @"row\.ColumnStyles\.Add\(new ColumnStyle\(SizeType\.AutoSize\)\);").Count);
        Assert.Single(Regex.Matches(body, @"row\.ColumnStyles\.Add\(new ColumnStyle\(SizeType\.Percent, 100\)\);").Cast<Match>());
        // 停靠与底色：与另四张长清单同一份（Top 停靠、Panel 底、底部 Sm 留白、无外边距）。
        Assert.Contains("Dock = DockStyle.Top,", body, StringComparison.Ordinal);
        Assert.Contains("AutoSize = true,", body, StringComparison.Ordinal);
        Assert.Contains("BackColor = UiTokens.Panel,", body, StringComparison.Ordinal);
        Assert.Contains("Padding = new Padding(0, 0, 0, UiTokens.Sm),", body, StringComparison.Ordinal);
        Assert.Contains("Margin = new Padding(0),", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_CarriesTheThreeThingsInOrder()
    {
        var body = FactoryBody();
        // 筛选 | 下拉 | 关键词 | 关键词框 | 计数——次序就是这一行的读法，换位等于换一种排布。
        Assert.Contains("row.Controls.Add(filterCaption, 0, 0);", body, StringComparison.Ordinal);
        Assert.Contains("row.Controls.Add(filter, 1, 0);", body, StringComparison.Ordinal);
        Assert.Contains("row.Controls.Add(keywordCaption, 2, 0);", body, StringComparison.Ordinal);
        Assert.Contains("row.Controls.Add(Field(keyword), 3, 0);", body, StringComparison.Ordinal);
        Assert.Contains("row.Controls.Add(counts, 4, 0);", body, StringComparison.Ordinal);
        // 关键词框经 Ui.Field 包 1px 发丝边（§6「输入框」：放入界面时必须经 Field 包装）。
        Assert.Contains("Field(keyword)", body, StringComparison.Ordinal);
        // 两个说明字只在这里出现一次（界面不各写一遍）。
        Assert.Equal(1, Occurrences(body, "\"筛选\""));
        Assert.Equal(1, Occurrences(body, "\"关键词\""));
    }

    [Fact]
    public void Factory_LeavesTheLastColumnEmptyWhenThereIsNoCounts()
    {
        var body = FactoryBody();
        // 计数不在筛选行上的视图（检查结果的计数在表头行）传 null：末列留空、行形状仍是同一份。
        var guard = body.IndexOf("if (counts is not null)", StringComparison.Ordinal);
        Assert.True(guard >= 0, "工厂缺少 counts 可缺省的守卫——没有计数的视图会被塞进一个空标签。");
        var add = body.IndexOf("row.Controls.Add(counts, 4, 0);", StringComparison.Ordinal);
        Assert.True(add > guard, "计数必须在守卫之内加入：守卫之外加入等于给没有计数的视图也占一格。");
        // 计数的三处对齐也在守卫之内（缺省时不该动一个不存在的标签）。
        foreach (var line in new[] { "counts.AutoSize = false;", "counts.Dock = DockStyle.Fill;", "counts.TextAlign = ContentAlignment.MiddleRight;" })
        {
            var at = body.IndexOf(line, StringComparison.Ordinal);
            Assert.True(at > guard, $"{line} 必须在 counts 守卫之内。");
        }
    }

    // ---------- 五张清单都从工厂取，不各拼一遍 ----------

    [Theory]
    [InlineData("CheckResultsDialog.cs", "counts: null")]
    [InlineData("DecisionsDialog.cs", "_counts")]
    [InlineData("ScanResultsDialog.cs", "_counts")]
    [InlineData("CandidatesDialog.cs", "_counts")]
    [InlineData("ContractsDialog.cs", "_counts")]
    public void EachList_TakesTheRowFromTheFactory(string file, string countsArgument)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var expected = countsArgument == "counts: null"
            ? @"(?m)^\s*var filterRow = Ui\.FilterRow\(_filter, _keyword, counts: null\);$"
            : @"(?m)^\s*var filterRow = Ui\.FilterRow\(_filter, _keyword, _counts\);$";
        Assert.Matches(expected, source);
        // 恰一处：同一屏拼两遍就是两套排布。
        Assert.Single(Regex.Matches(source, @"Ui\.FilterRow\(").Cast<Match>());
        // 界面不再自己拼这一行：列数、三件东西的加入、关键词框的描边包装都不在界面里。
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(_filter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(Ui.Field(_keyword)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(_counts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.Field(_keyword)", source, StringComparison.Ordinal);
        // 筛选行不是 Flow：Flow 会把关键词框在窄窗口下裁掉（检查结果那一屏此前的样子）。
        // 按钮行仍是 Flow（那是另一件事），故只禁「同一行里既有 Flow 又有筛选下拉」。
        Assert.DoesNotContain(source.Split('\n'), l =>
            l.Contains("FlowLayoutPanel", StringComparison.Ordinal) && l.Contains("_filter", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRowShape_IsAssembledInExactlyOnePlace()
    {
        // 「筛选」这个说明字只在工厂里出现一次：五张清单共用一份形状，谁再拼一遍就是第二套。
        // （`ColumnCount = 5` 不能当锚点——补充说明与执行计划两屏的表格也是五列，与筛选行无关。）
        var withFilterCaption = Repo.NativeSources().Where(s => CodeLinesContaining(s.Text, "Body(\"筛选\")") > 0).Select(s => s.Name).ToList();
        Assert.Equal(["Ui.cs"], withFilterCaption);
        // 关键词框的描边包装也只在工厂里（界面交的是裸框）：`Ui.Field(_keyword)` 在整个原生界面零次，
        // 工厂内部写的是 `Field(keyword)`（同一个类里不写类名）。
        Assert.DoesNotContain(Repo.NativeSources(), s => s.Contains("Ui.Field(_keyword)"));
        Assert.Contains("Field(keyword)", FactoryBody(), StringComparison.Ordinal);
        // 工厂的调用点恰为这五张清单加同步预览的接口勾选页（第七十二轮）、接口追踪明细（第七十五轮）、
        // 补充说明（第七十六轮）与生成文档核对条目（第七十七轮）——后四处走的是只有关键词框的那一支。
        var callers = Repo.NativeSources().Where(s => s.Contains("Ui.FilterRow(")).Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(FiveLists.Append("SyncPreviewDialog.cs").Append("EndpointTraceDialog.cs").Append("SupplementsDialog.cs").Append("PropagationDialog.cs").OrderBy(n => n, StringComparer.Ordinal).ToList(), callers);
    }

    [Fact]
    public void KeywordOnlyList_TakesTheRowFromTheSameFactory()
    {
        // 同步预览的接口勾选页没有筛选下拉：走 counts 可缺省的那一支重载，前两列留空、行形状仍是同一份五列。
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, _visibleCount\);$", source);
        Assert.Single(Regex.Matches(source, @"Ui\.FilterRow\(").Cast<Match>());
        // 界面不自己拼这一行，也不自己量关键词框的宽度。
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        // 重载只是同一份形状的入口：它把筛选下拉传 null，不另起一种排布。
        Assert.Contains("public static TableLayoutPanel FilterRow(TextBox keyword, Label? counts) => FilterRow(null, keyword, counts);", FactoryOverload());
    }

    [Fact]
    public void CheckResults_KeepsItsCountsInTheHeadRow()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        // 这一屏的计数在表头行（与「计算时间」同排），故筛选行传 null——不是漏了计数。
        Assert.Matches(@"(?m)^\s*headRow\.Controls\.Add\(_counts, 1, 0\);$", source);
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_filter, _keyword, counts: null\);$", source);
    }

    [Fact]
    public void ScanResults_KeepsItsCoverageRowSpanningTheWholeRow()
    {
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        // 覆盖率行加在第二行、跨满五列：工厂的列数与这里的跨度必须是同一个数，漏改就是覆盖率行只盖住前几格。
        Assert.Matches(@"(?m)^\s*filterRow\.RowCount = 2;$", source);
        Assert.Matches(@"(?m)^\s*filterRow\.Controls\.Add\(_coverage, 0, 1\);$", source);
        Assert.Matches(@"(?m)^\s*filterRow\.SetColumnSpan\(_coverage, 5\);$", source);
        Assert.Contains("ColumnCount = 5,", FactoryBody(), StringComparison.Ordinal);
    }

    // ---------- 文档：条款与台账 ----------

    [Fact]
    public void DesignDoc_RecordsTheSharedRowShape()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("筛选行形状收成一处", section, StringComparison.Ordinal);
        Assert.Contains("Ui.FilterRow", section, StringComparison.Ordinal);
        Assert.Contains("Ui.FilterKeywordWidth", section, StringComparison.Ordinal);
        // 缺省计数的形状也要写清：否则下一轮会以为检查结果那一屏漏了计数。
        Assert.Contains("counts: null", section, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var section = Repo.DesignSection(8);
        var row = section.Split('\n').FirstOrDefault(l => l.StartsWith("| 筛选行形状（§6） |", StringComparison.Ordinal));
        Assert.NotNull(row);
        Assert.Contains("第六十八轮", row!, StringComparison.Ordinal);
        // 台账要如实：这一轮只收形状，不改筛选口径、不加视觉语言。
        Assert.Contains("不改筛选口径", row!, StringComparison.Ordinal);
        Assert.Contains("G-D5", row!, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimWhatThisRoundDidNotDo()
    {
        var section = Repo.DesignSection(6);
        // 本轮没有新增视觉语言、没有改筛选选项与匹配规则、没有把 ComboBox 收进工厂。
        Assert.DoesNotContain("筛选行新增", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ComboBox 已并入工厂", section, StringComparison.Ordinal);
        // 筛选选项集合与顺序、占位文字、匹配规则、筛到零行那句话逐字不动——条款里要写明这一点。
        Assert.Contains("筛选选项集合与顺序", section, StringComparison.Ordinal);
    }

    // ---------- 基建 ----------

    /// <summary>只有关键词框的那一支重载（第七十二轮）：一行表达式，转调同一份形状。</summary>
    private static string FactoryOverload()
    {
        var source = Repo.Source("Desktop", "Native", "Ui.cs");
        var start = source.IndexOf("public static TableLayoutPanel FilterRow(TextBox keyword, Label? counts)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 缺少只有关键词框的那一支重载。");
        var end = source.IndexOf(';', start);
        Assert.True(end > start, "重载缺少表达式体。");
        return source[start..(end + 1)];
    }

    /// <summary>Ui.FilterRow 的方法体（签名行到 return row; 为止）。</summary>
    private static string FactoryBody()
    {
        var source = Repo.Source("Desktop", "Native", "Ui.cs");
        var start = source.IndexOf("public static TableLayoutPanel FilterRow(", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 缺少 Ui.FilterRow 工厂。");
        var end = source.IndexOf("return row;", start, StringComparison.Ordinal);
        Assert.True(end > start, "Ui.FilterRow 方法体缺少 return row;。");
        return source[start..(end + "return row;".Length)];
    }

    private static int Occurrences(string haystack, string needle) => haystack.Split(needle).Length - 1;

    /// <summary>出现该字面量的代码行数（注释行跳过：注释里引用是说明理由，不是第二套拼写）。</summary>
    private static int CodeLinesContaining(string text, string fragment) =>
        text.Split('\n').Count(l =>
        {
            var trimmed = l.TrimStart();
            return trimmed.Contains(fragment, StringComparison.Ordinal)
                && !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal);
        });
}
