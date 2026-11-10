using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 第八十六轮：同步预览「未纳入计划」清单的关键词框（DESIGN.md §6「列表筛选」）。
/// 同一个预览页的写入清单自第八十轮起就有关键词框、未纳入清单没有——而这一张往往更长
/// （计划只纳入需要写入的接口与模型，其余全部落在这里：「无需修改」「检查之后远端已被修改」
/// 「引用的模型存在阻断项」…），真实项目里几十上百条，要找到「上次那个接口为什么没进计划」只能逐行扫，
/// 属同壳不同行为。这一屏没有筛选下拉（未纳入原因是逐条结果、不是可选状态），故走只有关键词框的那一支；
/// 匹配的是那一行在屏幕上被读作的那两格（资源、未纳入原因）——原因进关键词是这一屏的要害。
/// 同一页两个框：Enter 与 Esc 看焦点各接一次，Ctrl+F 只交给写入清单那个框。
/// 口径是真执行（<see cref="ExclusionEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 界面接线属 WinForms 控件触达、本环境不可执行，全部以源码切片与行首正则钉住，不冒充跑过。
/// 真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ExclusionFilterTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：匹配的是那一行在屏幕上被读作的那两格 ----------

    [Fact]
    public void KeywordFields_AreTheResourceAndTheReason()
    {
        var fields = ExclusionEntries.KeywordFields("GET /orders", "无需修改（结构差异只来自共享模型，由模型动作处理）");
        Assert.Equal(2, fields.Length);
        Assert.Equal("GET /orders", fields[0]);
        Assert.Equal("无需修改（结构差异只来自共享模型，由模型动作处理）", fields[1]);
    }

    [Theory]
    [InlineData("orders", true)]         // 资源（忽略大小写）
    [InlineData("GET", true)]            // 资源片段
    [InlineData("无需修改", true)]        // 原因——这一屏的要害：用户问的正是「为什么没进计划」
    [InlineData("共享模型", true)]        // 原因片段
    [InlineData("model:OrderDto", false)] // 另一条的资源不在这一行里
    [InlineData("不存在的条目", false)]
    [InlineData("", true)]               // 空关键词即全部行
    public void Matches_TheTwoCellsOnly(string query, bool expected)
    {
        var fields = ExclusionEntries.KeywordFields("GET /orders", "无需修改（结构差异只来自共享模型，由模型动作处理）");
        Assert.Equal(expected, ListFilter.Matches(query, fields));
    }

    [Theory]
    [InlineData("model:OrderDto", true)]  // 模型条目的资源
    [InlineData("远端模型已被修改", true)] // 模型条目的原因
    [InlineData("orders", false)]         // 接口条目的资源不在这一行里
    [InlineData("", true)]
    public void Matches_TheModelRowToo(string query, bool expected)
    {
        var fields = ExclusionEntries.KeywordFields("model:OrderDto", "检查之后远端模型已被修改，请重新检查");
        Assert.Equal(expected, ListFilter.Matches(query, fields));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndFiltersOnlyTheVisibleRows()
    {
        var excluded = new List<PlanExclusion>
        {
            new() { ResourceKey = "GET /orders", Reason = "无需修改" },
            new() { ResourceKey = "model:OrderDto", Reason = "检查之后远端模型已被修改，请重新检查" },
            new() { ResourceKey = "POST /orders", Reason = "引用的模型 OrderDto 存在阻断项或未纳入计划" },
        };
        var all = ListFilter.Apply(excluded, "", e => ExclusionEntries.KeywordFields(e.ResourceKey, e.Reason));
        Assert.Equal(excluded, all);
        // 「orders」只命中两条接口的资源（模型条目的资源是 model:OrderDto、原因里也没有这个词）。
        var orders = ListFilter.Apply(excluded, "orders", e => ExclusionEntries.KeywordFields(e.ResourceKey, e.Reason));
        Assert.Equal(["GET /orders", "POST /orders"], orders.Select(e => e.ResourceKey));
        var blocked = ListFilter.Apply(excluded, "阻断项", e => ExclusionEntries.KeywordFields(e.ResourceKey, e.Reason));
        Assert.Equal(["POST /orders"], blocked.Select(e => e.ResourceKey));
        var none = ListFilter.Apply(excluded, "不存在的条目", e => ExclusionEntries.KeywordFields(e.ResourceKey, e.Reason));
        Assert.Empty(none);
        // 筛到零行是「有未纳入条目、只是不命中」，不是「计划没有未纳入条目」——两者的下一步不同。
        Assert.True(ListFilter.IsFilteredEmpty(excluded.Count, none.Count));
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
    }

    [Fact]
    public void FilteredEmptySentence_PointsAtTheKeywordBoxOnly()
    {
        // 这一屏没有筛选下拉：说「换一个筛选」就是指向界面上按不动的控件。
        Assert.Equal("这个关键词下没有未纳入条目。换一个关键词再试。", ListFilter.FilteredEmptyKeywordHint(ListFilter.ExclusionNoun));
        Assert.Equal("筛选未纳入条目（资源、原因）", ListFilter.ExclusionSearchPlaceholder);
        // 与写入清单的名词分开：同一个预览页两张清单清的不是同一样东西。
        Assert.NotEqual(ListFilter.ActionNoun, ListFilter.ExclusionNoun);
    }

    // ---------- 接线：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_TakesTheThirdKeywordBoxFromTheFactories()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 关键词框经工厂出厂（§6「输入框」），筛选行经工厂组装（§6「列表筛选」），界面不各拼一遍。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _excludedKeyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_excludedKeyword\.PlaceholderText = ListFilter\.ExclusionSearchPlaceholder;$", source);
        Assert.Matches(@"(?m)^\s*_excludedKeyword\.TextChanged \+= \(_, _\) => PopulateExcluded\(\);$", source);
        Assert.Matches(@"(?m)^\s*var excludedFilterRow = Ui\.FilterRow\(_excludedKeyword, counts: null\);$", source);
        // 界面不自己拼这一行、不自己量宽度、不就地拼占位文字。
        Assert.DoesNotContain("excludedFilterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_excludedKeyword.Width", source, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选未纳入条目（资源、原因）", source, StringComparison.Ordinal);
        // 筛选行排在标题行之后加入（停靠按集合倒序生效，标题行在最上、筛选行在其下、清单填满剩余）。
        var filterAt = source.IndexOf("_excludedHost.Controls.Add(excludedFilterRow);", StringComparison.Ordinal);
        var captionAt = source.IndexOf("_excludedHost.Controls.Add(excludedCaption);", StringComparison.Ordinal);
        Assert.True(filterAt >= 0 && captionAt > filterAt, "筛选行要排在标题行之前加入（倒序停靠后标题行在最上）。");
    }

    [Fact]
    public void FilteredEmptyBranch_KeepsTheBoxAndSaysTheNextStep()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", File), "private void PopulateExcluded");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(branch >= 0, "筛到零行要经共享判定，不就地写「行数 == 0」。");
        var tail = body[branch..];
        // 那句话与下一步：只指那个框（这一屏没有筛选下拉），关键词框留着（§7「禁用不得移除下一步」）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.ExclusionNoun)", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_excludedKeyword.Enabled", tail, StringComparison.Ordinal);
        // 清单藏起来、提示显示出来；分支先返回、回显在后。
        Assert.Contains("_excludedList.Visible = false;", tail, StringComparison.Ordinal);
        Assert.Contains("_excludedHint.Visible = true;", tail, StringComparison.Ordinal);
        Assert.True(tail.IndexOf("_excludedHint.Visible = false;", StringComparison.Ordinal) > tail.IndexOf("return;", StringComparison.Ordinal),
            "筛到零行的分支要排在清单回显之前。");
        // 未纳入清单没有选中语义：不恢复选中、不碰键盘焦点、不自己滚动。
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_AreStillComputedOnceFromTheWholePlan()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 计数行（含「排除 N」）只在 ShowPlan 里按整份计划算一次：放进重建里就等于随关键词变化。
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_planCounts\.Text = PlanEntries\.CountsLine\(").Cast<Match>());
        Assert.DoesNotContain("_planCounts.Text", MethodSlice(source, "private void PopulateExcluded"), StringComparison.Ordinal);
    }

    [Fact]
    public void BoxIsDisabledWhenTheWholeDataIsUnavailable()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 只有关键词框的那一支：停用与恢复都经共享口径，界面不就地写 true/false 字面。
        Assert.Contains("_excludedKeyword.Enabled = ListFilter.KeywordEnabled(plan.Excluded.Count > 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_excludedKeyword.Enabled = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_excludedKeyword.Enabled = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_DoesNotRecognizeTheGesturesItself()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = MethodSlice(source, "protected override bool ProcessDialogKey(Keys keyData)");
        // 同一页两个框各接 Enter 与 Esc（判定看焦点，一次按键只有一个含义），Ctrl+F 只交给写入清单那个框。
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _excludedKeyword, _excludedList\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _excludedKeyword\)\) return true;$", body);
        // Ctrl+F 两页各一次（勾选页的接口框、预览页的写入清单框），未纳入清单的框不接查找手势。
        Assert.Equal(2, Regex.Matches(body, @"Ui\.TryFocusFilter\(").Count);
        Assert.DoesNotContain("TryFocusFilter(keyData, _excludedKeyword)", body, StringComparison.Ordinal);
        // 对话框不就地认键、不自己清空关键词。
        Assert.DoesNotContain("Keys.F", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("_excludedKeyword.Clear()", CodeLines(source), StringComparison.Ordinal);
    }

    [Fact]
    public void Gauges_AreSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ExclusionEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheExclusionKeywordBox()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("未纳入清单", row, StringComparison.Ordinal);
        Assert.Contains("第八十六轮", row, StringComparison.Ordinal);
        Assert.Contains("ExclusionSearchPlaceholder", row, StringComparison.Ordinal);
        // 这一屏与另十三张的差别要写清：同一页两张清单、Ctrl+F 只交给写入清单那个框。
        Assert.Contains("Ctrl+F", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "同步预览未纳入清单的关键词框（§6）");
        Assert.Contains("第八十六轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改计划口径、不改计数行、不写任何数据。
        Assert.Contains("不改计划口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已接入筛选下拉", row, StringComparison.Ordinal);
    }

    // ---------- 助手 ----------

    private static string DesignRow(int section, string clause)
    {
        var lines = Repo.DesignSection(section).Split('\n');
        var row = lines.SingleOrDefault(l => l.StartsWith($"| {clause} |", StringComparison.Ordinal));
        Assert.True(row is not null, $"DESIGN.md §{section} 缺少「{clause}」这一行。");
        return row;
    }

    /// <summary>方法体切片：签名行到下一个「    }」为止（本文件只切两个方法，缩进固定四格）。</summary>
    private static string MethodSlice(string source, string signature)
    {
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {signature}。");
        var to = source.IndexOf("\n    }", from, StringComparison.Ordinal);
        Assert.True(to > from, $"{signature} 的方法体没有收尾。");
        return source[from..to];
    }

    private static string CodeLines(string source) => CodeScan.Text(source);
}
