using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 第七十八轮：整份数据不可得时关键词框停用（DESIGN.md §6「列表筛选」/§7「禁用 = 控件禁用 + 一句话说明缺什么」）。
/// 口径在 <see cref="ListFilter.KeywordEnabled"/> 一处，四张只有关键词框的清单（同步预览的接口勾选页、
/// 接口追踪明细、补充说明、生成文档核对）同经它；停用的框不接 Ctrl+F（<c>Ui.TryFocusFilter</c> 不吞键）。
/// 口径是真执行（ListFilter 与测试工程共享编译），接线以源码切片与行首正则钉住。
/// 真机上「没有数据时框是灰的、按 Ctrl+F 什么都不发生（按键不被吞掉）、有数据时框恢复可用」随 G-D5 复核
/// （Linux 无 WinForms GUI 运行时）。
/// </summary>
public class KeywordAvailabilityTests
{
    private static readonly string[] KeywordOnlyDialogs =
    [
        "SyncPreviewDialog.cs",
        "EndpointTraceDialog.cs",
        "SupplementsDialog.cs",
        "PropagationDialog.cs",
    ];

    // ---------- 口径真值表（框能不能用只有一份判定，四张清单认的就是它） ----------

    [Theory]
    [InlineData(true, false, true)]   // 有数据、没有作业在跑：可用
    [InlineData(true, true, false)]   // 有数据但作业在跑：清单正在被重建，停用
    [InlineData(false, false, false)] // 整份数据不可得：停用（下一步在提示出口那句话里）
    [InlineData(false, true, false)]
    public void KeywordEnabled_TruthTable(bool hasData, bool busy, bool expected)
    {
        Assert.Equal(expected, ListFilter.KeywordEnabled(hasData, busy));
    }

    [Fact]
    public void KeywordEnabled_DefaultsToNotBusy()
    {
        // 三张只读清单没有作业态，调用点只交「有没有数据」这一件事。
        Assert.Equal(ListFilter.KeywordEnabled(true, false), ListFilter.KeywordEnabled(true));
        Assert.Equal(ListFilter.KeywordEnabled(false, false), ListFilter.KeywordEnabled(false));
    }

    // ---------- 停用的框不接 Ctrl+F（不吞键） ----------

    [Fact]
    public void TryFocusFilter_DoesNotSwallowTheKeyWhenTheBoxIsDisabled()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "Ui.cs"), "public static bool TryFocusFilter");
        // 判定要在 Focus 之前：焦点交不过去（Focus 对停用控件不生效），吞掉按键等于把「什么都没发生」说成做了什么。
        var guard = body.IndexOf("if (!keyword.Enabled) return false;", StringComparison.Ordinal);
        var focus = body.IndexOf("keyword.Focus();", StringComparison.Ordinal);
        Assert.True(guard >= 0, "停用的关键词框不接这一次按键（返回 false，交回基类）。");
        Assert.True(focus > guard, "判定要排在 Focus 之前。");
        // 只加这一条守卫：手势判定仍取 ShellShortcuts 单一口径，不就地认键。
        Assert.Contains("ShellShortcuts.IsFindGesture(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.F", body, StringComparison.Ordinal);
    }

    // ---------- 四张只有关键词框的清单同经一处口径 ----------

    [Fact]
    public void TheFourKeywordOnlyLists_AllUseTheSharedRule()
    {
        foreach (var file in KeywordOnlyDialogs)
        {
            var source = Repo.Source("Desktop", "Native", file);
            // 停用与恢复都经口径，界面不就地写 true/false 字面。
            Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_keyword.Enabled = false;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_keyword.Enabled = true;", source, StringComparison.Ordinal);
        }
        // 三张只读清单没有作业态，只交「有没有数据」；同步预览那一页另有作业态，交两件事。
        foreach (var file in new[] { "EndpointTraceDialog.cs", "SupplementsDialog.cs", "PropagationDialog.cs" })
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(false);", source, StringComparison.Ordinal);
            Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(true);", source, StringComparison.Ordinal);
        }
        Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(_diff is not null, busy);",
            Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheThreeReadOnlyLists_DisableTheBoxInTheirDataUnavailableExit()
    {
        // 三张只读清单的「整份数据不可得」出口：提示出口（ShowHint）里停用——缺什么与下一步由那句话给出。
        foreach (var file in new[] { "EndpointTraceDialog.cs", "SupplementsDialog.cs", "PropagationDialog.cs" })
        {
            var body = MethodSlice(Repo.Source("Desktop", "Native", file), "private void ShowHint");
            Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(false);", body, StringComparison.Ordinal);
            // 那句话仍在（§7：禁用不得移除下一步——下一步就在提示出口里）。
            Assert.Contains("_hint.Text = message;", body, StringComparison.Ordinal);
        }
        // 生成文档核对另有「整份数据没有条目」这一支（不走 ShowHint），同样停用。
        var propagation = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        var empty = propagation.IndexOf("没有核对条目。", StringComparison.Ordinal);
        Assert.True(empty >= 0, "整份数据没有条目那一支要留着。");
        Assert.Contains("_keyword.Enabled = ListFilter.KeywordEnabled(false);", propagation[empty..], StringComparison.Ordinal);
    }

    [Fact]
    public void TheThreeReadOnlyLists_RestoreTheBoxWhenDataIsThere()
    {
        // 有数据可筛时恢复可用：停用只属于「整份数据不可得」那一屏，不粘在框上。
        foreach (var file in new[] { "EndpointTraceDialog.cs", "SupplementsDialog.cs", "PropagationDialog.cs" })
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.Single(Regex.Matches(source, @"(?m)^\s*_keyword\.Enabled = ListFilter\.KeywordEnabled\(true\);$").Cast<Match>());
        }
    }

    [Fact]
    public void FilteredEmptyBranch_StillLeavesTheBoxUsable()
    {
        // 两种空不能混：筛到零行是「有数据、只是关键词不命中」，下一步就在那个框上（§7「禁用不得移除下一步」）。
        foreach (var file in new[] { "EndpointTraceDialog.cs", "PropagationDialog.cs" })
        {
            var body = MethodSlice(Repo.Source("Desktop", "Native", file), "private void PopulateList");
            var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
            Assert.True(branch >= 0, "筛到零行要经共享判定。");
            Assert.DoesNotContain("_keyword.Enabled", body[branch..], StringComparison.Ordinal);
        }
        var supplements = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        var exit = MethodSlice(supplements, "private void ShowFilteredEmpty");
        Assert.DoesNotContain("_keyword.Enabled", exit, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFiveDropdownLists_AreNotInThisRound()
    {
        // 五张带筛选下拉的清单是「下拉 + 框」一对一起启停（形状不同），自第五十九轮起就是这条口径，本轮不动。
        foreach (var file in new[] { "ScanResultsDialog.cs", "CandidatesDialog.cs", "ContractsDialog.cs", "DecisionsDialog.cs" })
        {
            var source = Repo.Source("Desktop", "Native", file);
            Assert.DoesNotContain("ListFilter.KeywordEnabled", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SharedCompilation_IncludesTheRule()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 文档：条款与台账 ----------

    [Fact]
    public void DesignDoc_RecordsTheRule()
    {
        var design = Repo.Source("docs", "DESIGN.md");
        Assert.Contains("ListFilter.KeywordEnabled", design, StringComparison.Ordinal);
        Assert.Contains("整份数据不可得时关键词框停用", design, StringComparison.Ordinal);
        // 台账那一行指向本轮，且与「筛到零行留着」不混为一谈。
        Assert.Contains("第七十八轮", design, StringComparison.Ordinal);
    }

    // ---------- 工具 ----------

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
