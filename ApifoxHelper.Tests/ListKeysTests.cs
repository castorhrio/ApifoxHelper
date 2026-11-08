using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 主页列表按键断言（DESIGN.md §6「列表筛选」Enter 手势、§8 本轮条款）：Ctrl+F 把焦点交给筛选框，
/// 但筛完之后键盘走不出那个框——Enter 此前在筛选框里没有任何动作，要看某一行结果必须回鼠标点列表；
/// 配置页也只有双击能打开该行的编辑。两个手势都是 Windows 列表里本就存在的：搜索框输入完按 Enter
/// 到结果第一行（资源管理器、文件对话框），列表行上按 Enter 打开选中项（与双击同一动作）。
/// 口径单一落点在 <see cref="ListKeys"/>（不依赖 WinForms，与测试工程共享编译）：裸 Enter 判定与
/// 「显示序第一行」的目标行；两页接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）——
/// 组合 Enter 不吞键、零行不动作、焦点行缺失不借当前选中、选中只经既有唯一入口。
/// Enter 的实际按键路由与滚动对位随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListKeysTests
{
    private static readonly string[][] Pages =
    [
        ["Desktop", "Native", "TasksPage.cs"],
        ["Desktop", "Native", "ProjectsPage.cs"],
    ];

    // ---------- 口径真值表 ----------

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    public void Enter_Gesture_IsPlainEnterOnly(bool shift, bool control, bool alt, bool expected)
    {
        // 裸 Enter 才是手势：带 Ctrl/Alt/Shift 的组合留给控件自身，不吞键、不给既有组合键发明新语义。
        Assert.Equal(expected, ListKeys.IsEnterGesture(shift, control, alt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FirstResult_NoTargetWhenNothingDisplayed(int displayedCount)
    {
        // 筛到零行时没有目标行：空态的下一步是「清空筛选」按钮，不把焦点丢进空列表。
        Assert.Null(ListKeys.FirstResultIndex(displayedCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(40)]
    public void FirstResult_IsFirstDisplayedRow(int displayedCount)
    {
        // 显示序第一行（筛选与排序之后的可见顺序）：入参是可见行数，不是数据源行数。
        Assert.Equal(0, ListKeys.FirstResultIndex(displayedCount));
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void Pages_WireEnterInFilterBox()
    {
        foreach (var page in Pages)
        {
            var name = page[^1];
            var keys = Slice(Repo.Source(page), "_filter.KeyDown += (_, e) =>", "_list.SelectedIndexChanged");
            Assert.Matches(@"(?m)^\s*if \(e\.KeyCode == Keys\.Enter && ListKeys\.IsEnterGesture\(e\.Shift, e\.Control, e\.Alt\)\)$", keys);
            Assert.Matches(@"(?m)^\s*FocusFirstResult\(\);", keys);
            Assert.Matches(@"(?m)^\s*e\.SuppressKeyPress = true;", keys);
            // 既有 Esc 清空路径不动：Enter 分支不得改写清空。
            Assert.Matches(@"(?m)^\s*if \(e\.KeyCode != Keys\.Escape \|\| _filter\.Text\.Length == 0\) return;", keys);
            Assert.Matches(@"(?m)^\s*ClearListFilter\(\);", keys);
            // Enter 分支必须在 Esc 守卫的短路之前：放在其后就永远走不到。
            var enter = keys.IndexOf("Keys.Enter", StringComparison.Ordinal);
            var escape = keys.IndexOf("Keys.Escape", StringComparison.Ordinal);
            Assert.True(enter >= 0, $"{name}:筛选框没接 Enter。");
            Assert.True(escape >= 0, $"{name}:筛选框的 Esc 清空丢失。");
            Assert.True(enter < escape, $"{name}:Enter 分支必须在 Esc 守卫之前。");
        }
    }

    [Fact]
    public void Pages_FocusFirstResult_MovesFocusAndSelectionWithoutNewPaths()
    {
        foreach (var page in Pages)
        {
            var name = page[^1];
            var body = Slice(Repo.Source(page), "private void FocusFirstResult()", "private void ClearListFilter()");
            // 目标行取共享口径，不在页内就地写 0；选中经 Ui.SelectItem 唯一入口（与鼠标点行、重建恢复同一条路）。
            Assert.Matches(@"(?m)^\s*if \(ListKeys\.FirstResultIndex\(_list\.Items\.Count\) is not int index\) return;", body);
            Assert.Matches(@"(?m)^\s*var item = _list\.Items\[index\];", body);
            Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(item\);", body);
            // 选中的行必须在可见位置，否则「焦点交给第一行」这句话不成立——第五十七轮把这一步收进
            // Ui.SelectItem 唯一入口（见 ListRevealTests），页内不再各写一句滚动。
            Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
            // 列表先取焦点再选中：方向键从该行继续（锚点缺失时 IndexOf 为 -1，只比大小会被骗过）。
            var focus = body.IndexOf("_list.Focus();", StringComparison.Ordinal);
            var select = body.IndexOf("Ui.SelectItem(item);", StringComparison.Ordinal);
            Assert.True(focus >= 0, $"{name}:FocusFirstResult 未把焦点交给列表。");
            Assert.True(select >= 0, $"{name}:FocusFirstResult 未经 Ui.SelectItem 选中。");
            Assert.True(focus < select, $"{name}:应先让列表取得焦点再选中行。");
            // 只移焦点与选中：不自己改筛选关键词、不重建列表、不写数据。
            Assert.DoesNotMatch(@"(?m)^\s*_filter\.Text =", body);
            Assert.DoesNotMatch(@"(?m)^\s*Load(Tasks|Profiles)\(\);", body);
        }
    }

    [Fact]
    public void TaskPage_DoesNotInventARowAction()
    {
        // 任务页列表没有「打开该行」这一步（选中即出详情），本轮不为它发明 Enter 动作；
        // 配置页的行激活在 Ui.ActivateItem 内（见 ListActivationTests），两处口径不互相覆盖。
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.DoesNotMatch(@"(?m)^\s*_list\.KeyDown \+= ", tasks);
        Assert.DoesNotContain("ActivateItem", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellForm_HasNoAcceptButtonSoEnterReachesPageLists()
    {
        // 前提：壳窗体没有默认按钮，Enter 才不会被窗体的对话键处理取走、才能到筛选框与列表；
        // 各对话框自设 AcceptButton（Enter=确认），故 Enter 激活只按需在主页面接线。
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        Assert.DoesNotContain("AcceptButton", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCompile_IncludesListKeys()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListKeys.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListKeys.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsEnterGestures()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("列表筛选", section);
        Assert.Contains("Enter", section);
        Assert.Contains("`ListKeys`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("列表键盘通路", ledger);
        Assert.Contains("ListKeys", ledger);
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
