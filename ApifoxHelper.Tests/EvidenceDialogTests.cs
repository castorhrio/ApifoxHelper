using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 代码证据清单口径与接线断言（DESIGN.md §6「列表行」、§8 本轮条款）。
/// 这一屏存在的理由就是看代码：清单是「哪几条证据」，片段区是「那一条的代码」。
/// 基线形状可核对：片段区是一块没有标题的框、打开时又是空的（看不出这一格是什么、也看不出该点一行），
/// 而另六处只读清单（检查结果、核对传播、接口追踪明细、补充说明、注释回写、同步预览写入清单）
/// 打开即选中第一条——同一个动作在别处有、在这一处没有，属同壳不同行为，按第五十五、五十七、五十九轮
/// 那条判据判定为缺口，不是偏好。清单三格文字、计数行、片段区说明行与片段文字此前在对话框里就地拼写，
/// 现收进 <see cref="EvidenceEntries"/>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）。
/// 界面接线属 WinForms 控件触达、本环境不可执行，全部以源码切片与行首正则钉住，不冒充跑过。
/// </summary>
public class EvidenceDialogTests
{
    // ---------- 口径真执行：清单三格、计数行、片段文字 ----------

    [Fact]
    public void Row_CarriesTheThreeCellsInScreenOrder()
    {
        // 分类（映射与单元格同一份）、证据摘要（标识列）、文件:行。
        var cells = EvidenceEntries.Row("validation", "必填校验：订单号不能为空", "src/OrderService.cs", 42, 47);
        Assert.Equal(["校验", "必填校验：订单号不能为空", "src/OrderService.cs:42-47"], cells);
    }

    [Fact]
    public void Row_DoesNotPutTheLocationOrTheKindInPlaceOfAMissingSummary()
    {
        // 摘要缺失按空串处理：拿位置或分类顶上，就是把「没有摘要」说成「摘要就是文件名」。
        var cells = EvidenceEntries.Row("route", null, "src/Api.cs", 1, 3);
        Assert.Equal("", cells[1]);
        Assert.Equal("src/Api.cs:1-3", cells[2]);
    }

    [Theory]
    [InlineData("route", "路由")]
    [InlineData("guard", "守卫条件")]
    [InlineData("queryFilter", "查询过滤")]
    public void Row_TakesTheKindTextFromTheSharedMapping(string kind, string expected)
    {
        // 分类文字与清单单元格同源（TaskDisplay.EvidenceKindText），不在这份口径里另写一份中文表。
        Assert.Equal(expected, EvidenceEntries.Row(kind, "摘要", "f.cs", 1, 1)[0]);
    }

    [Fact]
    public void LocationText_IsFileAndLineRange()
    {
        Assert.Equal("src/A.cs:7-9", EvidenceEntries.LocationText("src/A.cs", 7, 9));
        // 单行证据的区间两端相同，仍如实写出两端（不折叠成「:7」——折叠是另一种形状，界面没有第二种读法）。
        Assert.Equal("src/A.cs:7-7", EvidenceEntries.LocationText("src/A.cs", 7, 7));
    }

    [Theory]
    [InlineData(1, "证据 1 条")]
    [InlineData(12, "证据 12 条")]
    [InlineData(0, "证据 0 条")]
    public void CountsLine_CountsTheFilteredEvidence(int count, string expected)
    {
        Assert.Equal(expected, EvidenceEntries.CountsLine(count));
    }

    [Theory]
    [InlineData("var x = 1;", "var x = 1;")]
    [InlineData("  ", "（无代码片段）")]
    [InlineData("", "（无代码片段）")]
    [InlineData(null, "（无代码片段）")]
    public void SnippetText_KeepsTheCodeAndNamesTheEmptyCase(string? snippet, string expected)
    {
        // 空白即「无代码片段」：不把空串当成一段代码显示，也不留一块空白（§7「空态不留白」）。
        Assert.Equal(expected, EvidenceEntries.SnippetText(snippet));
    }

