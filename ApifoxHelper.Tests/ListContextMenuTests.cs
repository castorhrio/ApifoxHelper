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
/// <b>第四百二十二轮补的是键盘那一半</b>：那份菜单此前只有鼠标到得了——键盘用户看着焦点行仍然拿不到
/// 那一格文字。Windows 列表里打开上下文菜单的两个既有手势（<c>Shift+F10</c> 与菜单键 <c>VK_APPS</c>）
/// 现接到<b>同一份菜单</b>上：手势与「有没有焦点行」由 <see cref="ListContextMenu.ShouldOfferByKeyboard"/> 判，
/// 弹出与「可不可点」走工厂里同一个 <c>ShowMenu</c>，于是仍是「两条入口、一件事、一处判定」。
/// 实际右键与按键路由、菜单观感随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）。
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

    // ---------- 键盘上的同一件事（第四百二十二轮） ----------

    [Theory]
    [InlineData(false, true, true)] // Shift+F10：Windows 列表里打开上下文菜单的既有手势。
    [InlineData(true, false, false)] // 菜单键（VK_APPS）：同一个动作的另一个既有手势。
    public void MenuGesture_IsShiftF10OrTheMenuKey(bool menuKey, bool shift, bool f10) =>
        Assert.True(ListContextMenu.IsMenuGesture(menuKey, shift, f10));

    [Theory]
    [InlineData(false, false, true)] // 裸 F10：系统「把焦点交给菜单栏」的手势，壳有菜单栏，不抢。
    [InlineData(false, true, false)] // 裸 Shift：不是手势。
    [InlineData(false, false, false)] // 什么都没按。
    [InlineData(true, true, true)] // 菜单键与 Shift+F10 一起按：仍是同一个手势，不重复弹。
    public void EverythingElse_IsNotAMenuGesture(bool menuKey, bool shift, bool f10) =>
        Assert.Equal(menuKey || (shift && f10), ListContextMenu.IsMenuGesture(menuKey, shift, f10));

    [Fact]
    public void KeyboardOffer_NeedsAFocusedRow()
    {
        // 手势成立 ∧ 有焦点行——只有这一种弹。
        Assert.True(ListContextMenu.ShouldOfferByKeyboard(false, true, true, true));
        Assert.True(ListContextMenu.ShouldOfferByKeyboard(true, false, false, true));
        // 手势成立但没有焦点行（清单是空的、焦点框不在任何一行上）：菜单里的动作没有作用对象。
        Assert.False(ListContextMenu.ShouldOfferByKeyboard(false, true, true, false));
        Assert.False(ListContextMenu.ShouldOfferByKeyboard(true, false, false, false));
        // 有焦点行但不是手势：不弹（裸 F10 归菜单栏、裸 Enter 归「打开选中行」）。
        Assert.False(ListContextMenu.ShouldOfferByKeyboard(false, false, true, true));
        Assert.False(ListContextMenu.ShouldOfferByKeyboard(false, false, false, true));

        // 全组合对着「手势 ∧ 焦点行」核一遍：多一分是别的键也弹菜单，少一分是键盘在行上按不动。
        for (var mask = 0; mask < 16; mask++)
        {
            var menuKey = (mask & 1) != 0;
            var shift = (mask & 2) != 0;
            var f10 = (mask & 4) != 0;
            var hasFocusedRow = (mask & 8) != 0;
            Assert.Equal(
                (menuKey || (shift && f10)) && hasFocusedRow,
                ListContextMenu.ShouldOfferByKeyboard(menuKey, shift, f10, hasFocusedRow));
        }
    }

    [Fact]
    public void KeyboardAndMouse_AreTwoQuestionsNotOne()
    {
        // 两个入口分开判：鼠标问「光标下有没有一行」，键盘问「焦点框在哪一行」——答案可以不同
        // （右键落在别的行上时焦点行还是原来那一行）。合并成一个参数会把「右键先选中光标下那一行」读丢。
        Assert.True(ListContextMenu.ShouldOffer(true, true));
        Assert.False(ListContextMenu.ShouldOfferByKeyboard(false, false, false, true));
        // 键盘那一条不看「命中行」：它没有光标位置可言。
        Assert.True(ListContextMenu.ShouldOfferByKeyboard(false, true, true, true));
        Assert.False(ListContextMenu.ShouldOffer(false, true));
    }

    [Fact]
    public void Ui_ListFactory_OffersTheMenuFromTheKeyboard()
    {
        var slice = ListFactorySlice();
        // 弹不弹只问 ListContextMenu 一处：工厂不自己写 `e.KeyCode == Keys.F10 && e.Shift`。
        Assert.Matches(
            @"(?m)^\s*if \(!ListContextMenu\.ShouldOfferByKeyboard\(\s*$",
            slice);
        Assert.Contains("e.KeyCode == Keys.Apps, e.Shift, e.KeyCode == Keys.F10, list.FocusedItem is not null",
            slice, StringComparison.Ordinal);
        // 键盘这一下没有光标位置：弹在焦点行的左下角（贴着它作用的那一行），不弹在屏幕左上角。
        Assert.Matches(@"(?m)^\s*ShowMenu\(new Point\(row\.Bounds\.Left, row\.Bounds\.Bottom\)\);", slice);
        // 先吞键再弹：不吞的话 Shift+F10 会继续往系统走（壳的菜单栏、清单自己的默认处理）。
        var suppress = slice.IndexOf("e.SuppressKeyPress = true;", StringComparison.Ordinal);
        var show = slice.IndexOf("ShowMenu(new Point(row.Bounds.Left, row.Bounds.Bottom));", StringComparison.Ordinal);
        Assert.True(suppress >= 0 && show > suppress, "应先吞键，再弹菜单。");
    }

    [Fact]
    public void TwoEntries_ShareOnePopupPoint()
    {
        // 两条入口、一件事、一处判定：弹出与「可不可点」收在工厂里那一个 ShowMenu 上——
        // 定义恰一处、调用恰两处（右键、键盘），menu.Show 全工厂恰一处。
        var slice = ListFactorySlice();
        Assert.Single(Regex.Matches(slice, @"(?m)^\s*void ShowMenu\(Point at\)\s*$"));
        // 调用恰两处：右键（光标处）与键盘（焦点行左下角）——定义那一行不以 ShowMenu 起头，不混进这一数。
        Assert.Equal(2, Regex.Matches(slice, @"(?m)^\s*ShowMenu\(").Count);
        Assert.Single(Regex.Matches(slice, @"menu\.Show\("));
        // 两条入口都不自己定可不可点：那一句只在 ShowMenu 里出现一次。
        Assert.Single(Regex.Matches(slice, @"copyItem\.Enabled = "));
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
        // 没有可交出去的文字就灰着：不弹一个点了没反应的项（第四百二十二轮起这一句收在 ShowMenu 里，
        // 右键与键盘两条入口共用——两条入口各写一遍，迟早一条灰着、一条可点）。
        Assert.Matches(@"(?m)^\s*copyItem\.Enabled = CopyTarget\(\) is not null;", slice);
        // 菜单弹在清单上、弹在给定的那一点上（不是屏幕左上角，也不是某个固定点）。
        Assert.Matches(@"(?m)^\s*menu\.Show\(list, at\);", slice);
        // 顺序：先判该不该弹 → 再选中那一行 → 再把落点交给 ShowMenu（第四百二十二轮起
        // 「定可不可点 + 弹」收在 ShowMenu 一处，两条入口共用，故那两句不再排在这一段里）。
        var offer = slice.IndexOf("ListContextMenu.ShouldOffer(", StringComparison.Ordinal);
        var select = slice.IndexOf("SelectItem(row);", StringComparison.Ordinal);
        var popup = slice.IndexOf("ShowMenu(e.Location);", StringComparison.Ordinal);
        Assert.True(offer < select, "应先判该不该弹，再选中光标下那一行。");
        Assert.True(select < popup, "应先选中那一行，再弹菜单（菜单项可不可点按选中后的焦点行判）。");
        // ShowMenu 自己那两句的顺序：先定可不可点，再弹。
        var enable = slice.IndexOf("copyItem.Enabled = CopyTarget() is not null;", StringComparison.Ordinal);
        var show = slice.IndexOf("menu.Show(list, at);", StringComparison.Ordinal);
        Assert.True(enable >= 0 && show > enable, "应先定菜单项可不可点，再弹菜单。");
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
        var guard = ReadCopyGuardShape(slice);
        Assert.True(guard.Problems.Count == 0, string.Join(" ／ ", guard.Problems));
        Assert.Equal(3, guard.CallSites);
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
        // 第四百二十二轮：键盘入口那一半也落在同一处（§6 条款 + §8 台账行）。
        Assert.Contains("**列表右键菜单的键盘入口**（第四百二十二轮）", section);
        Assert.Contains("ListContextMenu.ShouldOfferByKeyboard(菜单键, Shift, F10, 有焦点行)", section);
        Assert.Matches(@"(?m)^\| 列表右键菜单的键盘入口（§6） \|", ledger);
        Assert.Contains("ListContextMenu.ShouldOfferByKeyboard(菜单键, Shift, F10, 有焦点行)", ledger);
        Assert.Contains("Shift+F10", ledger);
    }

    /// <summary>
    /// 守卫链形状的判定（第四百二十一轮，简报 P0-2：把第四百二十轮 commit message 里做过的两处变异
    /// 落进测试代码）。返回问题清单（空 = 形状成立）与调用点条数。
    /// <para>
    /// <b>为什么收成一个纯函数</b>：第四百二十轮改断言时是<b>手工</b>把 <c>string?</c> 改成 <c>object?</c>、
    /// 再给定义加一个参数，跑一遍看红不红，然后改回来——那两发变异只活在 commit message 里。
    /// 下一轮谁再动守卫链的形状，没有任何机械保证。收成一处之后，
    /// <see cref="CopyGuardShapeDetector_ReportsTheBreaksItMustCatch"/> 能把那两发原样喂进来。
    /// </para>
    /// <para>
    /// <b>它判什么</b>：定义处恰一处（<c>string? CopyTarget()</c> 独占一行）、调用处恰三处
    /// （Ctrl+C、菜单项点击、菜单项启停）。定义处认不到时<b>不再数调用点</b>——那正是第四百一十九轮
    /// 之前那句 <c>Regex.Matches(slice, @"CopyTarget\(\)").Count - 1</c> 的假绿来源：
    /// 定义签名一改，它就不再被数到，「减 1」减掉的是某个调用点，于是「定义一处 + 三个调用点」
    /// 这句话在源码里已经不成立、断言却照样绿。
    /// </para>
    /// </summary>
    private static (List<string> Problems, int CallSites) ReadCopyGuardShape(string slice)
    {
        var problems = new List<string>();
        var definitions = Regex.Matches(slice, @"(?m)^\s*string\? CopyTarget\(\)\s*$");
        if (definitions.Count != 1)
        {
            problems.Add($"工厂里 `string? CopyTarget()` 这一行有 {definitions.Count} 处，口径是恰一处："
                         + "守卫链的定义只有一份，两条入口（Ctrl+C 与右键菜单）共用它。");
            return (problems, -1);
        }

        var callSites = Regex.Matches(
            slice.Remove(definitions[0].Index, definitions[0].Length), @"CopyTarget\(\)").Count;
        if (callSites != 3)
        {
            problems.Add($"守卫链的调用处读到 {callSites} 处，口径是三处（Ctrl+C、菜单项点击、菜单项启停）："
                         + "少一处 = 有一条入口没接上，多一处 = 多了一条没人登记的入口。");
        }

        return (problems, callSites);
    }

    /// <summary>
    /// 守卫链形状的判定<b>抓不抓得住</b>（第四百二十一轮，简报 P0-2）。
    /// <para>
    /// 第四百二十轮把计数断言从「总数减 1」改成「定义处与调用处各数各的」，验证方式是<b>手工</b>做两处变异
    /// （<c>string?</c> → <c>object?</c>、给定义加一个参数），跑一遍看红不红，然后改回来——那两发变异
    /// 只留在 commit message 里。本门把它们写成夹具：喂同一段源码切片给 <see cref="ReadCopyGuardShape"/>，
    /// 断言它当场红。形状与 <c>LayoutMeasurementGateTests.ReasonDetector_ReportsTheBreaksItMustCatch</c> 同一做法。
    /// </para>
    /// <para>
    /// <b>第一发还额外钉住「旧写法在这一发上照样绿」</b>：<c>object? CopyTarget()</c> 之后
    /// <c>CopyTarget()</c> 仍出现 4 次，旧断言 <c>Count - 1 == 3</c> 照样成立——这正是第四百一十九轮
    /// 那一句的假绿形状。把它写进断言里，下一轮谁把判定改回「总数减 1」即红。
    /// </para>
    /// </summary>
    [Fact]
    public void CopyGuardShapeDetector_ReportsTheBreaksItMustCatch()
    {
        var slice = ListFactorySlice();

        // 前提：仓里现在这一份是合规的（下面三发之所以能红，只可能来自注入的破坏）。
        Assert.Empty(ReadCopyGuardShape(slice).Problems);

        // ① 变异一：返回类型 `string?` → `object?`（第四百二十轮手工做过的那一处）。
        var mutatedReturn = slice.Replace("string? CopyTarget()", "object? CopyTarget()", StringComparison.Ordinal);
        Assert.NotEqual(slice, mutatedReturn);
        var returnProblems = ReadCopyGuardShape(mutatedReturn).Problems;
        Assert.Single(returnProblems);
        Assert.Contains("定义", returnProblems[0], StringComparison.Ordinal);
        // 旧写法（第四百一十九轮之前那一句）在这一发上照样绿——这就是本轮要留痕的那一处假绿。
        Assert.Equal(3, Regex.Matches(mutatedReturn, @"CopyTarget\(\)").Count - 1);

        // ② 变异二：给定义加一个参数（第四百二十轮手工做过的另一处）。
        var mutatedParam = slice.Replace(
            "string? CopyTarget()", "string? CopyTarget(bool allowEmpty)", StringComparison.Ordinal);
        Assert.NotEqual(slice, mutatedParam);
        var paramProblems = ReadCopyGuardShape(mutatedParam).Problems;
        Assert.Single(paramProblems);
        Assert.Contains("定义", paramProblems[0], StringComparison.Ordinal);

        // ③ 变异三：多一条调用处（多一条没人登记的入口）。
        var extraCall = slice.Replace(
            "copyItem.Enabled = CopyTarget() is not null;",
            "copyItem.Enabled = CopyTarget() is not null;\n            var extra = CopyTarget();",
            StringComparison.Ordinal);
        Assert.NotEqual(slice, extraCall);
        var extraProblems = ReadCopyGuardShape(extraCall).Problems;
        Assert.Single(extraProblems);
        Assert.Contains("调用处", extraProblems[0], StringComparison.Ordinal);
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
