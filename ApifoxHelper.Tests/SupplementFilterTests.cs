using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 补充说明的接口清单关键词框（第七十六轮，DESIGN.md §6「列表筛选」）：这一屏的接口清单是整份检查结果的
/// 接口（真实项目几十上百个，方案 §4.2 记录千级接口分支），要找到「上次那个接口」只能逐行扫；
/// 另七张长清单在第六十五至七十五轮都有关键词框，同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 这一屏与那七张有一处不同：**编辑位挂在选中上**，筛选要重建清单，重建会换选中——而换选中按既有的
/// 「切换接口丢弃未保存输入」口径会重建编辑位。故本轮的取舍是：同一个接口仍在筛选结果里时编辑位原样留着
/// （未保存的输入不因敲了一个字就丢，与第七十二轮「勾选不因筛选而丢」同一取舍），被筛掉时按既有口径重建
/// 并在状态行如实说明这一次丢弃（不静默）。
/// 口径是真执行（<see cref="SupplementEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 接线以源码切片与行首正则钉住；真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class SupplementFilterTests
{
    // ---------- 口径真执行：匹配的是那一行在屏幕上被读作的那两格 ----------

    [Fact]
    public void KeywordFields_CarryTheEndpointIdentityAndTheRemoteName()
    {
        var check = new EndpointCheck { Key = "GET /api/orders", Method = "GET", Path = "/api/orders", RemoteName = "订单列表" };
        var fields = SupplementEntries.KeywordFields(check);
        // 与清单首两列同一份拼法：接口身份（方法 + 路径）与远端名称。
        Assert.Equal(["GET /api/orders", "订单列表"], fields);
        // 远端没有这个接口时名称列是空串（单元格同样显示空），不抛异常、不误命中。
        var sourceOnly = new EndpointCheck { Key = "POST /api/ping", Method = "POST", Path = "/api/ping", RemoteName = null };
        Assert.Equal(["POST /api/ping", ""], SupplementEntries.KeywordFields(sourceOnly));
        // 「补充」那一列是数字（「N 处」），不进关键词（与接口追踪明细的三个计数列同一取舍）。
        Assert.DoesNotContain(fields, f => f.Contains("处", StringComparison.Ordinal));
    }

    [Fact]
    public void Keyword_FindsTheEndpointByPathMethodOrName()
    {
        var orders = SupplementEntries.KeywordFields(new EndpointCheck { Method = "GET", Path = "/api/orders", RemoteName = "订单列表" });
        var ping = SupplementEntries.KeywordFields(new EndpointCheck { Method = "POST", Path = "/api/ping", RemoteName = null });
        // 路径、方法、名称都能找到那一行（忽略大小写，与另七张长清单同一份匹配规则）。
        Assert.True(ListFilter.Matches("orders", orders));
        Assert.True(ListFilter.Matches("get", orders));
        Assert.True(ListFilter.Matches("订单", orders));
        Assert.False(ListFilter.Matches("orders", ping));
        // 词间「且」：两个词分别落在不同字段里也算命中。
        Assert.True(ListFilter.Matches("get 订单", orders));
        Assert.False(ListFilter.Matches("get 用户", orders));
        // 空关键词命中全部行。
        Assert.True(ListFilter.Matches("", ping));
        Assert.True(ListFilter.Matches(null, ping));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndFiltersOnlyTheVisibleRows()
    {
        var rows = new[]
        {
            new EndpointCheck { Key = "GET /api/orders", Method = "GET", Path = "/api/orders", RemoteName = "订单列表" },
            new EndpointCheck { Key = "POST /api/ping", Method = "POST", Path = "/api/ping" },
            new EndpointCheck { Key = "GET /api/orders/{id}", Method = "GET", Path = "/api/orders/{id}", RemoteName = "订单详情" },
        };
        var all = ListFilter.Apply(rows, "", SupplementEntries.KeywordFields);
        Assert.Equal(rows, all);
        var orders = ListFilter.Apply(rows, "orders", SupplementEntries.KeywordFields);
        Assert.Equal(["GET /api/orders", "GET /api/orders/{id}"], orders.Select(r => r.Key));
        var none = ListFilter.Apply(rows, "不存在的接口", SupplementEntries.KeywordFields);
        Assert.Empty(none);
        // 筛到零行是「有条目、只是不命中」，不是「检查结果里没有接口」——两者的下一步不同。
        Assert.True(ListFilter.IsFilteredEmpty(rows.Length, none.Count));
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
    }

    [Fact]
    public void FilteredEmptySentence_PointsAtTheKeywordBoxOnly()
    {
        // 这一屏没有筛选下拉：说「换一个筛选」就是指向界面上按不动的控件。
        Assert.Equal("这个关键词下没有接口。换一个关键词再试。", ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun));
        // 占位文字与检查结果共用一份：两处匹配的正是同样两格（接口身份 + 名称）。
        Assert.Equal("筛选接口（方法、路径、名称）", ListFilter.EndpointSearchPlaceholder);
    }

    // ---------- 接线：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_TakesTheKeywordBoxFromTheFactories()
    {
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        // 关键词框经工厂出厂（§6「输入框」），筛选行经工厂组装（§6「列表筛选」），界面不各拼一遍。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.EndpointSearchPlaceholder;$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => PopulateList\(\);$", source);
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, counts: null\);$", source);
        Assert.Single(Regex.Matches(source, @"Ui\.FilterRow\(").Cast<Match>());
        // 界面不自己拼这一行、不自己量宽度、不就地拼占位文字。
        Assert.DoesNotContain("filterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选接口（方法、路径、名称）", source, StringComparison.Ordinal);
        // 筛选行排在接口清单之前加入（停靠按集合倒序生效：标题行在最上、筛选行其次、清单填满剩余空间）。
        var listAt = source.IndexOf("sidePanel.Controls.Add(_list);", StringComparison.Ordinal);
        var filterAt = source.IndexOf("sidePanel.Controls.Add(filterRow);", StringComparison.Ordinal);
        var captionAt = source.IndexOf("sidePanel.Controls.Add(sideCaption);", StringComparison.Ordinal);
        Assert.True(listAt >= 0 && filterAt > listAt && captionAt > filterAt, "筛选行要排在接口清单之后、标题行之前加入。");
    }

    [Fact]
    public void FilteredEmptyBranch_KeepsTheKeywordBoxAndTheAggregateCounts()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "SupplementsDialog.cs"), "private void PopulateList()");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(branch >= 0, "筛到零行要经共享判定，不就地写「行数 == 0」。");
        var tail = body[branch..];
        // 分支只把这一屏交给自己的出口（那句话与收控件都在出口里，不在这里另写一套）。
        Assert.Contains("ShowFilteredEmpty();", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Enabled = false", tail, StringComparison.Ordinal);
        // 计数行按整份检查结果统计，不随关键词变化：分支里不抹它（「共 N 个接口 · 已保存补充 M 处」）。
        Assert.DoesNotContain("_counts.Text", tail, StringComparison.Ordinal);
        // 分支先返回、回显在后：反过来那句话刚显示就被自己抹掉。
        var restore = tail.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(restore > tail.IndexOf("ShowFilteredEmpty();", StringComparison.Ordinal), "筛到零行的分支要排在列表回显之前。");
        // 编辑位收走、保存与放弃一起禁用——没有选中就没有可保存的那一个接口。
        var exit = MethodSlice(Repo.Source("Desktop", "Native", "SupplementsDialog.cs"), "private void ShowFilteredEmpty()");
        // 那句话与下一步：只指那个框（这一屏没有筛选下拉），关键词框留着（§7「禁用不得移除下一步」）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun)", exit, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Enabled = false", exit, StringComparison.Ordinal);
        Assert.DoesNotContain("_counts.Text", exit, StringComparison.Ordinal);
        Assert.Contains("_editorsHost.Visible = false;", exit, StringComparison.Ordinal);
        Assert.Contains("_saveButton.Enabled = false;", exit, StringComparison.Ordinal);
        Assert.Contains("_discardButton.Enabled = false;", exit, StringComparison.Ordinal);
        Assert.Contains("UpdateCounts();", exit, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_IsRestoredByEndpointKeyAndUnsavedInputSurvivesTheSameEndpoint()
    {
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        var body = MethodSlice(source, "private void PopulateList()");
        // 先记下当前接口身份与脏标记，再清空清单（顺序反过来记到的就是空）。
        var remember = body.IndexOf("var previousKey = Current()?.Key;", StringComparison.Ordinal);
        var clear = body.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "接口身份要在清空清单之前记下。");
        // 重建期间选中事件不重建编辑位（清空与回填都会触发它，那一次不是用户切换接口）。
        Assert.Matches(@"(?m)^\s*private bool _rebuilding;$", source);
        Assert.Matches(@"(?m)^\s*if \(_rebuilding\) return;$", source);
        // 恢复不到时落在新筛选第一条（与第五十六轮「切换筛选落在新筛选第一条」同一口径），选中仍经唯一入口。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);$", body);
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        // 同一个接口仍在筛选结果里：编辑位原样留着（不调 ReloadEditors），只把计数行跟上。
        var same = body.IndexOf("if (target is not null)", StringComparison.Ordinal);
        Assert.True(same >= 0, "同一个接口仍在筛选结果里时要单独一支，不能一律重建编辑位。");
        var sameBranch = body[same..body.IndexOf("ReloadEditors();", same, StringComparison.Ordinal)];
        Assert.Contains("UpdateCounts();", sameBranch, StringComparison.Ordinal);
        Assert.Contains("return;", sameBranch, StringComparison.Ordinal);
        // 被筛掉时按既有口径重建编辑位，并在状态行如实说明这一次丢弃（不静默）。
        Assert.Matches(@"(?m)^\s*if \(wasDirty\) SetStatus\(""已切换到另一个接口，上一个接口的未保存输入已丢弃。"", UiTokens\.WarnText\);$", body);
    }

    [Fact]
    public void Dialog_DoesNotRecognizeTheGesturesItself()
    {
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        var body = MethodSlice(source, "protected override bool ProcessDialogKey(Keys keyData)");
        // 三个手势只经共享入口（另七张长清单同一处接线），对话框不就地认键、不自己清空关键词。
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryFocusFilter\(keyData, _keyword\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, _list\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _keyword\)\) return true;$", body);
        Assert.DoesNotContain("Keys.F", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Clear()", CodeLines(source), StringComparison.Ordinal);
    }

    [Fact]
    public void Gauges_AreSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\SupplementEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSupplementKeywordBox()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("补充说明", row, StringComparison.Ordinal);
        Assert.Contains("第七十六轮", row, StringComparison.Ordinal);
        Assert.Contains("EndpointSearchPlaceholder", row, StringComparison.Ordinal);
        // 这一屏与另七张的差别要写清：编辑位挂在选中上，未保存的输入不因筛选而丢。
        Assert.Contains("未保存的输入", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "补充说明的接口清单关键词框（§6）");
        Assert.Contains("第七十六轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改保存切片、不改校验、不写任务数据。
        Assert.Contains("不改保存切片", row, StringComparison.Ordinal);
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

    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
