using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 列表右键菜单断言（DESIGN.md §6「列表行」、§8 本轮条款）：列表单元格自第二十三轮起由 <c>Ui.List</c>
/// 工厂自绘，文字不再可选中；第五十二轮补上的 Ctrl+C 是<b>唯一</b>把一格文字交给用户的通路——而它
/// <b>在屏幕上不可见</b>：不知道这个手势的人，看着「GET /路径」那一格仍然只能肉眼抄。本仓从第二十二轮起
/// 把「不做右键菜单」挂在账上（第五十二、五十三、五十七、六十一、六十二、六十三轮各记一次），挂账的理由是
/// 「右键不是点选手势，界面上也没有上下文菜单可给它翻」——现在有了：右键菜单是 Windows 列表里
/// 「这一行能做什么」的既有入口，而这一行能做的事恰好只有一件，就是复制它的标识列。
/// 口径单一落点在 <see cref="ListContextMenu"/>（不依赖 WinForms，与测试工程共享编译），接线只装在
/// <see cref="Ui"/> 的列表工厂一处：菜单项文字与「这一次该不该弹」都只在这一份里，动作与 Ctrl+C 走
/// 同一个守卫链（同一格文字、同一个剪贴板写入点），于是两条入口不会各说一套。
/// 实际右键路由与菜单观感随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListContextMenuTests
{
    // ---------- 口径真值表：这一次右键该不该弹 ----------

    [Theory]
    [InlineData(true, true)] // 右键 ∧ 命中在一行上——只有这一种弹。
    public void RightClickOnARow_IsTheOnlyOffer(bool rightButton, bool hitOnRow) =>
        Assert.True(ListContextMenu.ShouldOffer(rightButton, hitOnRow));

    [Theory]
    [InlineData(false, true)] // 左键：按下与抬起是选中、双击与勾选的地盘，本入口不接。
    [InlineData(true, false)] // 右键落在列头或清单外空白：那里没有「这一行」，菜单里的动作没有作用对象。
    [InlineData(false, false)]
    public void EverythingElse_NeverOffers(bool rightButton, bool hitOnRow) =>
        Assert.False(ListContextMenu.ShouldOffer(rightButton, hitOnRow));

    [Fact]
    public void Rule_CoversEveryCombination()
    {
        // 四个组合逐一对着「右键 ∧ 命中行」核一遍：多一分是左键也弹菜单，少一分是右键在行上没反应。
        for (var mask = 0; mask < 4; mask++)
        {
            var rightButton = (mask & 1) != 0;
            var hitOnRow = (mask & 2) != 0;
            Assert.Equal(rightButton && hitOnRow, ListContextMenu.ShouldOffer(rightButton, hitOnRow));
        }
    }

    // ---------- 菜单项文字：一件事、一项 ----------

    [Fact]
    public void CopyItemText_SaysCopyAndNothingElse()
    {
        // 与 Ctrl+C 是同一件事，所以只说「复制」——「哪一格」是 ListCopy 的口径，不是菜单要解释的事。
        Assert.Equal("复制(&C)", ListContextMenu.CopyItemText);
        Assert.StartsWith("复制", ListContextMenu.CopyItemText, StringComparison.Ordinal);
        // 助记符与壳菜单同一写法（`文件(&F)` 那一族）：键盘上按 C 也能选中这一项。
        Assert.Contains("(&C)", ListContextMenu.CopyItemText, StringComparison.Ordinal);
        // 不写「复制标识列」：菜单项是给用户看的，不是把内部口径名念一遍。
        Assert.DoesNotContain("标识列", ListContextMenu.CopyItemText, StringComparison.Ordinal);
    }

    // ---------- 接线机械检查：口径存在但没装到列表上等于没有 ----------

    [Fact]
    public void Ui_ListFactory_OffersTheMenuOnRightClickOverARow()
    {
        var slice = ListFactorySlice();
        // 弹不弹只问 ListContextMenu 一处：工厂不自己写 `e.Button == MouseButtons.Right && row is not null`。
        Assert.Matches(
            @"(?m)^\s*if \(!ListContextMenu\.ShouldOffer\(e\.Button == MouseButtons\.Right, row is not null\)\) return;",
            slice);
        // 右键先选中光标下那一行，且仍经 SelectItem 唯一入口（Selected + Focused + 滚入可见区）。
        Assert.Matches(@"(?m)^\s*SelectItem\(row\);", slice);
        // 没有可交出去的文字就灰着：不弹一个点了没反应的项。
        Assert.Matches(@"(?m)^\s*copyItem\.Enabled = CopyTarget\(\) is not null;", slice);
        // 菜单弹在清单上、弹在光标处（不是屏幕左上角，也不是某个固定点）。
        Assert.Matches(@"(?m)^\s*menu\.Show\(list, e\.Location\);", slice);
        // 顺序：先判该不该弹 → 再选中那一行 → 再定菜单项可不可点 → 最后才弹。
        var offer = slice.IndexOf("ListContextMenu.ShouldOffer", StringComparison.Ordinal);
        var select = slice.IndexOf("SelectItem(row);", StringComparison.Ordinal);
        var enable = slice.IndexOf("copyItem.Enabled = CopyTarget() is not null;", StringComparison.Ordinal);
        var show = slice.IndexOf("menu.Show(list, e.Location);", StringComparison.Ordinal);
        Assert.True(offer < select, "应先判该不该弹，再选中光标下那一行。");
        Assert.True(select < enable, "应先选中那一行，再按它判菜单项可不可点。");
        Assert.True(enable < show, "应先定菜单项可不可点，再弹菜单。");
    }

    [Fact]
    public void Ui_ListFactory_MenuHasExactlyOneItemAndSharesTheCopyGuard()
    {
        var slice = ListFactorySlice();
        // 只放一项：菜单里多一项就是给这一行发明第二个动作，而这一行能做的事只有一件。
        Assert.Single(Regex.Matches(slice, @"menu\.Items\.Add\("));
        Assert.Matches(@"(?m)^\s*menu\.Items\.Add\(copyItem\);", slice);
        // 菜单项文字取口径常量，工厂不就地拼字。
        Assert.Matches(@"(?m)^\s*var copyItem = new ToolStripMenuItem\(ListContextMenu\.CopyItemText\);", slice);
        // 动作与 Ctrl+C 走同一个守卫链：定义一处 + 三个调用点（Ctrl+C、菜单项点击、菜单项启停），
        // 于是「哪一格算标识列」「哪一行算被标出来」只有一处判定，两条入口不会各说一套。
        // 两件事分开数（第四百一十九轮，简报 P1-2）：定义处与调用处各数各的。旧写法
        // `Regex.Matches(slice, @"CopyTarget\(\)").Count - 1` 把「减掉的那一处」当成定义——定义签名一改
        // （比如加了参数、换了返回类型），它就不再被这一句数到，减 1 减掉的是某个调用点，
        // 于是「定义一处 + 三个调用点」这句话在源码里已经不成立、断言却照样绿。
        Assert.True(CopyGuardDefinition().Success, "工厂里找不到守卫链的定义。");
        Assert.Equal(3, CopyGuardCallSites());
        // 剪贴板写入点仍是 Ui.CopyToClipboard 一处（菜单项不自己碰 Clipboard）。
        Assert.DoesNotContain("Clipboard.SetText", slice, StringComparison.Ordinal);
        // 菜单项点击里先取守卫链的结果再写剪贴板：取不到就不动作，不把用户先前复制的内容抹掉。
        var click = MenuItemClickSlice();
        Assert.Matches(@"(?m)^\s*if \(CopyTarget\(\) is not string text\) return;", click);
        Assert.Matches(@"(?m)^\s*CopyToClipboard\(text\);", click);
    }

    [Fact]
    public void Ui_ListFactory_MenuDoesNotInventAVisualLanguage()
    {
        // 本仓的灰阶加单强调色说的是界面自己画的东西；系统菜单沿用系统观感（与表头「沿用系统绘制」同一取舍）。
        var menu = MenuSlice();
        Assert.DoesNotContain("ForeColor", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("BackColor", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("Font", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("Image", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("Renderer", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowImageMargin", menu, StringComparison.Ordinal);
        // 不自绘：自绘菜单就要自己画选中态与边框，那是另起一套视觉语言。
        Assert.DoesNotContain("OwnerDraw", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawItem", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextMenu_IsAControlLevelEntry_NotAShellMenuItem()
    {
        // 右键菜单是控件级入口，不进壳的菜单栏：菜单栏里放一项「复制」是假入口（焦点不在列表上时它复制不到东西）。
        Assert.DoesNotContain("复制(&C)", Repo.Source("Desktop", "Native", "NativeShellForm.cs"), StringComparison.Ordinal);
        // 也不占壳级快捷键册：Ctrl+C 仍是保留编辑键，壳不抢（与第五十二轮同一分册）。
        Assert.Null(ShellShortcuts.GestureOf("copy"));
        Assert.Contains("Ctrl+C", ShellShortcuts.ReservedEditingKeys);
    }

    [Fact]
    public void OnlyTheListFactory_InstallsAContextMenu()
    {
        // 右键菜单只装在工厂一处：页面与对话框不各装一套（与「剪贴板只触达一次」同一纪律）。
        foreach (var source in Repo.NativeSources())
        {
            var hits = Regex.Matches(source.Code, @"new ContextMenuStrip\(").Count;
            if (source.IsUi)
            {
                Assert.Equal(1, hits);
                continue;
            }

            Assert.Equal(0, hits);
        }
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void SharedCompile_IncludesListContextMenu()
    {
        // 口径只有一份：真值表跑的就是主工程同一份代码。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListContextMenu.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsTheContextMenu()
    {
        // 断言取本轮条款独有的字面，不取那些节里早已被别轮写过的那几句。
        var section = Repo.DesignSection(6);
        Assert.Contains("**列表右键菜单**（`ListContextMenu` 单一口径，第四百一十八轮", section);
        Assert.Contains("ListContextMenu.ShouldOffer", section);
        Assert.Contains("复制(&C)", section);
        // 挂账收口：第二十二轮起「不做右键菜单」的那一笔在这里结清，理由写在同一处。
        Assert.Contains("不做右键菜单", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 列表右键菜单（§6） \|", ledger);
        Assert.Contains("ListContextMenu.ShouldOffer(右键, 命中行)", ledger);
        Assert.Contains("两条入口、一件事、一处判定", ledger);
    }

    /// <summary>
    /// 守卫链的定义处（第四百一十九轮，简报 P1-2）：局部函数声明那一行 <c>string? CopyTarget()</c>，
    /// 独占一行、恰一处。
    /// <para>
    /// <b>为什么单独取这一处</b>：调用点的条数只有在「定义确实还是这一行」时才有意义——签名一改
    /// （加参数、换返回类型、改成字段），定义就不再被这个形状认到，此时「三个调用点」这句话已经不成立，
    /// 该红的是定义这一句，而不是让「总数减 1」把某个调用点当成定义蒙过去。
    /// </para>
    /// </summary>
    private static Match CopyGuardDefinition()
    {
        var matches = Regex.Matches(ListFactorySlice(), @"(?m)^\s*string\? CopyTarget\(\)\s*$");
        Assert.True(matches.Count == 1,
            $"工厂里 `string? CopyTarget()` 这一行有 {matches.Count} 处，口径是恰一处："
            + "守卫链的定义只有一份，两条入口（Ctrl+C 与右键菜单）共用它。");
        return matches[0];
    }

    /// <summary>
    /// 守卫链的调用处条数（第四百一十九轮，简报 P1-2）：把定义那一行切掉之后，剩下的部分里数
    /// <c>CopyTarget()</c>——三个（Ctrl+C、菜单项点击、菜单项启停）。
    /// </summary>
    private static int CopyGuardCallSites()
    {
        var slice = ListFactorySlice();
        var definition = CopyGuardDefinition();
        return Regex.Matches(slice.Remove(definition.Index, definition.Length), @"CopyTarget\(\)").Count;
    }

    /// <summary>Ui.List 工厂的方法体切片（从构造到 SelectItem 之前），右键菜单接线必须在这一段内。</summary>
    private static string ListFactorySlice()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var from = ui.IndexOf("public static ListView List(", StringComparison.Ordinal);
        Assert.True(from >= 0, "源码缺少 Ui.List 工厂。");
        var to = ui.IndexOf("public static void SelectItem", StringComparison.Ordinal);
        Assert.True(to > from, "源码缺少 Ui.SelectItem。");
        return ui[from..to];
    }

    /// <summary>菜单构造那一段（从 <c>new ContextMenuStrip()</c> 到把菜单项加进去）。</summary>
    private static string MenuSlice()
    {
        var slice = ListFactorySlice();
        var from = slice.IndexOf("var menu = new ContextMenuStrip();", StringComparison.Ordinal);
        Assert.True(from >= 0, "源码缺少右键菜单的构造。");
        var to = slice.IndexOf("menu.Items.Add(copyItem);", StringComparison.Ordinal);
        Assert.True(to > from, "源码缺少把菜单项加进菜单的那一句。");
        return slice[from..to];
    }

    /// <summary>菜单项点击那一段（从 <c>copyItem.Click</c> 到把菜单项加进菜单）。</summary>
    private static string MenuItemClickSlice()
    {
        var slice = ListFactorySlice();
        var from = slice.IndexOf("copyItem.Click += ", StringComparison.Ordinal);
        Assert.True(from >= 0, "源码缺少菜单项的点击接线。");
        var to = slice.IndexOf("menu.Items.Add(copyItem);", StringComparison.Ordinal);
        Assert.True(to > from, "源码缺少把菜单项加进菜单的那一句。");
        return slice[from..to];
    }
}
