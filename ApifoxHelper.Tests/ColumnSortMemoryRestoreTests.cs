using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「点过列头之后，下次打开还是那一列那一方向」这条链的<b>逻辑层</b>断言（第三百二十八轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>ListSortMemory</c> 自己的读写口径有 <c>ListSortMemoryTests</c> 逐条断言，
/// 两页的接线也有行首正则钉住；但<b>「记忆读回来的那一列那一方向，渲染出来是不是点击当时那一屏」</b>
/// 此前没有任何一处判过——记忆写对了列、页面也读了，可 <c>ListSort.Header</c> 传错参数、
/// 或者 <c>ListSort.Order</c> 用了另一个方向，屏幕上就是「表头带着 ▲、行序却是倒的」。
/// 那种错在 Linux 上全绿，只有真控件才看得见（冒烟器第三百二十八轮那两段量的就是它）。
/// </para>
/// <para>
/// <b>本文件判三件事</b>：① 记忆读回来的状态渲染出来，与点击当时逐字相同（表头文字 + 行序，七列 × 两方向）；
/// ② 认不出标题时回退未排序，且<b>表头一个方向标记都不带</b>（不残留上次的标记）；
/// ③ 界面在它前面插了一列之后，恢复出来的标记仍落在<b>同一列</b>上（记标题而不是列号的实际收益）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真控件上点一次列头会不会落进 <c>ColumnClick</c>（那是冒烟器
/// <c>RunColumnSortGesture</c> 那八段的事），也不判记忆文件写不写得进去（那是 <c>ListSortMemoryTests</c> 的事）。
/// 三处各判一段，谁也不替谁作证。
/// </para>
/// </summary>
public class ColumnSortMemoryRestoreTests : IDisposable
{
    /// <summary>任务列表的七列标题（与 <c>TasksPage</c> 建列时逐字相同）。</summary>
    private static readonly string[] TaskTitles = ["任务", "配置", "下一步", "接口", "注释候选", "Apifox", "更新时间"];

    /// <summary>
    /// 夹具：任务名带序号（自然序 1 → 2 → 10 与文字序 1 → 10 → 2 不同），
    /// 配置名与任务名的顺序<b>故意错开</b>——两种排法互不相同，才量得出「恢复的是哪一列」。
    /// </summary>
    private static readonly (string Name, string Profile)[] Rows =
    [
        ("排序任务2", "配置1"),
        ("排序任务1", "配置2"),
        ("排序任务10", "配置3"),
    ];

    private readonly string _root;
    private readonly string _path;

    public ColumnSortMemoryRestoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apifox-helper-sortrestore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "list-sort.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>排序键：与 <c>TasksPage.SortKey</c> 同一形状（名称列按任务名、配置列按配置名，其余列不参与本文件）。</summary>
    private static IComparable? Key((string Name, string Profile) row, int column) => column switch
    {
        0 => row.Name,
        1 => row.Profile,
        _ => null,
    };

    /// <summary>
    /// 恢复出来的状态渲染成「屏幕上那一屏」：表头文字 + 行序。
    /// <para>
    /// <b>渲染用的是产品自己的 <see cref="ListSort.Header"/> 与 <see cref="ListSort.Order"/></b>——
    /// 这一条判的是「记忆读回来的列与方向，喂给产品渲染之后是不是点击当时那一屏」。
    /// 期望值<b>不从这里取</b>（两侧都调同一个函数，等于判据在判自己：把 <c>Header</c> 改成永远画 ▲，
    /// 两侧一起变，这一条照旧全绿——第三百二十八轮第一版就是这么写的，变异当场验出来了）。
    /// 期望值一律写成字面量，见 <see cref="RestoredState_RendersTheSameHeadersAndRowOrderAsTheClick"/>。
    /// </para>
    /// </summary>
    private static (string[] Headers, string[] Rows) Render(int column, bool descending)
    {
        var headers = TaskTitles.Select((title, i) => ListSort.Header(title, i, column, descending)).ToArray();
        var rows = ListSort.Order(Rows, column, descending, Key).Select(row => row.Name).ToArray();
        return (headers, rows);
    }