    [Fact]
    public void SnippetCaption_SaysWhatTheBoxIsAndWhereTheLocationIs()
    {
        // 说明行要同时回答两件事：这一格是什么、文件与行号在哪看（在清单里，不在这一格）。
        Assert.Equal("选中证据的代码片段（文件与行号见清单）", EvidenceEntries.SnippetCaption);
        Assert.Equal("（无代码片段）", EvidenceEntries.EmptySnippetText);
    }

    // ---------- 接线：对话框不就地拼这几处文字 ----------

    [Fact]
    public void Dialog_TakesTheThreeCellsFromTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "EvidenceDialog.cs");
        Assert.Matches(@"(?m)^\s*var cells = EvidenceEntries\.Row\(item\.Kind, item\.Summary, item\.File, item\.StartLine, item\.EndLine\);$", source);
        Assert.Matches(@"(?m)^\s*var row = new ListViewItem\(cells\[0\]\) \{ Tag = item \};$", source);
        Assert.Matches(@"(?m)^\s*row\.SubItems\.Add\(cells\[1\]\);$", source);
        Assert.Matches(@"(?m)^\s*var location = row\.SubItems\.Add\(cells\[2\]\);$", source);
        // 就地拼写的老形状不得回来（分类映射、摘要回退、文件:行）。
        Assert.DoesNotContain("TaskDisplay.EvidenceKindText(item.Kind)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Summary ?? \"\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("item.File + \":\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_TakesTheCountsLineAndTheSnippetTextFromTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "EvidenceDialog.cs");
        Assert.Matches(@"(?m)^\s*_counts\.Text = EvidenceEntries\.CountsLine\(items\.Count\);$", source);
        Assert.Matches(@"(?m)^\s*_snippet\.Text = EvidenceEntries\.SnippetText\(item\.Snippet\);$", source);
        Assert.DoesNotContain("\"证据 \" + items.Count", source, StringComparison.Ordinal);
        Assert.DoesNotContain("（无代码片段）", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_TakesTheSnippetCaptionFromTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "EvidenceDialog.cs");
        Assert.Matches(@"(?m)^\s*var snippetCaption = Ui\.Caption\(EvidenceEntries\.SnippetCaption\);$", source);
        // 说明行字面只在口径文件里出现一次，界面不拼第二份。
        Assert.DoesNotContain("选中证据的代码片段", source, StringComparison.Ordinal);
    }

    // ---------- 片段区形状：说明行 + 发丝边内层 + 底部宿主 ----------

    [Fact]
    public void SnippetArea_HasACaptionAboveTheHairlineBox()
    {
        var source = Repo.Source("Desktop", "Native", "EvidenceDialog.cs");
        Assert.Matches(@"(?m)^\s*snippetCaption\.Dock = DockStyle\.Top;$", source);
        Assert.Matches(@"(?m)^\s*var snippetInner = Ui\.Hairline\(_snippet\);$", source);
        Assert.Matches(@"(?m)^\s*snippetInner\.Dock = DockStyle\.Fill;$", source);
        Assert.Matches(@"(?m)^\s*var snippetHost = new Panel \{ Dock = DockStyle\.Bottom, Height = \d+, BackColor = UiTokens\.Bg, Padding = new Padding\(0, UiTokens\.Sm, 0, 0\) \};$", source);
        // 停靠按集合倒序生效：先加内层、后加说明行，说明行才先停靠到顶部、内层填满剩余。
        var inner = source.IndexOf("snippetHost.Controls.Add(snippetInner);", StringComparison.Ordinal);
        var caption = source.IndexOf("snippetHost.Controls.Add(snippetCaption);", StringComparison.Ordinal);
        Assert.True(inner >= 0 && caption > inner, "说明行要加在内层之后（倒序停靠才把说明行放到顶部）。");
    }

    // ---------- 打开即选中第一条：与另六处只读清单同一口径 ----------

    [Fact]
    public void Open_SelectsTheFirstEvidenceThroughTheSingleEntry()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "EvidenceDialog.cs"), "protected override void OnLoad");
        // 恰一处，且经 Ui.SelectItem 唯一入口（Selected + Focused + 滚入可见区，片段随选中事件显示）。
        Assert.Single(Regex.Matches(body, @"(?m)^\s*Ui\.SelectItem\(_list\.Items\[0\]\);$"));
        // 排在填充之后：清单还空着时选中第一行就是索引一个不存在的行。
        var filled = body.IndexOf("_list.Items.Add(row);", StringComparison.Ordinal);
        var selected = body.IndexOf("Ui.SelectItem(_list.Items[0]);", StringComparison.Ordinal);
        Assert.True(filled >= 0 && selected > filled, "选中要排在清单填充之后。");
        // 不自己写选中/焦点/滚动（各有唯一入口）。
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focused = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_DoesNotIndexAnEmptyList()
    {
        // 没有证据的那一支在上面已经返回（没有可索引的第一行），这里不猜。
        var body = MethodSlice(Repo.Source("Desktop", "Native", "EvidenceDialog.cs"), "protected override void OnLoad");
        var emptyBranch = body.IndexOf("if (items.Count == 0)", StringComparison.Ordinal);
        var selected = body.IndexOf("Ui.SelectItem(_list.Items[0]);", StringComparison.Ordinal);
        Assert.True(emptyBranch >= 0 && selected > emptyBranch, "空清单分支要排在选中之前。");
        // 该分支里必须真的返回（否则会继续往下走到选中）。
        var branch = body[emptyBranch..selected];
        Assert.Contains("return;", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void Snippet_FollowsTheSelectionThroughTheSharedEntry()
    {
        // 片段随选中事件显示，读的是 Ui.SelectedItem 唯一入口（第六十一轮口径），不自己索引选中集合。
        var body = MethodSlice(Repo.Source("Desktop", "Native", "EvidenceDialog.cs"), "private void ShowSnippet");
        Assert.Contains("Ui.SelectedItem(_list)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItems[", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndices[", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyLists_AllSelectARowOnOpen()
    {
        // 这一族屏打开时都有一行被标出来（选中即出详情、按钮随之启用），证据对话框此前是唯一例外。
        foreach (var file in new[]
                 {
                     "CheckResultsDialog.cs", "PropagationDialog.cs", "EndpointTraceDialog.cs",
                     "SupplementsDialog.cs", "ApplyPlanDialog.cs", "SyncPreviewDialog.cs", "EvidenceDialog.cs",
                 })
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.True(
                Regex.IsMatch(source, @"Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);|Ui\.SelectItem\(_list\.Items\[0\]\);|Ui\.SelectItem\(target \?\? _actionList\.Items\[0\]\);"),
                $"{file} 打开时应把选中交给清单第一行。");
        }
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void SharedCompile_IncludesEvidenceEntries()
    {
        // 真值表跑的是主工程同一份代码：共享编译清单里必须有 EvidenceEntries.cs。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\EvidenceEntries.cs", csproj);
    }

    [Fact]
    public void Rule_LivesWithoutWinForms()
    {
        // 口径必须能在 Linux 真执行：不碰 WinForms、不碰控件类型。
        var rule = Repo.Source("Desktop", "Native", "EvidenceEntries.cs");
        Assert.DoesNotContain("System.Windows.Forms", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheSlice()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("代码证据清单", section);
        Assert.Contains("EvidenceEntries", section);
        Assert.Contains("打开即选中第一条证据", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 代码证据清单（§6） \|", ledger);
        Assert.Contains("第八十一轮", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimWhatThisRoundDidNotDo()
    {
        // 本轮没做的事不能被写成做了：证据清单不加关键词框（这一屏按位置过滤、条目少），
        // 也不改证据的读取与过滤口径。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 代码证据清单（§6） |", "\n| 输入框（§6） |");
        Assert.Contains("不加关键词框", row);
        Assert.Contains("不改证据的读取与过滤口径", row);
        Assert.DoesNotContain("关键词框已接线", row, StringComparison.Ordinal);
    }

    private static string MethodSlice(string source, string start)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf("\n    }", from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"{start} 找不到收尾大括号。");
        return source[from..to];
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
