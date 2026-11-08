using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 检查结果两张清单的列排序断言（DESIGN.md §6「列表列排序」，第九十七轮）：
/// 两个主页面的列表自第四十七轮起点击列头即按该列排序，而检查结果对话框的两张清单
/// （接口清单四列、模型清单五列）点击列头没有任何反应——这一屏是只读工作台，
/// 真实项目几十上百个接口与模型，「把阻断的那几条集中看」「按范围外影响排」只能逐行扫，
/// 属同壳不同行为。排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，
/// 「这一列按什么排」收进 <see cref="CheckResultSort"/> 单一口径（与单元格取值同源），
/// 界面接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）。
/// </summary>
public class CheckResultSortTests
{
    private static EndpointCheck Endpoint(string method, string path, string? name, string status, int blockers) => new()
    {
        Key = method + " " + path,
        Method = method,
        Path = path,
        RemoteName = name,
        Status = status,
        Blockers = Enumerable.Range(0, blockers).Select(i => "阻断 " + i).ToList(),
    };

    private static ModelCheck Model(string name, string mapping, string status, int usedBy, int impact) => new()
    {
        Name = name,
        MappingState = mapping,
        Status = status,
        UsedBy = Enumerable.Range(0, usedBy).Select(i => "GET /a" + i).ToList(),
        ImpactOutOfScope = Enumerable.Range(0, impact).Select(i => new ImpactEntry()).ToList(),
    };

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void EndpointKey_MirrorsTheFourCells()
    {
        var check = Endpoint("GET", "/orders/{id}", "订单详情", "blocked", 2);
        // 接口列：与首格同一表达式（方法 + 空格 + 路径）。
        Assert.Equal("GET /orders/{id}", CheckResultSort.EndpointKey(check, 0));
        // 名称列：远端名称；缺值交 null（ListSort 视为最小，与单元格的「—」同一取舍）。
        Assert.Equal("订单详情", CheckResultSort.EndpointKey(check, 1));
        Assert.Null(CheckResultSort.EndpointKey(Endpoint("GET", "/x", null, "new", 0), 1));
        // 状态列：与状态格同一份中文映射。
        Assert.Equal(TaskDisplay.CheckStatusText("blocked"), CheckResultSort.EndpointKey(check, 2));
        // 阻断列：数值（不是「—」那格文字）。
        Assert.Equal(2, CheckResultSort.EndpointKey(check, 3));
        Assert.Equal(0, CheckResultSort.EndpointKey(Endpoint("GET", "/x", null, "new", 0), 3));
    }

