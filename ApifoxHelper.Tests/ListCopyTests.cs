using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 列表 Ctrl+C 复制断言（DESIGN.md §6「列表行」复制条款、§8 本轮条款）：列表单元格自绘后文字不可选中，
/// 而原生界面此前没有任何把文字交给用户的通路——没有剪贴板入口、没有右键菜单，只有只读预览框能选。
/// 「GET /路径」这类接口身份只出现在检查结果、补充说明、同步预览与契约/追踪清单的首列，新建任务的
/// 「指定接口」又要逐行敲同样的文字，中间这一跳只能靠肉眼抄。补的是资源管理器本就有的手势：
/// Ctrl+C 复制选中项的名称列——这里即首列（标识列），不是整行拼接。
/// 口径单一落点在 <see cref="ListCopy"/>（不依赖 WinForms，与测试工程共享编译），接线只装在
/// <see cref="Ui"/> 的列表工厂一处（剪贴板在整个原生界面触达一次）：以焦点行为准、未选中不动作、
/// 首列无文字不动作（不把用户先前复制的内容抹掉），组合 Ctrl+C 不吞键，剪贴板被占用不谎称已复制。
/// 实际按键路由与剪贴板内容的粘贴验证随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListCopyTests
{
    // ---------- 口径真值表 ----------

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void Gesture_IsCtrlPlusCOnly(bool shift, bool control, bool alt, bool expected)
    {
        // 只有 Ctrl+C 是复制：裸 C 是往筛选框里打字，Shift/Alt 组合留给控件自身，不吞键、不发明新语义。
        Assert.Equal(expected, ListCopy.IsCopyGesture(shift, control, alt));
    }

    [Theory]
    [InlineData("GET /api/orders")]
    [InlineData("订单查询")]
    [InlineData("  前后有空格的一格  ")]
    public void CopyText_ReturnsTheIdentityCellVerbatim(string text)
    {
        // 复制的是首列原文：不裁剪、不补空白、不改大小写——粘贴出去的就是看到的那一格。
        Assert.Equal(text, ListCopy.CopyText(text, true));
    }

    [Theory]
    [InlineData("GET /api/orders")]
    [InlineData("订单查询")]
    public void CopyText_IsNull_WhenFocusedRowIsNotSelected(string text)
    {
        // 只复制被标出来的那一行（自绘选中态就是标记）：焦点行未选中即不动作，与鼠标看到的不是同一行时不抄。
        Assert.Null(ListCopy.CopyText(text, false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CopyText_IsNull_WhenIdentityCellIsBlank(string text)
    {
        // 首列没有文字就没有可交出去的东西：返回 null，不动剪贴板——空复制会把用户先前复制的内容抹掉。
        Assert.Null(ListCopy.CopyText(text, true));
    }

    [Fact]
    public void CopyText_IsNull_WhenRowTextIsMissing()
    {
        Assert.Null(ListCopy.CopyText(null, true));
    }

    // ---------- 接线机械检查：口径存在但没装到列表上等于没有 ----------

    [Fact]
    public void Ui_ListFactory_InstallsCopyOnEveryList()
    {
        var slice = ListFactorySlice();
        // 修饰键与目标文字都取共享口径，不在工厂里就地判定。
        Assert.Matches(@"(?m)^\s*if \(e\.KeyCode != Keys\.C \|\| !ListCopy\.IsCopyGesture\(e\.Shift, e\.Control, e\.Alt\)\) return;", slice);
        Assert.Matches(@"(?m)^\s*if \(list\.FocusedItem is not ListViewItem row\) return;", slice);
        Assert.Matches(@"(?m)^\s*if \(ListCopy\.CopyText\(row\.Text, row\.Selected\) is not string text\) return;", slice);
        // 动作前吞键，且写在剪贴板之前：与 Enter 激活同一纪律。
        Assert.Matches(@"(?m)^\s*e\.SuppressKeyPress = true;", slice);
        Assert.Matches(@"(?m)^\s*Clipboard\.SetText\(text\);", slice);
        var suppress = slice.IndexOf("e.SuppressKeyPress = true;", StringComparison.Ordinal);
        var set = slice.IndexOf("Clipboard.SetText(text);", StringComparison.Ordinal);
        Assert.True(suppress < set, "应先吞键再写剪贴板。");
        // 剪贴板可能被其他进程占用：不冒到 UI 线程的异常处理里，也不把它显示成已复制。
        Assert.Matches(@"(?m)^\s*catch \(ExternalException\)", slice);
    }

    [Fact]
    public void Ui_ListFactory_CopiesOnlyTheIdentityColumn()
    {
        var slice = ListFactorySlice();
        // 复制首列原文（row.Text），不遍历其余列、不拼整行——与资源管理器「复制名称列」一致。
        Assert.DoesNotContain("SubItems", slice, StringComparison.Ordinal);
        // 就地判定修饰键（不经 ListCopy）是第二套口径，不允许出现。
        Assert.DoesNotContain("e.Control &&", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void Clipboard_IsTouchedOnlyInTheListFactory()
    {
        // 剪贴板在整个原生界面只出现在工厂一处：页面与对话框不再各装一套复制。
        // 行首只允许空白再接 Clipboard——被注释掉的调用不算接线（变异验证 M5 实测抓出这一盲区）。
        foreach (var source in Repo.NativeSources())
        {
            var hits = Regex.Matches(source.Text, @"(?m)^[ \t]*Clipboard\.").Count;
            if (source.IsUi)
            {
                Assert.Equal(1, hits);
                continue;
            }

            Assert.Equal(0, hits);
        }
    }

    [Fact]
    public void Pages_DoNotInstallTheirOwnCopyGesture()
    {
        foreach (var source in Repo.NativeSources())
        {
            if (source.IsUi || source.Name == "ListCopy.cs") continue;
            Assert.Empty(source.Matches(@"Keys\.C\b"));
            Assert.DoesNotContain("ListCopy.", source.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Copy_IsAControlLevelGesture_NotAShellShortcut()
    {
        // 复制不占壳级快捷键册（菜单里没有「复制」项，显示了反而是假入口）；Ctrl+C 是保留编辑键，
        // 壳不抢——与筛选框的 Esc/Enter 同一分册（§6「菜单/快捷键」）。
        Assert.Null(ShellShortcuts.GestureOf("copy"));
        Assert.Contains("Ctrl+C", ShellShortcuts.ReservedEditingKeys);
        Assert.DoesNotContain("Ctrl+C", Repo.Source("Desktop", "Native", "NativeShellForm.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCompile_IncludesListCopy()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListCopy.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListCopy.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsCopyGesture()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("Ctrl+C", section);
        Assert.Contains("ListCopy", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("列表复制", ledger);
        Assert.Contains("ListCopy", ledger);
    }

    /// <summary>Ui.List 工厂的方法体切片（从构造到 SelectItem 之前），复制接线必须在这一段内。</summary>
    private static string ListFactorySlice()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var from = ui.IndexOf("public static ListView List(", StringComparison.Ordinal);
        Assert.True(from >= 0, "源码缺少 Ui.List 工厂。");
        var to = ui.IndexOf("public static void SelectItem", StringComparison.Ordinal);
        Assert.True(to > from, "源码缺少 Ui.SelectItem。");
        return ui[from..to];
    }
}