    /// <summary>
    /// ① 记忆读回来的状态渲染出来，与点击当时逐字相同：七列 × 两方向，表头文字与行序都要对上。
    /// <para>
    /// 这一条是「跨启动」这件事的全部内容——用户点了一次列头，下次打开看到的必须是同一屏。
    /// 记忆写对了列、页面也读了，但渲染时把方向传反，屏幕上就是「表头 ▲、行序倒着」。
    /// </para>
    /// </summary>
    [Theory]
    // 任务列：自然序 1 → 2 → 10（不是文字序 1 → 10 → 2），反向就是 10 → 2 → 1。
    [InlineData(0, false, "任务 ▲", "排序任务1,排序任务2,排序任务10")]
    [InlineData(0, true, "任务 ▼", "排序任务10,排序任务2,排序任务1")]
    // 配置列：配置名与任务名的顺序故意错开，所以这一列排出来的行序与任务列不同。
    [InlineData(1, false, "配置 ▲", "排序任务2,排序任务1,排序任务10")]
    [InlineData(1, true, "配置 ▼", "排序任务10,排序任务1,排序任务2")]
    // 其余列本文件不给排序键（键全为 null）：两个方向都保持数据源顺序，但标记仍要画在那一列上。
    [InlineData(2, false, "下一步 ▲", "排序任务2,排序任务1,排序任务10")]
    [InlineData(6, true, "更新时间 ▼", "排序任务2,排序任务1,排序任务10")]
    public void RestoredState_RendersTheSameHeadersAndRowOrderAsTheClick(
        int column, bool descending, string expectedMarkedHeader, string expectedRows)
    {
        new ListSortMemory(_path).Save(ListSortMemoryKeys.Tasks, TaskTitles, column, descending);

        // 另一份实例读回来：模拟下次启动（内存里的字段一个都不在）。
        var (restoredColumn, restoredDescending) = new ListSortMemory(_path).Read(ListSortMemoryKeys.Tasks, TaskTitles);
        Assert.Equal((column, descending), (restoredColumn, restoredDescending));

        var (headers, rows) = Render(restoredColumn, restoredDescending);

        // 期望值是字面量（「任务 ▲」/「任务 ▼」逐字写出来），不从 ListSort.Header 取——
        // 两侧都调同一个函数的话，把 Header 改成永远画 ▲ 这一条也照旧全绿。
        Assert.Equal(expectedMarkedHeader, headers[column]);
        for (var i = 0; i < TaskTitles.Length; i++)
        {
            if (i == column) continue;
            Assert.Equal(TaskTitles[i], headers[i]);
        }

        Assert.Equal(expectedRows.Split(','), rows);
    }

    /// <summary>
    /// ② 认不出标题时回退未排序，且表头<b>一个方向标记都不带</b>。
    /// <para>
    /// 列被删掉或文案改动之后，记忆里那个标题在本页列头里找不到——此时必须回到数据源顺序，
    /// 而且不能把上次的标记留在某一列上：带着标记却是数据源顺序，比不记还糟。
    /// </para>
    /// </summary>
    [Fact]
    public void RestoredState_WhenTheTitleIsGone_DrawsNoMarkAndKeepsSourceOrder()
    {
        new ListSortMemory(_path).Save(ListSortMemoryKeys.Tasks, TaskTitles, 1, descending: true);

        // 界面改过列：记忆里那个「配置」列不在了。
        string[] renamed = ["任务", "所属配置", "下一步", "接口", "注释候选", "Apifox", "更新时间"];
        var (column, descending) = new ListSortMemory(_path).Read(ListSortMemoryKeys.Tasks, renamed);

        Assert.Equal((ListSort.None, false), (column, descending));

        var headers = renamed.Select((title, i) => ListSort.Header(title, i, column, descending)).ToArray();
        Assert.Equal(renamed, headers);
        Assert.DoesNotContain(headers, header => header.Contains(ListSort.AscendingMark, StringComparison.Ordinal)
            || header.Contains(ListSort.DescendingMark, StringComparison.Ordinal));

        // 行序回到数据源自身顺序。
        Assert.Equal(Rows.Select(row => row.Name), ListSort.Order(Rows, column, descending, Key).Select(row => row.Name));
    }

    /// <summary>
    /// ③ 界面在它前面插了一列之后，恢复出来的标记仍落在<b>同一列</b>上——记标题而不是列号的实际收益。
    /// <para>
    /// 记列号的话，插一列之后「第 1 列」变成了另一列，用户上次排的那一列被悄悄换掉；
    /// 记标题则跟着那一列走。这一条同时钉住「恢复出来的列号要喂给渲染，而不是原样用记忆里的数」。
    /// </para>
    /// </summary>
    [Fact]
    public void RestoredState_AfterAColumnIsInserted_MarksTheSameColumn()
    {
        new ListSortMemory(_path).Save(ListSortMemoryKeys.Tasks, TaskTitles, 1, descending: false);

        string[] afterInsert = ["任务", "模块", "配置", "下一步", "接口", "注释候选", "Apifox", "更新时间"];
        var (column, descending) = new ListSortMemory(_path).Read(ListSortMemoryKeys.Tasks, afterInsert);

        Assert.Equal((2, false), (column, descending));

        var headers = afterInsert.Select((title, i) => ListSort.Header(title, i, column, descending)).ToArray();
        Assert.Equal("配置 " + ListSort.AscendingMark, headers[2]);
        Assert.Equal("模块", headers[1]);
        Assert.Equal("任务", headers[0]);
    }
}
