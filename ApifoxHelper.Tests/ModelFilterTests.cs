using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 检查结果的数据模型清单关键词框（第八十五轮，DESIGN.md §6「列表筛选」）：同一个对话框里接口清单
/// 自第六十五轮起就有关键词框、模型清单没有——真实项目的模型清单几十上百个，要找到「还没绑定的那个模型」
/// 「有范围外影响的模型」只能逐行扫，属同壳不同行为。这一屏没有筛选下拉（模型清单没有状态筛选），
/// 故走只有关键词框的那一支；匹配的是那一行在屏幕上被读作的那三格（模型名、映射、状态），
/// 「用于接口」「范围外影响」是数字、不进关键词。
/// 口径是真执行（<see cref="ModelEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 接线以源码切片与行首正则钉住；真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ModelFilterTests
{
    // ---------- 口径真执行：匹配的是那一行在屏幕上被读作的那三格 ----------

    [Fact]
    public void KeywordFields_CarryTheNameTheMappingAndTheStatus()
    {
        var model = new ModelCheck { Name = "OrderDto", MappingState = "candidate", Status = "changed" };
        var fields = ModelEntries.KeywordFields(model);
        // 三格：模型名、映射文字、状态文字——与清单前三列同一批文字（映射与状态取 TaskDisplay 同一份映射）。
        Assert.Equal(3, fields.Length);
        Assert.Equal("OrderDto", fields[0]);
        Assert.Equal(TaskDisplay.MappingStateText("candidate"), fields[1]);
        Assert.Equal(TaskDisplay.ModelStatusText("changed"), fields[2]);
        // 数字列不进关键词：用于接口与范围外影响是计数，敲数字不该筛出模型。
        Assert.DoesNotContain(fields, field => field is not null && field.Contains('3'));
    }

    [Fact]
    public void Keyword_FindsTheModelByAnyOfTheThreeCells()
    {
        var model = new ModelCheck { Name = "OrderDto", MappingState = "candidate", Status = "changed" };
        var fields = ModelEntries.KeywordFields(model);
        // 模型名按片段找、忽略大小写（与另十二张长清单同一份匹配规则）。
        Assert.True(ListFilter.Matches("order", fields));
        // 映射与状态文字进关键词：这一屏没有筛选下拉，不进关键词就等于筛不到。
        Assert.True(ListFilter.Matches(TaskDisplay.MappingStateText("candidate"), fields));
        Assert.True(ListFilter.Matches(TaskDisplay.ModelStatusText("changed"), fields));
        Assert.False(ListFilter.Matches("不存在的模型", fields));
        // 空关键词命中全部行。
        Assert.True(ListFilter.Matches("", fields));
        Assert.True(ListFilter.Matches(null, fields));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndFiltersOnlyTheVisibleRows()
    {
        var models = new List<ModelCheck>
        {
            new() { Name = "OrderDto", MappingState = "mapped", Status = "unchanged" },
            new() { Name = "UserDto", MappingState = "candidate", Status = "new" },
            new() { Name = "OrderItemDto", MappingState = "mapped", Status = "changed" },
        };
        var all = ListFilter.Apply(models, "", ModelEntries.KeywordFields);
        Assert.Equal(models, all);
        var orders = ListFilter.Apply(models, "order", ModelEntries.KeywordFields);
        Assert.Equal(["OrderDto", "OrderItemDto"], orders.Select(m => m.Name));
        var none = ListFilter.Apply(models, "不存在的模型", ModelEntries.KeywordFields);
        Assert.Empty(none);
        // 筛到零行是「有模型、只是不命中」，不是「没有模型」——两者的下一步不同。
        Assert.True(ListFilter.IsFilteredEmpty(models.Count, none.Count));
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
    }

    [Fact]
    public void FilteredEmptySentence_PointsAtTheKeywordBoxOnly()
    {
        // 这一屏没有筛选下拉：说「换一个筛选」就是指向界面上按不动的控件。
        Assert.Equal("这个关键词下没有模型。换一个关键词再试。", ListFilter.FilteredEmptyKeywordHint(ListFilter.ModelNoun));
        Assert.Equal("筛选模型（名称、映射、状态）", ListFilter.ModelSearchPlaceholder);
        // 与接口清单的名词分开：同一个对话框里两张清单清的不是同一样东西。
        Assert.NotEqual(ListFilter.EndpointNoun, ListFilter.ModelNoun);
    }

    // ---------- 接线：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_TakesTheSecondKeywordBoxFromTheFactories()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        // 关键词框经工厂出厂（§6「输入框」），筛选行经工厂组装（§6「列表筛选」），界面不各拼一遍。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _modelKeyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_modelKeyword\.PlaceholderText = ListFilter\.ModelSearchPlaceholder;$", source);
        Assert.Matches(@"(?m)^\s*_modelKeyword\.TextChanged \+= \(_, _\) => PopulateModels\(\);$", source);
        Assert.Matches(@"(?m)^\s*var modelsFilterRow = Ui\.FilterRow\(_modelKeyword, counts: null\);$", source);
        // 界面不自己拼这一行、不自己量宽度、不就地拼占位文字。
        Assert.DoesNotContain("modelsFilterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelKeyword.Width", source, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选模型（名称、映射、状态）", source, StringComparison.Ordinal);
        // 筛选行排在标题行之后加入（停靠按集合倒序生效，标题行在最上、筛选行在其下、清单填满剩余）。
        var filterAt = source.IndexOf("_modelsHost.Controls.Add(modelsFilterRow);", StringComparison.Ordinal);
        var captionAt = source.IndexOf("_modelsHost.Controls.Add(modelsCaption);", StringComparison.Ordinal);
        Assert.True(filterAt >= 0 && captionAt > filterAt, "筛选行要排在标题行之前加入（倒序停靠后标题行在最上）。");
    }

    [Fact]
    public void FilteredEmptyBranch_KeepsTheBoxAndSaysTheNextStep()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "CheckResultsDialog.cs"), "private void PopulateModels");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(branch >= 0, "筛到零行要经共享判定，不就地写「行数 == 0」。");
        var tail = body[branch..];
        // 那句话与下一步：只指那个框（这一屏没有筛选下拉），关键词框留着（§7「禁用不得移除下一步」）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.ModelNoun)", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelKeyword.Enabled", tail, StringComparison.Ordinal);
        // 清单藏起来、提示显示出来；分支先返回、回显在后。
        Assert.Contains("_modelList.Visible = false;", tail, StringComparison.Ordinal);
        Assert.Contains("_modelsHint.Visible = true;", tail, StringComparison.Ordinal);
        Assert.True(tail.IndexOf("_modelsHint.Visible = false;", StringComparison.Ordinal) > tail.IndexOf("return;", StringComparison.Ordinal),
            "筛到零行的分支要排在清单回显之前。");
        // 模型清单没有选中语义：不恢复选中、不碰键盘焦点、不自己滚动。
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_AreStillComputedOnceFromTheWholeResult()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        // 计数行（含「模型 N」）只在 OnLoad 里按整份检查结果算一次：放进重建里就等于随关键词变化。
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_counts\.Text = string\.Join\(").Cast<Match>());
        var populate = MethodSlice(source, "private void PopulateModels");
        Assert.DoesNotContain("_counts.Text", populate, StringComparison.Ordinal);
    }

    [Fact]
    public void BoxIsDisabledWhenTheWholeDataIsUnavailable()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        // 只有关键词框的那一支：停用与恢复都经共享口径，界面不就地写 true/false 字面。
        Assert.Contains("_modelKeyword.Enabled = ListFilter.KeywordEnabled(false);", source, StringComparison.Ordinal);
        Assert.Contains("_modelKeyword.Enabled = ListFilter.KeywordEnabled(result.Models.Count > 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelKeyword.Enabled = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelKeyword.Enabled = true;", source, StringComparison.Ordinal);
        // 两个「整份数据不可得」出口（读取失败、尚未检查）都要停：框里敲字筛不到任何东西。
        Assert.Equal(2, Regex.Matches(source, @"(?m)^\s*_modelKeyword\.Enabled = ListFilter\.KeywordEnabled\(false\);$").Count);
    }

    [Fact]
    public void Dialog_DoesNotRecognizeTheGesturesItself()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        var body = MethodSlice(source, "protected override bool ProcessDialogKey(Keys keyData)");
        // 两个框各接 Enter 与 Esc（判定看焦点，一次按键只有一个含义），Ctrl+F 只交给本屏主清单的那个框。
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _modelKeyword, _modelList\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _modelKeyword\)\) return true;$", body);
        Assert.Single(Regex.Matches(body, @"Ui\.TryFocusFilter\(").Cast<Match>());
        Assert.DoesNotContain("TryFocusFilter(keyData, _modelKeyword)", body, StringComparison.Ordinal);
        // 对话框不就地认键、不自己清空关键词。
        Assert.DoesNotContain("Keys.F", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("_modelKeyword.Clear()", CodeLines(source), StringComparison.Ordinal);
    }

    [Fact]
    public void Gauges_AreSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ModelEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheModelKeywordBox()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("模型清单", row, StringComparison.Ordinal);
        Assert.Contains("第八十五轮", row, StringComparison.Ordinal);
        Assert.Contains("ModelSearchPlaceholder", row, StringComparison.Ordinal);
        // 这一屏与另十二张的差别要写清：同一个对话框里两张清单、Ctrl+F 只交给主清单那个框。
        Assert.Contains("Ctrl+F", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "检查结果模型清单的关键词框（§6）");
        Assert.Contains("第八十五轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改检查口径、不改计数行、不写任何数据。
        Assert.Contains("不改检查口径", row, StringComparison.Ordinal);
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
