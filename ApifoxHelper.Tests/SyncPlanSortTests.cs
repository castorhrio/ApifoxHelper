using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览两张清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零六轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）、任务日志的记录清单（第一百零四轮）
/// 与注释回写的文件清单（第一百零五轮）点击列头即按该列排序，而同步预览预览页的两张清单
/// （写入清单七列、未纳入清单两列）点击列头没有任何反应——这一页是确认同步之前的最后一屏，
/// 「把写入失败的那几条集中看」「按资源排」「按原因聚在一起看」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进
/// <see cref="SyncPlanSort"/> 单一口径（与单元格取值同源），界面接线由行首正则与源码切片
/// 机械检查钉住（被注释掉也算删除）。
/// </summary>
public class SyncPlanSortTests
{
    /// <summary>写入清单一行在屏幕上被读作的那七格（与对话框建行处同一份取值）。</summary>
    private sealed record ActionRow(int Step, string Kind, string Resource, long? RemoteId, string Changes, string? Validation, string State);

    /// <summary>未纳入清单一行在屏幕上被读作的那两格。</summary>
    private sealed record ExclusionRow(string Resource, string Reason);

    private static IComparable? Key(ActionRow row, int column) =>
        SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, column);

    private static IComparable? Key(ExclusionRow row, int column) =>
        SyncPlanSort.ExclusionKey(row.Resource, row.Reason, column);

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void ActionKey_MirrorsTheSevenCells()
    {
        var row = new ActionRow(3, "更新接口", "GET /orders/{id}", 42, "说明：旧 → 新", "通过", "已写入并核对");
        // 序号列：执行顺序里的第几步（与首格同一份，数值）。
        Assert.Equal(3, SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, 0));
        // 动作列：与第二格同一份中文映射（TaskDisplay.PlanActionKindText）。
        Assert.Equal("更新接口", SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, 1));
        // 资源列：资源键原文。
        Assert.Equal("GET /orders/{id}", SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, 2));
        // 远端 ID 列：ID 本身（数值），不是那一格的文字。
        Assert.Equal(42L, SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, 3));
        // 改动列：与第五格同一份（PlanEntries.ChangesText）。
        Assert.Equal("说明：旧 → 新", SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, 4));
        // CLI 校验列：校验结果原文（与那一格同一份原始值）。
        Assert.Equal("通过", SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, 5));
        // 状态列：与第七格同一份中文映射（TaskDisplay.PlanActionStateText）。
        Assert.Equal("已写入并核对", SyncPlanSort.ActionKey(row.Step, row.Kind, row.Resource, row.RemoteId, row.Changes, row.Validation, row.State, 6));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(9)]
    public void ActionKey_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(SyncPlanSort.ActionKey(1, "新建模型", "model:Order", null, "—", null, "待执行", column));
    }

    [Fact]
    public void ActionKey_TreatsThePlaceholdersAsMissingValues()
    {
        // 新建动作还没有远端 ID、还没有校验结果：两格显示的是「—」，那是占位不是值——
        // 排序键交 null（视为最小，升序排在最前），不按那个字的码位排。
        Assert.Null(SyncPlanSort.ActionKey(1, "新建模型", "model:Order", null, "—", null, "待执行", 3));
        Assert.Null(SyncPlanSort.ActionKey(1, "新建模型", "model:Order", null, "—", "  ", "待执行", 5));
        // 有值时仍是原文（与那一格同一份）。
        Assert.Equal("失败：schema 校验未通过", SyncPlanSort.ActionKey(2, "更新模型", "model:Order", 7, "—", "失败：schema 校验未通过", "写入失败", 5));
    }

    [Fact]
    public void ExclusionKey_MirrorsTheTwoCells()
    {
        var row = new ExclusionRow("GET /users/{id}", "无需修改");
        Assert.Equal("GET /users/{id}", SyncPlanSort.ExclusionKey(row.Resource, row.Reason, 0));
        Assert.Equal("无需修改", SyncPlanSort.ExclusionKey(row.Resource, row.Reason, 1));
        Assert.Null(SyncPlanSort.ExclusionKey(row.Resource, row.Reason, -1));
        Assert.Null(SyncPlanSort.ExclusionKey(row.Resource, row.Reason, 2));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByStepRestoresTheExecutionOrder()
    {
        // 序号列按数值排：升序即回到执行顺序（模型在前、接口在后），排序之后仍能按序号核对执行日志。
        var rows = new[]
        {
            new ActionRow(3, "新建接口", "GET /c", null, "—", null, "待执行"),
            new ActionRow(1, "新建模型", "model:A", null, "—", null, "待执行"),
            new ActionRow(2, "更新接口", "GET /b", 9, "说明：旧 → 新", "通过", "待执行"),
        };
        Assert.Equal([1, 2, 3], ListSort.Order(rows, 0, false, Key).Select(r => r.Step));
        Assert.Equal([3, 2, 1], ListSort.Order(rows, 0, true, Key).Select(r => r.Step));
    }

    [Fact]
    public void Ordering_ByKindGroupsTheSameKindsTogether()
    {
        // 动作列按那一格的文字排：同一动作的行聚在一起（「把新建与更新分开」）。
        var rows = new[]
        {
            new ActionRow(1, "新建模型", "model:A", null, "—", null, "待执行"),
            new ActionRow(2, "更新接口", "GET /b", 9, "—", "通过", "待执行"),
            new ActionRow(3, "新建模型", "model:C", null, "—", null, "待执行"),
            new ActionRow(4, "更新接口", "GET /d", 11, "—", "通过", "待执行"),
        };
        var expected = rows.OrderBy(r => r.Kind, StringComparer.CurrentCulture).Select(r => r.Step).ToList();
        Assert.Equal(expected, ListSort.Order(rows, 1, false, Key).Select(r => r.Step));
        // 反向：同值行仍按数据源顺序（两个方向都稳定）。
        Assert.Equal(
            rows.OrderByDescending(r => r.Kind, StringComparer.CurrentCulture).Select(r => r.Step),
            ListSort.Order(rows, 1, true, Key).Select(r => r.Step));
    }

    [Fact]
    public void Ordering_ByRemoteIdUsesNumericOrderAndPutsNewActionsFirst()
    {
        // 远端 ID 列按数值排（按文字排会把「10」排到「2」前）；新建动作还没有 ID，交 null 排在最前。
        var rows = new[]
        {
            new ActionRow(1, "更新接口", "GET /a", 10, "—", "通过", "待执行"),
            new ActionRow(2, "新建接口", "GET /b", null, "—", null, "待执行"),
            new ActionRow(3, "更新接口", "GET /c", 2, "—", "通过", "待执行"),
        };
        Assert.Equal([2, 3, 1], ListSort.Order(rows, 3, false, Key).Select(r => r.Step));
        Assert.Equal([1, 3, 2], ListSort.Order(rows, 3, true, Key).Select(r => r.Step));
    }

    [Fact]
    public void Ordering_ByValidationPutsTheNotYetValidatedRowsFirst()
    {
        // CLI 校验列：还没有校验结果的行交 null，升序排在最前（「还没校验的那几条」正是确认同步前要看的）。
        var rows = new[]
        {
            new ActionRow(1, "更新接口", "GET /a", 10, "—", "通过", "待执行"),
            new ActionRow(2, "新建接口", "GET /b", null, "—", null, "待执行"),
            new ActionRow(3, "更新接口", "GET /c", 2, "—", "失败：schema 校验未通过", "写入失败"),
        };
        var ordered = ListSort.Order(rows, 5, false, Key).Select(r => r.Step).ToList();
        Assert.Equal(2, ordered[0]);
        // 升序与降序互为镜像（同值行稳定，故直接反转即可）。
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 5, true, Key).Select(r => r.Step));
    }

    [Fact]
    public void Ordering_ByStateGroupsTheSameStatesTogether()
    {
        // 状态列按那一格的文字排：同一状态的行聚在一起（「把写入失败的那几条集中看」）。
        var rows = new[]
        {
            new ActionRow(1, "更新接口", "GET /a", 10, "—", "通过", "已写入并核对"),
            new ActionRow(2, "更新接口", "GET /b", 11, "—", "通过", "写入失败"),
            new ActionRow(3, "更新接口", "GET /c", 12, "—", "通过", "已写入并核对"),
            new ActionRow(4, "更新接口", "GET /d", 13, "—", "通过", "写入失败"),
        };
        var expected = rows.OrderBy(r => r.State, StringComparer.CurrentCulture).Select(r => r.Step).ToList();
        Assert.Equal(expected, ListSort.Order(rows, 6, false, Key).Select(r => r.Step));
        Assert.Equal(
            rows.OrderByDescending(r => r.State, StringComparer.CurrentCulture).Select(r => r.Step),
            ListSort.Order(rows, 6, true, Key).Select(r => r.Step));
    }

    [Fact]
    public void Ordering_ByResourceUsesTheNaturalOrder()
    {
        // 资源列常带序号（同一目录下第 2 个与第 10 个接口）：数字段按数值排，与资源管理器名称列同序。
        var rows = new[]
        {
            new ActionRow(1, "更新接口", "GET /orders/10", 10, "—", "通过", "待执行"),
            new ActionRow(2, "更新接口", "GET /orders/2", 2, "—", "通过", "待执行"),
        };
        Assert.Equal([2, 1], ListSort.Order(rows, 2, false, Key).Select(r => r.Step));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            new ActionRow(1, "更新接口", "GET /a", 10, "—", "通过", "待执行"),
            new ActionRow(2, "更新接口", "GET /b", 11, "—", "通过", "待执行"),
            new ActionRow(3, "更新接口", "GET /c", 12, "—", "通过", "待执行"),
        };
        Assert.Equal([1, 2, 3], ListSort.Order(rows, 6, false, Key).Select(r => r.Step));
        Assert.Equal([1, 2, 3], ListSort.Order(rows, 6, true, Key).Select(r => r.Step));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与另十处同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（动作、资源、改动、状态四格）。
        var rows = new[]
        {
            new ActionRow(1, "更新接口", "GET /orders/{id}", 10, "说明：旧 → 新", "通过", "待执行"),
            new ActionRow(2, "更新接口", "GET /users/{id}", 11, "说明：旧 → 新", "通过", "待执行"),
            new ActionRow(3, "新建接口", "GET /orders/export", null, "—", null, "待执行"),
        };
        var visible = ListFilter.Apply(rows, "orders", r => PlanEntries.KeywordFields(r.Kind, r.Resource, r.Changes, r.State));
        Assert.Equal([1, 3], visible.Select(r => r.Step));
        // 排序只在筛出来的行里发生：按远端 ID 升序，另一行不在结果里。
        Assert.Equal([3, 1], ListSort.Order(visible, 3, false, Key).Select(r => r.Step));
    }

    [Fact]
    public void ExclusionOrdering_ByReasonGroupsTheSameReasonsTogether()
    {
        // 未纳入清单按原因排：同一类原因（无需修改、远端已被修改…）聚在一起，这是这一屏的要害。
        var rows = new[]
        {
            new ExclusionRow("GET /a", "无需修改"),
            new ExclusionRow("GET /b", "检查之后远端已被修改"),
            new ExclusionRow("GET /c", "无需修改"),
            new ExclusionRow("GET /d", "检查之后远端已被修改"),
        };
        var expected = rows.OrderBy(r => r.Reason, StringComparer.CurrentCulture).Select(r => r.Resource).ToList();
        Assert.Equal(expected, ListSort.Order(rows, 1, false, Key).Select(r => r.Resource));
        Assert.Equal(
            rows.OrderByDescending(r => r.Reason, StringComparer.CurrentCulture).Select(r => r.Resource),
            ListSort.Order(rows, 1, true, Key).Select(r => r.Resource));
        // 资源列按自然序（「2」在「10」前）。
        var numbered = new[]
        {
            new ExclusionRow("GET /orders/10", "无需修改"),
            new ExclusionRow("GET /orders/2", "无需修改"),
        };
        Assert.Equal(["GET /orders/2", "GET /orders/10"], ListSort.Order(numbered, 0, false, Key).Select(r => r.Resource));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresBothListsThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        // 两张清单各记各的列与方向（排写入清单不会把未纳入清单也排了）。
        Assert.Matches(@"(?m)^\s*private int _actionSortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*private bool _actionSortDescending;$", source);
        Assert.Matches(@"(?m)^\s*private int _excludedSortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*private bool _excludedSortDescending;$", source);
        // 标题快照取自建列之后（表头标记按原标题重算，不叠加）。
        Assert.Matches(@"(?m)^\s*_actionTitles = _actionList\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        Assert.Matches(@"(?m)^\s*_excludedTitles = _excludedList\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        // 列头点击各接一次。
        Assert.Matches(@"(?m)^\s*_actionList\.ColumnClick \+= \(_, e\) => SortActions\(e\.Column\);", source);
        Assert.Matches(@"(?m)^\s*_excludedList\.ColumnClick \+= \(_, e\) => SortExcluded\(e\.Column\);", source);
        // 方向翻转只在共享口径一处（对话框不自己写 !descending）。
        Assert.Matches(@"(?m)^\s*\(_actionSortColumn, _actionSortDescending\) = ListSort\.Toggle\(_actionSortColumn, _actionSortDescending, column\);", source);
        Assert.Matches(@"(?m)^\s*\(_excludedSortColumn, _excludedSortDescending\) = ListSort\.Toggle\(_excludedSortColumn, _excludedSortDescending, column\);", source);
        Assert.DoesNotContain("_actionSortDescending = !", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_excludedSortDescending = !", source, StringComparison.Ordinal);
        // 表头标记按原标题重算（不就地拼方向字面）。
        Assert.Matches(@"(?m)^\s*_actionList\.Columns\[i\]\.Text = ListSort\.Header\(_actionTitles\[i\], i, _actionSortColumn, _actionSortDescending\);", source);
        Assert.Matches(@"(?m)^\s*_excludedList\.Columns\[i\]\.Text = ListSort\.Header\(_excludedTitles\[i\], i, _excludedSortColumn, _excludedSortDescending\);", source);
        Assert.DoesNotContain("▲", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▼", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_AppliesSortAfterFilterAndRefreshesHeadersAfterRebuild()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");

        var actions = Slice(source, "private void PopulateActions(", "private void SortActions(");
        // 筛选先于排序：ListFilter.Apply 的同一表达式作为 ListSort.Order 的输入，排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", actions);
        Assert.Contains("ListFilter.Apply(", actions, StringComparison.Ordinal);
        Assert.Contains("_actionSortColumn, _actionSortDescending,", actions, StringComparison.Ordinal);
        Assert.Contains("(row, column) => SyncPlanSort.ActionKey(", actions, StringComparison.Ordinal);
        // 表头标记在清单重建之后刷新（EndUpdate 之后），筛到零行那一支之前。
        var actionRefresh = actions.IndexOf("RefreshActionSortHeaders();", StringComparison.Ordinal);
        var actionEnd = actions.IndexOf("_actionList.EndUpdate();", StringComparison.Ordinal);
        var actionEmpty = actions.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(actionEnd >= 0 && actionRefresh > actionEnd, "表头标记要在清单重建（EndUpdate）之后刷新。");
        Assert.True(actionEmpty > actionRefresh, "表头标记要在筛到零行那一支之前刷新（那一支先返回）。");

        var excluded = Slice(source, "private void PopulateExcluded(", "private void SortExcluded(");
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", excluded);
        Assert.Contains("ListFilter.Apply(", excluded, StringComparison.Ordinal);
        Assert.Contains("_excludedSortColumn, _excludedSortDescending,", excluded, StringComparison.Ordinal);
        Assert.Contains("(exclusion, column) => SyncPlanSort.ExclusionKey(", excluded, StringComparison.Ordinal);
        var excludedRefresh = excluded.IndexOf("RefreshExcludedSortHeaders();", StringComparison.Ordinal);
        var excludedEnd = excluded.IndexOf("_excludedList.EndUpdate();", StringComparison.Ordinal);
        var excludedEmpty = excluded.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(excludedEnd >= 0 && excludedRefresh > excludedEnd, "表头标记要在清单重建（EndUpdate）之后刷新。");
        Assert.True(excludedEmpty > excludedRefresh, "表头标记要在筛到零行那一支之前刷新（那一支先返回）。");
    }

    [Fact]
    public void SortArmsCoverEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "SyncPlanSort.cs");
        Assert.Equal(7, Regex.Matches(dialog, @"(?m)^\s*_actionList\.Columns\.Add\(").Count);
        Assert.Equal(2, Regex.Matches(dialog, @"(?m)^\s*_excludedList\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 7), Arms(shared, "IComparable? ActionKey("));
        Assert.Equal(Enumerable.Range(0, 2), Arms(shared, "IComparable? ExclusionKey("));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "SyncPlanSort\\.ActionKey").Cast<Match>());
        Assert.Single(Regex.Matches(dialog, "SyncPlanSort\\.ExclusionKey").Cast<Match>());
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByActionIdAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按动作标识恢复（重建前记下、重建后找回），写入内容随选中事件跟上。
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        var actions = Slice(source, "private void PopulateActions(", "private void SortActions(");
        var remember = actions.IndexOf("var selectedId = (Ui.SelectedItem(_actionList)?.Tag as PlanAction)?.ActionId;", StringComparison.Ordinal);
        var clear = actions.IndexOf("_actionList.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "选中标识要在清空清单之前取局部。");
        // 恢复仍经唯一入口（Selected + Focused + 定位），不就地设 Selected、不自己滚动。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _actionList\.Items\[0\]\);$", actions);
        Assert.DoesNotContain(".Selected = true", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", actions, StringComparison.Ordinal);
        // 排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var sortActions = Slice(source, "private void SortActions(", "private void RefreshActionSortHeaders");
        Assert.Matches(@"(?m)^\s*PopulateActions\(\);$", sortActions);
        Assert.DoesNotContain("SelectItem", sortActions, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortActions, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAsync", sortActions, StringComparison.Ordinal);
        // 未纳入清单没有选中语义：排序与重建都不碰选中、不碰键盘焦点。
        var excluded = Slice(source, "private void PopulateExcluded(", "private void SortExcluded(");
        Assert.DoesNotContain("SelectItem", excluded, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", excluded, StringComparison.Ordinal);
        var sortExcluded = Slice(source, "private void SortExcluded(", "private void RefreshExcludedSortHeaders");
        Assert.Matches(@"(?m)^\s*PopulateExcluded\(\);$", sortExcluded);
        Assert.DoesNotContain("SelectItem", sortExcluded, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\SyncPlanSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSyncPlanSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("同步预览两张清单的列排序", section);
        Assert.Contains("`SyncPlanSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("同步预览两张清单的列排序（§6）", ledger);
        Assert.Contains("SyncPlanSort", ledger);
        // 本轮没做的事不能被写成做了。
        var row = ledger.Split('\n').Single(l => l.StartsWith("| 同步预览两张清单的列排序（§6） |", StringComparison.Ordinal));
        Assert.Contains("第一百零六轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        Assert.Contains("不改计划口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已真机验证", row, StringComparison.Ordinal);
    }

    private static List<int> Arms(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"缺少 {signature}。");
        var body = source[start..source.IndexOf("};", start, StringComparison.Ordinal)];
        return Regex.Matches(body, @"(?m)^\s*(\d+) => ").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
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