    [Fact]
    public void ModelKey_MirrorsTheFiveCells()
    {
        var model = Model("Order", "candidate", "changed", 3, 1);
        Assert.Equal("Order", CheckResultSort.ModelKey(model, 0));
        Assert.Equal(TaskDisplay.MappingStateText("candidate"), CheckResultSort.ModelKey(model, 1));
        Assert.Equal(TaskDisplay.ModelStatusText("changed"), CheckResultSort.ModelKey(model, 2));
        Assert.Equal(3, CheckResultSort.ModelKey(model, 3));
        Assert.Equal(1, CheckResultSort.ModelKey(model, 4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    public void EndpointKey_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(CheckResultSort.EndpointKey(Endpoint("GET", "/x", "n", "new", 0), column));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(9)]
    public void ModelKey_OutsideTheColumnsIsNull(int column)
    {
        Assert.Null(CheckResultSort.ModelKey(Model("M", "mapped", "unchanged", 0, 0), column));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByBlockersPutsTheBlockedFirstWhenDescending()
    {
        var rows = new[]
        {
            Endpoint("GET", "/a", "A", "new", 0),
            Endpoint("GET", "/b", "B", "blocked", 2),
            Endpoint("GET", "/c", "C", "structure", 1),
        };
        var ascending = ListSort.Order(rows, 3, false, CheckResultSort.EndpointKey);
        Assert.Equal(["/a", "/c", "/b"], ascending.Select(r => r.Path));
        var descending = ListSort.Order(rows, 3, true, CheckResultSort.EndpointKey);
        Assert.Equal(["/b", "/c", "/a"], descending.Select(r => r.Path));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            Endpoint("GET", "/a", "A", "new", 1),
            Endpoint("GET", "/b", "B", "new", 1),
            Endpoint("GET", "/c", "C", "new", 1),
        };
        Assert.Equal(["/a", "/b", "/c"], ListSort.Order(rows, 3, false, CheckResultSort.EndpointKey).Select(r => r.Path));
        Assert.Equal(["/a", "/b", "/c"], ListSort.Order(rows, 3, true, CheckResultSort.EndpointKey).Select(r => r.Path));
    }

    [Fact]
    public void Ordering_ByModelImpactPutsTheOutOfScopeOnesFirst()
    {
        var rows = new[] { Model("A", "mapped", "unchanged", 1, 0), Model("B", "candidate", "changed", 2, 4), Model("C", "new", "new", 0, 1) };
        Assert.Equal(["B", "C", "A"], ListSort.Order(rows, 4, true, CheckResultSort.ModelKey).Select(m => m.Name));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与两个主页面同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（接口身份、远端名称），故这里也只用这两格找行。
        var rows = new[]
        {
            Endpoint("GET", "/a", "订单列表", "new", 0),
            Endpoint("GET", "/b2", "订单详情", "blocked", 2),
            Endpoint("GET", "/b1", "订单草稿", "blocked", 1),
        };
        var filtered = ListFilter.Apply(rows, "订单", c => [c.Method + " " + c.Path, c.RemoteName]);
        Assert.Equal(3, filtered.Count);
        // 筛掉一行之后排序只在剩下的行里发生：/a 不在结果里，两个 /b 按阻断数倒序。
        var narrowed = ListFilter.Apply(rows, "blocked", c => [c.Method + " " + c.Path, c.RemoteName]);
        Assert.Empty(narrowed);
        var ordered = ListSort.Order(ListFilter.Apply(rows, "/b", c => [c.Method + " " + c.Path, c.RemoteName]), 3, true, CheckResultSort.EndpointKey);
        Assert.Equal(["/b2", "/b1"], ordered.Select(r => r.Path));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresBothListsThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        Assert.Matches(@"(?m)^\s*private int _endpointSortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*private int _modelSortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*_endpointTitles = _list\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        Assert.Matches(@"(?m)^\s*_modelTitles = _modelList\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        Assert.Matches(@"(?m)^\s*_list\.ColumnClick \+= \(_, e\) => SortEndpoints\(e\.Column\);", source);
        Assert.Matches(@"(?m)^\s*_modelList\.ColumnClick \+= \(_, e\) => SortModels\(e\.Column\);", source);
        // 方向翻转只在共享口径一处（对话框不自己写 !descending）。
        Assert.Matches(@"(?m)^\s*\(_endpointSortColumn, _endpointSortDescending\) = ListSort\.Toggle\(_endpointSortColumn, _endpointSortDescending, column\);", source);
        Assert.Matches(@"(?m)^\s*\(_modelSortColumn, _modelSortDescending\) = ListSort\.Toggle\(_modelSortColumn, _modelSortDescending, column\);", source);
        Assert.DoesNotContain("_endpointSortDescending = !", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelSortDescending = !", source, StringComparison.Ordinal);
        // 表头标记按原标题重算（不叠加、不就地拼 ▲/▼）。
        Assert.Matches(@"(?m)^\s*_list\.Columns\[i\]\.Text = ListSort\.Header\(_endpointTitles\[i\], i, _endpointSortColumn, _endpointSortDescending\);", source);
        Assert.Matches(@"(?m)^\s*_modelList\.Columns\[i\]\.Text = ListSort\.Header\(_modelTitles\[i\], i, _modelSortColumn, _modelSortDescending\);", source);
        Assert.DoesNotContain("▲", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▼", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_AppliesSortAfterFilterAndRefreshesHeadersAfterRebuild()
    {
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        var endpoints = Slice(source, "private void PopulateList(", "private void PopulateModels");
        // 筛选先于排序：ListFilter.Apply 在 ListSort.Order 之内（同一表达式），排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var endpoints = ListSort\.Order\($", endpoints);
        Assert.Contains("ListFilter.Apply(_result.Endpoints, keyword", endpoints, StringComparison.Ordinal);
        Assert.Contains("_endpointSortColumn, _endpointSortDescending, CheckResultSort.EndpointKey);", endpoints, StringComparison.Ordinal);
        // 表头标记在清单重建之后刷新（EndUpdate 之后），筛到零行那一支之前。
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);\s*\n\s*RefreshEndpointSortHeaders\(\);", endpoints);
        var models = Slice(source, "private void PopulateModels", "private void SortEndpoints");
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", models);
        Assert.Contains("_modelSortColumn, _modelSortDescending, CheckResultSort.ModelKey);", models, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*_modelList\.EndUpdate\(\);\s*\n\s*RefreshModelSortHeaders\(\);", models);
    }

    [Fact]
    public void SortArmsCoverEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        var dialog = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "CheckResultSort.cs");
        Assert.Equal(4, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(5, Regex.Matches(dialog, @"(?m)^\s*_modelList\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 4), Arms(shared, "EndpointKey"));
        Assert.Equal(Enumerable.Range(0, 5), Arms(shared, "ModelKey"));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("=> check.", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("=> model.", dialog, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(dialog, "CheckResultSort\\.(EndpointKey|ModelKey)").Count);
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByEndpointKeyAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按接口身份恢复（重建前记下、重建后找回），模型清单没有选中语义。
        var source = Repo.Source("Desktop", "Native", "CheckResultsDialog.cs");
        var endpoints = Slice(source, "private void PopulateList(", "private void PopulateModels");
        var remember = endpoints.IndexOf("var selectedKey = (Ui.SelectedItem(_list)?.Tag as EndpointCheck)?.Key;", StringComparison.Ordinal);
        var clear = endpoints.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        var restore = endpoints.IndexOf("Ui.SelectItem(target ?? _list.Items[0]);", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember && restore > clear, "选中要在清空之前记下、在重建之后经唯一入口恢复。");
        var models = Slice(source, "private void PopulateModels", "private void SortEndpoints");
        Assert.DoesNotContain("SelectItem", models, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\CheckResultSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheCheckResultSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("检查结果两张清单的列排序", section);
        Assert.Contains("`CheckResultSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("检查结果两张清单的列排序（§6）", ledger);
        Assert.Contains("CheckResultSort", ledger);
    }

    private static List<int> Arms(string source, string method)
    {
        var start = source.IndexOf("IComparable? " + method + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"缺少 {method}。");
        var body = source[start..source.IndexOf("};", start, StringComparison.Ordinal)];
        return Regex.Matches(body, @"(?m)^\s*(\d+) => ").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
